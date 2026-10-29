#!/usr/bin/env python3
"""Run the unchanged committed Swift verifiers with at most two owned processes.

Only scheduling changes: every manifest command runs once, all failures gate CI, and
each verifier retains its own source pin, fixture, temporary files and assertions.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "scripts/macos-parity-suite.json"


@dataclass(frozen=True)
class Task:
    name: str
    script: str
    mac_repo: str


@dataclass(frozen=True)
class Result:
    task: Task
    code: int
    seconds: float
    output: str


def load_tasks(path=MANIFEST):
    value = json.loads(Path(path).read_text(encoding="utf-8"))
    if set(value) != {"version", "tasks"} or value["version"] != 1:
        raise ValueError("Unsupported parity manifest")
    if not isinstance(value["tasks"], list) or not 1 <= len(value["tasks"]) <= 128:
        raise ValueError("Invalid parity task count")
    tasks, scripts, names = [], set(), set()
    for row in value["tasks"]:
        if not isinstance(row, dict) or set(row) != {"name", "script", "mac_repo"}:
            raise ValueError("Invalid parity task shape")
        if not all(isinstance(v, str) for v in row.values()):
            raise ValueError("Invalid parity task value")
        task = Task(**row)
        if (not task.name or len(task.name) > 200 or any(ord(c) < 32 for c in task.name)
                or not re.fullmatch(r"scripts/verify-(?:[a-z0-9-]+-)?macos-parity\.py", task.script)
                or task.mac_repo not in {".mac-baseline", ".mac-current-editor"}
                or task.script in scripts or task.name in names):
            raise ValueError("Invalid or duplicated parity task")
        if not (ROOT / task.script).is_file():
            raise ValueError("Missing verifier: " + task.script)
        tasks.append(task)
        scripts.add(task.script)
        names.add(task.name)
    return tasks


class Children:
    """Own only processes created here; macOS cancellation also reaps their group.

    Verifiers use isolated temporary directories and UUID domains. They are never
    killed by executable name, and normal completion does not touch sibling work.
    """
    def __init__(self):
        self._gate = threading.Lock()
        self._children = set()
        self._stopping = False

    def run(self, task):
        started = time.monotonic()
        with tempfile.TemporaryFile() as output:
            process = None
            try:
                with self._gate:
                    if self._stopping:
                        return Result(task, 130, 0, "Cancelled before launch")
                    process = subprocess.Popen(
                        [sys.executable, str(ROOT / task.script), "--mac-repo", str(ROOT / task.mac_repo)],
                        cwd=ROOT, stdout=output, stderr=subprocess.STDOUT,
                        start_new_session=os.name != "nt")
                    self._children.add(process)
                code = process.wait()
            except OSError as error:
                return Result(task, 1, time.monotonic() - started, str(error))
            finally:
                if process is not None:
                    with self._gate:
                        self._children.discard(process)
            # Existing verifiers normally emit one line. Preserve diagnostics without
            # allowing a broken compiler to exhaust the scheduler's memory.
            size = output.tell()
            output.seek(max(0, size - 1024 * 1024))
            text = output.read().decode("utf-8", errors="replace")
            if size > 1024 * 1024:
                text = "[Earlier output truncated; last 1 MiB follows]\n" + text
            return Result(task, code, time.monotonic() - started, text)

    def stop(self):
        with self._gate:
            self._stopping = True
            processes = tuple(self._children)
        for process in processes:
            try:
                if os.name == "nt":
                    process.terminate()
                else:
                    os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
        for process in processes:
            try:
                process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                pass
            # A leader can exit before its compiler/helper descendants. Kill the
            # owned macOS group even after the leader exits, then reap the leader.
            try:
                if os.name == "nt":
                    if process.poll() is None:
                        process.kill()
                else:
                    os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()


def run_suite(tasks, jobs, run, emit, stop=lambda: None):
    if jobs not in (1, 2):
        raise ValueError("Only one or two concurrent verifiers are supported")
    pool = ThreadPoolExecutor(max_workers=jobs)
    futures, results = {}, []
    try:
        futures = {pool.submit(run, task): task for task in tasks}
        for future in as_completed(futures):
            task = futures[future]
            try:
                result = future.result()
            except Exception as error:
                result = Result(task, 1, 0, str(error))
            results.append(result)
            emit(result)
    except BaseException:
        stop()
        for future in futures:
            future.cancel()
        raise
    finally:
        pool.shutdown(wait=True, cancel_futures=True)
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jobs", type=int, choices=(1, 2), default=2)
    parser.add_argument("--list", action="store_true", help="Print every task without executing it")
    args = parser.parse_args()
    tasks = load_tasks()
    if args.list:
        for task in tasks:
            print(f"{task.script} --mac-repo {task.mac_repo}")
        return 0
    if sys.platform != "darwin":
        parser.error("The Swift source suite requires macOS; --list and scheduler unit tests are portable")
    children = Children()
    started = time.monotonic()
    completed = 0

    def emit(result):
        nonlocal completed
        completed += 1
        print(f"::group::[{completed}/{len(tasks)}] {result.task.name} ({result.seconds:.1f}s; exit {result.code})", flush=True)
        print(result.output, end="" if result.output.endswith("\n") else "\n", flush=True)
        print("::endgroup::", flush=True)

    try:
        def interrupted(_signal, _frame):
            # A second cancellation signal must not interrupt owned-child cleanup.
            signal.signal(signal.SIGINT, signal.SIG_IGN)
            signal.signal(signal.SIGTERM, signal.SIG_IGN)
            raise KeyboardInterrupt
        signal.signal(signal.SIGINT, interrupted)
        signal.signal(signal.SIGTERM, interrupted)
        results = run_suite(tasks, args.jobs, children.run, emit, children.stop)
    except KeyboardInterrupt:
        return 130
    failed = [result for result in results if result.code != 0]
    print(f"Completed all {len(results)}/{len(tasks)} verifiers with {args.jobs} workers in {time.monotonic() - started:.1f}s; {len(failed)} failed", flush=True)
    for result in failed:
        print("FAILED: " + result.task.script, flush=True)
    return 1 if failed or len(results) != len(tasks) else 0


if __name__ == "__main__":
    sys.exit(main())
