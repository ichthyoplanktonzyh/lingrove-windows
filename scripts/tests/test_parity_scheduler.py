import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import run_macos_parity_suite as suite


class ManifestTests(unittest.TestCase):
    def test_all_68_original_commands_and_reference_roots_are_preserved(self):
        tasks = suite.load_tasks()
        commands = "\n".join(task.script + " --mac-repo " + task.mac_repo for task in tasks)
        self.assertEqual(68, len(tasks))
        self.assertEqual("2d2053af74fe3fc5bedad2a8b366b96dd1d3be8dcdea49b00e11f09a93379323",
                         hashlib.sha256(commands.encode()).hexdigest())
        self.assertEqual(1, sum(task.mac_repo == ".mac-current-editor" for task in tasks))

    def test_duplicate_or_changed_command_shapes_are_rejected(self):
        task = suite.load_tasks()[0]
        row = {"name": task.name, "script": task.script, "mac_repo": task.mac_repo}
        cases = [[row, row], [dict(row, script="../verify-macos-parity.py")],
                 [dict(row, mac_repo=".mac-latest")], [dict(row, extra="--capture-output")],
                 [dict(row, name="invalid\nname")], []]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            for rows in cases:
                with self.subTest(rows=rows):
                    path.write_text(json.dumps({"version": 1, "tasks": rows}))
                    with self.assertRaises(ValueError):
                        suite.load_tasks(path)


class SchedulingTests(unittest.TestCase):
    def test_two_workers_overlap_and_every_failure_is_retained(self):
        tasks = [suite.Task(str(i), str(i), "fake") for i in range(8)]
        gate, lock = threading.Barrier(2, timeout=5), threading.Lock()
        active = maximum = 0
        calls, emitted = [], []

        def run(task):
            nonlocal active, maximum
            with lock:
                active += 1
                maximum = max(active, maximum)
                calls.append(task)
            try:
                if int(task.name) < 2:
                    gate.wait()
                time.sleep(.01)
                if task.name == "2":
                    raise RuntimeError("synthetic verifier failure")
                return suite.Result(task, 7 if task.name == "5" else 0, 0, task.name)
            finally:
                with lock:
                    active -= 1

        results = suite.run_suite(tasks, 2, run, emitted.append)
        self.assertEqual(2, maximum)
        self.assertCountEqual(tasks, calls)
        self.assertEqual(8, len(emitted))
        self.assertEqual({"2", "5"}, {row.task.name for row in results if row.code})

    def test_serial_mode_and_invalid_concurrency(self):
        tasks = [suite.Task(str(i), str(i), "fake") for i in range(3)]
        calls = []
        def run(task):
            calls.append(task)
            return suite.Result(task, 0, 0, "ok")
        self.assertEqual(3, len(suite.run_suite(tasks, 1, run, lambda _: None)))
        self.assertEqual(tasks, calls)
        for jobs in (0, 3, 16):
            with self.assertRaises(ValueError):
                suite.run_suite(tasks, jobs, run, lambda _: None)

    def test_aborted_collection_stops_and_joins_accepted_workers(self):
        stopped, entered = threading.Event(), threading.Event()
        tasks = [suite.Task(str(i), str(i), "fake") for i in range(20)]
        active = 0
        lock = threading.Lock()
        def run(task):
            nonlocal active
            with lock:
                active += 1
            try:
                if task.name == "0":
                    self.assertTrue(entered.wait(5))
                else:
                    entered.set()
                    self.assertTrue(stopped.wait(5))
                return suite.Result(task, 0, 0, "ok")
            finally:
                with lock:
                    active -= 1
        def abort(_):
            raise KeyboardInterrupt
        with self.assertRaises(KeyboardInterrupt):
            suite.run_suite(tasks, 2, run, abort, stopped.set)
        self.assertTrue(stopped.is_set())
        self.assertEqual(0, active)

    def test_owned_real_child_is_joined_on_stop(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "helper.py").write_text("from pathlib import Path\nimport time\nPath('ready').write_text('ready')\ntime.sleep(60)\n")
            children = suite.Children()
            results = []
            with patch.object(suite, "ROOT", root):
                worker = threading.Thread(target=lambda: results.append(children.run(suite.Task("owned", "helper.py", "unused"))))
                worker.start()
                try:
                    deadline = time.monotonic() + 10
                    while not (root / "ready").exists() and time.monotonic() < deadline:
                        time.sleep(.01)
                    self.assertTrue((root / "ready").exists())
                finally:
                    children.stop()
                    worker.join(10)
                self.assertFalse(worker.is_alive())
                self.assertEqual(1, len(results))
                self.assertNotEqual(0, results[0].code)
                self.assertFalse(children._children)


if __name__ == "__main__":
    unittest.main()
