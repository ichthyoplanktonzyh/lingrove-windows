#!/usr/bin/env python3
"""Run the complete pinned Mac archive actor and real memory SQLite; no replacement storage/media policies."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile

from macos_vocabulary_reference import BASELINE, database_archive_sources

_spec = importlib.util.spec_from_file_location('projection_fixture', Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
_builder = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_builder)
if _builder.DRIVER.count('func describe(') != 1: raise RuntimeError('Fixed graph boundary changed')
DRIVER = _builder.DRIVER[:_builder.DRIVER.index('func describe(')] + r'''
extension VocabularyArchive {
    // Test-only fault/interleaving drivers. All load/commit/validation/cache logic stays original.
    func sqlProbe(_ sql: String) throws { try persistence().connection.execute(sql) }
    func diskProbe() throws -> VocabularySnapshot { try persistence().read() }
    func seedProbe(_ value: VocabularyDocument) throws { _ = try persistence().write(value, previous: .init(), expectedRevision: 0) }
    func staleProbe(_ next: VocabularyDocument, winner: VocabularyDocument) throws -> (String, Bool, UInt64) {
        let before = try load()
        _ = try persistence().write(winner, previous: before.document, expectedRevision: before.revision)
        do { _ = try commit(next); return ("unexpected", false, 0) }
        catch let error as VocabularyError { let refreshed = try load(); return (String(describing: error), refreshed.document == winner, refreshed.revision) }
    }
}
func capture(_ usage: VocabularyExample? = nil) -> VocabularyCapture { .init(expression: "bank", dictionaryForm: "bank", language: .english, kind: .word, meaning: "core", example: usage) }
@MainActor func observe(_ mode: String) async -> [String: Any] {
    let archive = VocabularyArchive(url: nil)
    do {
        if mode == "fresh" { let s = try await archive.load(); return ["revision": s.revision, "entries": s.document.entries.count] }
        if mode == "removeMissing" { let s = try await archive.remove(capture()); return ["revision": s.revision, "entries": s.document.entries.count] }
        if mode == "removeMissingEntry" { let s = try await archive.remove(id: id(999)); return ["revision": s.revision, "entries": s.document.entries.count] }
        if mode == "explanationMissing" { let s = try await archive.saveExplanation(record(100, capture())); return ["revision": s.revision, "entries": s.document.entries.count] }
        if mode == "save" || mode == "duplicateSave" {
            let usage = document().entries[0].examples[0], c = capture(usage)
            _ = try await archive.save(c)
            let s = try await (mode == "save" ? archive.load() : archive.save(c))
            return ["revision": s.revision, "entries": s.document.entries.count, "senses": s.document.entries[0].senses.count,
                "usages": s.document.entries[0].examples.count, "usageIDRetained": s.document.entries[0].examples[0].id == usage.id,
                "contains": VocabularyPolicy.contains(c, in: s.document.entries)]
        }
        var old = document()
        if mode == "userExplanation" { old.entries[0].senses[0].examples[0].meaningOrigin = .user }
        if mode == "invalidLoad" {
            var bad = old; bad.entries[0].senses[0].examples[0].ranges = [.init(NSRange(location: 999, length: 1))]
            try await archive.seedProbe(bad)
            do { _ = try await archive.load(); return ["unexpected": true] }
            catch let error as VocabularyError { return ["error": String(describing: error), "preserved": try await archive.diskProbe().document == bad] }
        }
        _ = try await archive.commit(old)
        if mode == "commitBeforeLoad" { let s = try await archive.load(); return ["equal": s.document == old, "revision": s.revision] }
        if mode == "removeStandalone" {
            let s = try await archive.remove(capture()); let sense = s.document.entries[0].senses[0]
            return ["revision": s.revision, "saved": sense.isSavedWithoutContext, "general": sense.explanations.count,
                "usages": sense.examples.count, "note": sense.note]
        }
        if mode == "createRemoveSense" {
            _ = try await archive.createSense(entryID: id(1), senseID: id(4))
            let s = try await archive.removeSense(entryID: id(1), senseID: id(4))
            return ["revision": s.revision, "senses": s.document.entries[0].senses.count, "originalRetained": s.document.entries[0].senses[0].id == id(2)]
        }
        if mode == "moveUsage" {
            _ = try await archive.createSense(entryID: id(1), senseID: id(4))
            let s = try await archive.moveExample(entryID: id(1), exampleID: id(3), source: id(2), target: id(4))
            return ["revision": s.revision, "sourceEmpty": s.document.entries[0].senses[0].examples.isEmpty,
                "ownerRetained": s.document.entries[0].senses[1].examples[0].id == id(3), "records": s.document.entries[0].senses[1].examples[0].explanations.count]
        }
        if mode == "mergeSense" {
            _ = try await archive.createSense(entryID: id(1), senseID: id(4))
            let s = try await archive.mergeSenses(entryID: id(1), source: id(2), target: id(4)); let sense = s.document.entries[0].senses[0]
            return ["revision": s.revision, "senses": s.document.entries[0].senses.count, "alias": sense.mergedFrom.contains(id(2)),
                "owner": sense.id == id(4), "usages": sense.examples.count]
        }
        if mode == "updatePreservesCapture" {
            let usage = VocabularyExample(id: id(9), text: "Another bank.", translation: "", ranges: [.init(NSRange(location: 8, length: 4))], completeness: .sentence, meaning: "", createdAt: now)
            _ = try await archive.save(capture(usage))
            var edited = old.entries[0]; edited.senses[0].note = "edited"
            let s = try await archive.update(edited, basedOn: old.entries[0]); return ["revision": s.revision, "usages": s.document.entries[0].examples.count, "note": s.document.entries[0].senses[0].note]
        }
        if mode == "editConflict" {
            var first = old.entries[0]; first.senses[0].note = "winner"; _ = try await archive.update(first, basedOn: old.entries[0])
            var second = old.entries[0]; second.senses[0].note = "stale"
            do { _ = try await archive.update(second, basedOn: old.entries[0]); return ["unexpected": true] }
            catch let error as VocabularyError { let s = try await archive.load(); return ["error": String(describing: error), "revision": s.revision, "note": s.document.entries[0].senses[0].note] }
        }
        if mode == "explanation" || mode == "invalidExplanation" || mode == "userExplanation" {
            let input = mode == "userExplanation" ? old.entries[0].examples[0].explanations[0].input : old.entries[0].senses[0].explanations[0].input
            let record = LearningExplanationRecord(id: id(101), input: input, language: .chinese,
                result: .init(meaning: mode == "invalidExplanation" ? "" : "generated", explanation: "explanation"), model: "fixture", createdAt: now)
            let s = try await archive.saveExplanation(record)
            let sense = s.document.entries[0].senses[0], usage = sense.examples[0]
            return ["revision": s.revision, "meaning": mode == "userExplanation" ? usage.meaning : sense.meaning,
                "recordID": (mode == "userExplanation" ? usage.explanations.last!.id : sense.explanations.last!.id).uuidString.lowercased()]
        }
        if mode == "missingImage" {
            var edited = old.entries[0]
            edited.senses[0].content = [.init(id: id(21), content: .image(.init(digest: String(repeating: "a", count: 64), width: 2, height: 3, byteCount: 4), caption: ""))]
            do { _ = try await archive.update(edited, basedOn: old.entries[0]); return ["unexpected": true] }
            catch let error as VocabularyError { let s = try await archive.load(); return ["error": String(describing: error), "revision": s.revision, "preserved": s.document == old] }
        }
        var next = old
        if mode == "prune" {
            next.entries[0].senses[0].examples[0].text = "The bank!"
            let s = try await archive.commit(next); return ["revision": s.revision, "general": s.document.entries[0].senses[0].explanations.count, "usage": s.document.entries[0].examples[0].explanations.count]
        }
        if mode == "rollback" {
            try await archive.sqlProbe("CREATE TRIGGER fail_sense BEFORE UPDATE ON senses BEGIN SELECT RAISE(ABORT,'test'); END")
            next.entries[0].senses[0].note = "not committed"
            do { _ = try await archive.commit(next); return ["unexpected": true] }
            catch let error as VocabularyError { let s = try await archive.load(); return ["error": String(describing: error), "revision": s.revision, "cachePreserved": s.document == old, "diskPreserved": try await archive.diskProbe().document == old] }
        }
        if mode == "staleBaseline" {
            next.entries[0].senses[0].note = "stale"; var winner = old; winner.entries[0].senses[0].note = "winner"
            let (error, preserved, revision) = try await archive.staleProbe(next, winner: winner)
            return ["error": error, "preserved": preserved, "revision": revision]
        }
        if mode == "invalidCommit" {
            next.entries[0].senses[0].examples[0].ranges = [.init(NSRange(location: 999, length: 1))]
            do { _ = try await archive.commit(next); return ["unexpected": true] }
            catch let error as VocabularyError { let s = try await archive.load(); return ["error": String(describing: error), "revision": s.revision, "preserved": s.document == old] }
        }
        fatalError("Unknown archive mode")
    } catch { fatalError("Unexpected archive error: \(type(of: error))") }
}
@main struct Driver {
    static func main() async throws {
        let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
        var observations: [[String: Any]] = []
        for row in corpus["cases"] as! [[String: Any]] { observations.append(["id": row["id"]!, "expected": await observe(row["operation"] as! String)]) }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: observations, options: [.sortedKeys]))
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-archive-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Capture actual original archive observations in a new file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if (corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not value for value in ids) or len(set(ids)) != len(ids)):
        parser.error('Archive corpus requires fixed pin and unique IDs')
    if any(row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != 'Cotabby/Services/Vocabulary/VocabularyArchive.swift' for row in rows):
        parser.error('Archive probes require exact source provenance')
    with tempfile.TemporaryDirectory(prefix='lingrove-archive-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(database_archive_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'archive-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-parse-as-library', '-O', '-lsqlite3', *inputs, str(driver), '-o', str(binary)], check=True)
        observed = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output: json.dump(observed, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observed)} original archive actor observations'); return
    if observed != [{'id': row['id'], 'expected': row['expected']} for row in rows]: raise SystemExit('Original archive observation mismatch')
    print(f'Verified {len(observed)} cases against the complete original Mac archive actor')


if __name__ == '__main__': main()
