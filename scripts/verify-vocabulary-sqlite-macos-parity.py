#!/usr/bin/env python3
"""Run original pinned Mac SQLite classes against isolated native in-memory databases."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile

from macos_vocabulary_reference import BASELINE, database_sqlite_sources

_spec = importlib.util.spec_from_file_location('projection_fixture', Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
_builder = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_builder)
if _builder.DRIVER.count('func describe(') != 1: raise RuntimeError('Projection fixture boundary changed')
DRIVER = _builder.DRIVER[:_builder.DRIVER.index('func describe(')] + r'''
func observe(_ mode: String) -> [String: Any] {
    do {
        let db = try VocabularySQLiteDatabase(url: nil), old = document()
        let c = db.connection
        if mode == "fresh" { return ["revision": try db.read().revision, "entries": try db.read().document.entries.count,
            "schema": try c.rows("PRAGMA user_version")[0][0].integer!, "applicationID": try c.rows("PRAGMA application_id")[0][0].integer!] }
        _ = try db.write(old, previous: .init(), expectedRevision: 0)
        if mode == "roundtrip" { return ["equal": try db.read().document == old, "revision": try db.read().revision] }
        var next = old
        if mode == "noteEdit" {
            let bytes = try c.rows("SELECT payload FROM usages")[0][0].data!
            next.entries[0].senses[0].note = "edited"
            _ = try db.write(next, previous: old, expectedRevision: 1)
            return ["equal": try db.read().document == next, "written": db.lastWrittenRows, "usageUnchanged": try c.rows("SELECT payload FROM usages")[0][0].data == bytes]
        }
        if mode == "noop" { return ["revision": try db.write(old, previous: old, expectedRevision: 1), "written": db.lastWrittenRows] }
        if mode == "moveUsage" || mode == "moveReview" {
            var target = VocabularySense(id: id(4), meaning: "", createdAt: now, updatedAt: now)
            if mode == "moveUsage" { target.examples = next.entries[0].senses[0].examples }
            else { target.review = next.entries[0].senses[0].review; target.reviewCards = next.entries[0].senses[0].reviewCards }
            next.entries[0].senses = [target]
            _ = try db.write(next, previous: old, expectedRevision: 1)
            return ["equal": try db.read().document == next, "revision": try db.read().revision]
        }
        if mode == "replaceEntry" {
            let e = old.entries[0]
            next.entries[0] = .init(id: id(10), expression: e.expression, dictionaryForm: e.dictionaryForm, language: e.language, kind: e.kind,
                senses: e.senses, createdAt: e.createdAt, updatedAt: e.updatedAt)
            _ = try db.write(next, previous: old, expectedRevision: 1)
            return ["equal": try db.read().document == next, "revision": try db.read().revision]
        }
        if mode == "stale" {
            next.entries[0].senses[0].note = "winner"; _ = try db.write(next, previous: old, expectedRevision: 1)
            do { _ = try db.write(old, previous: old, expectedRevision: 1); return ["unexpected": true] }
            catch let error as VocabularyError { return ["error": String(describing: error), "preserved": try db.read().document == next, "revision": try db.read().revision] }
        }
        if mode == "overflow" {
            try c.execute("UPDATE state SET revision=?", [.integer(Int64.max)])
            do { _ = try db.write(old, previous: old, expectedRevision: UInt64(Int64.max)); return ["unexpected": true] }
            catch let error as VocabularyError { return ["error": String(describing: error), "preserved": try db.read().document == old] }
        }
        if mode == "rollback" {
            try c.execute("CREATE TRIGGER fail_usage BEFORE UPDATE ON usages BEGIN SELECT RAISE(ABORT,'test'); END")
            next.entries[0].expression = "changed"; next.entries[0].senses[0].examples[0].translation = "changed"
            do { _ = try db.write(next, previous: old, expectedRevision: 1); return ["unexpected": true] }
            catch let error as VocabularyError { return ["error": String(describing: error), "preserved": try db.read().document == old, "revision": try db.read().revision] }
        }
        if mode == "receipt" || mode == "receiptRollback" {
            let receipt = VocabularyCLIReceipt(key: "key", fingerprint: "fingerprint", result: Data([0, 1, 255]))
            _ = try db.write(old, previous: old, expectedRevision: 1, receipt: receipt)
            if mode == "receipt" { let found = try db.receipt(key: "key")!; return ["matched": found.fingerprint == receipt.fingerprint && found.result == receipt.result,
                "missing": try db.receipt(key: "missing") == nil, "revision": try db.read().revision] }
            next.entries[0].senses[0].note = "not committed"
            do { _ = try db.write(next, previous: old, expectedRevision: 2, receipt: receipt); return ["unexpected": true] }
            catch let error as VocabularyError { return ["error": String(describing: error), "preserved": try db.read().document == old, "revision": try db.read().revision] }
        }
        let statements = ["payloadCorrupt": "UPDATE usages SET payload=x'7b'", "payloadText": "UPDATE usages SET payload='{}'",
            "positionGap": "UPDATE usages SET position=1", "positionReal": "UPDATE usages SET position=0.5",
            "indexedCorrupt": "UPDATE senses SET meaning='tampered'", "templateFlag": "UPDATE state SET has_templates=0"]
        if let sql = statements[mode] {
            try c.execute(sql)
            let before = try c.rows("SELECT payload FROM usages")[0][0]
            do { _ = try db.read(); return ["unexpected": true] }
            catch let error as VocabularyError { return ["error": String(describing: error), "preserved": try c.rows("SELECT payload FROM usages")[0][0] == before, "revision": try db.revision()] }
        }
        if mode == "foreignKey" {
            do { try c.execute("UPDATE usages SET parent_id='orphan'"); return ["unexpected": true] }
            catch let error as VocabularyError { return ["error": String(describing: error), "preserved": try db.read().document == old] }
        }
        if mode == "nestedAccess" {
            let shared = try db.access(write: false) { try db.access(write: false) { 1 } }
            let exclusive = try db.access(write: true) { try db.access(write: true) { 2 } }
            do { _ = try db.access(write: false) { try db.access(write: true) { 3 } }; return ["unexpected": true] }
            catch let error as VocabularyError { return ["shared": shared, "exclusive": exclusive, "error": String(describing: error)] }
        }
        fatalError("Unknown native SQLite mode")
    } catch { fatalError("Unexpected SQLite failure: \(type(of: error))") }
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let results = (corpus["cases"] as! [[String: Any]]).map { row in ["id": row["id"]!, "expected": observe(row["operation"] as! String)] }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-sqlite-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Capture actual native observations in a new file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if (corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not item for item in ids) or len(set(ids)) != len(ids)):
        parser.error('SQLite corpus requires pinned version and unique IDs')
    if any(row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != 'Cotabby/Services/Vocabulary/VocabularySQLiteDatabase.swift' for row in rows):
        parser.error('SQLite cases require exact source provenance')
    with tempfile.TemporaryDirectory(prefix='lingrove-sqlite-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(database_sqlite_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'sqlite-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', '-lsqlite3', *inputs, str(driver), '-o', str(binary)], check=True)
        observed = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output: json.dump(observed, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observed)} original Mac SQLite observations'); return
    if observed != [{'id': row['id'], 'expected': row['expected']} for row in rows]: raise SystemExit('Native SQLite observation mismatch')
    print(f'Verified {len(observed)} cases against original pinned Mac SQLite implementation')


if __name__ == '__main__': main()
