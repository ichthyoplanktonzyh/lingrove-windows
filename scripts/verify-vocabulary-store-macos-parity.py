#!/usr/bin/env python3
"""Execute original Mac UI-store methods with the complete real archive, without models/UI windows."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, vocabulary_store_sources

_spec = importlib.util.spec_from_file_location('projection_fixture', Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
_builder = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_builder)
if _builder.DRIVER.count('func describe(') != 1: raise RuntimeError('Fixed graph boundary changed')
DRIVER = _builder.DRIVER[:_builder.DRIVER.index('func describe(')] + r'''
actor StoreProbeGate {
    var open = false; var pending: [CheckedContinuation<Void, Never>] = []
    func wait() async { if !open { await withCheckedContinuation { pending.append($0) } } }
    func release() { open = true; for item in pending { item.resume() }; pending.removeAll() }
}
@MainActor func observe(_ mode: String) async -> [String: Any] {
    let archive = VocabularyArchive(url: nil), old = document()
    let store = VocabularyStore(archive: archive)
    let capture = VocabularyCapture(expression: "bank", dictionaryForm: "bank", language: .english, kind: .word, meaning: "core")
    if mode == "fresh" {
        let before = store.isLoaded; await store.load()
        return ["before": before, "loaded": store.isLoaded, "entries": store.entries.count, "contains": store.contains(capture)]
    }
    if mode == "save" {
        var usage = capture; usage.example = old.entries[0].examples[0]; await store.save(usage)
        return ["containsUsage": store.contains(usage), "containsStandalone": store.contains(capture), "loaded": store.isLoaded, "pending": store.isPending(usage)]
    }
    if mode == "enrich" {
        let record = record(101, capture)
        store.enrichCapture = { value in var value = value; value.explanation = record; return value }
        await store.save(capture)
        return ["meaning": store.entries[0].senses[0].meaning, "record": store.explanation(for: record.input, language: .chinese)?.id == record.id]
    }
    if mode == "monotonic" || mode == "equalRevision" {
        store.applyProbe(.init(revision: 4, document: old))
        store.applyProbe(.init(revision: mode == "monotonic" ? 3 : 4, document: .init()))
        return ["entries": store.entries.count, "contains": store.contains(capture), "loaded": store.isLoaded]
    }
    if mode == "pending" {
        let first = StoreProbeGate(), second = StoreProbeGate()
        let a = Task { await store.controlledChangeProbe(capture, gate: first) }
        let b = Task { await store.controlledChangeProbe(capture, gate: second) }
        for _ in 0..<100_000 { if store.pendingCountProbe(capture) == 2 { break }; await Task.yield() }
        guard store.pendingCountProbe(capture) == 2 else { fatalError("Both pending operations must be admitted") }
        let before = store.isPending(capture)
        await first.release(); await a.value
        let middle = store.isPending(capture)
        await second.release(); await b.value
        return ["before": before, "middle": middle, "after": store.isPending(capture), "saved": store.contains(capture)]
    }
    if mode == "invalidSave" {
        let bad = VocabularyCapture(expression: "", dictionaryForm: "", language: .english, kind: .word, meaning: "core"); await store.save(bad)
        return ["error": store.errorMessage != nil, "pending": store.isPending(bad), "loaded": store.isLoaded, "entries": store.entries.count]
    }
    do {
        _ = try await archive.commit(old); await store.load()
        if mode == "remove" { await store.remove(capture); return ["contains": store.contains(capture), "usages": store.entries[0].examples.count, "note": store.entries[0].senses[0].note] }
        let input = old.entries[0].senses[0].explanations[0].input
        if mode == "lookup" { return ["same": store.explanation(for: input, language: .chinese)?.id == id(100), "wrongLanguage": store.explanation(for: input, language: .english) == nil] }
        var usage = capture; usage.example = old.entries[0].examples[0]
        let standaloneDestination = store.explanationDestination(capture)!, usageDestination = store.explanationDestination(usage)!
        if mode == "destination" { return ["entry": standaloneDestination.entryID == id(1), "sense": standaloneDestination.senseID == id(2), "noUsage": standaloneDestination.exampleID == nil, "usage": usageDestination.exampleID == id(3), "capturedUserMeaning": store.userMeaning(at: usageDestination) == nil] }
        let usageRecord = LearningExplanationRecord(id: id(101), input: old.entries[0].examples[0].explanations[0].input,
            language: .chinese, result: .init(meaning: "adopted", explanation: "explanation"), model: "fixture", createdAt: now)
        if mode == "adoptStandalone" {
            let record = LearningExplanationRecord(id: id(101), input: input, language: .chinese, result: .init(meaning: "adopted", explanation: "explanation"), model: "fixture", createdAt: now)
            let ok = await store.adoptExplanation(record, at: standaloneDestination)
            return ["ok": ok, "meaning": store.userMeaning(at: standaloneDestination) ?? "missing", "origin": store.entries[0].senses[0].meaningOrigin.rawValue]
        }
        if mode == "adoptMoved" {
            _ = try await archive.createSense(entryID: id(1), senseID: id(4))
            _ = await store.moveExample(entryID: id(1), exampleID: id(3), source: id(2), target: id(4))
            let ok = await store.adoptExplanation(usageRecord, at: usageDestination)
            return ["ok": ok, "owner": store.entries[0].senses[1].examples[0].id == id(3), "meaning": store.userMeaning(at: usageDestination) ?? "missing", "sourceEmpty": store.entries[0].senses[0].examples.isEmpty]
        }
        if mode == "adoptDeleted" { await store.remove(usage) }
        if mode == "adoptStale" {
            var edited = store.entries[0]; edited.senses[0].examples[0].text = "The bank!"
            _ = await store.update(edited, basedOn: store.entries[0])
        }
        if mode == "adoptDeleted" || mode == "adoptStale" {
            let ok = await store.adoptExplanation(usageRecord, at: usageDestination)
            return ["ok": ok, "usages": store.entries[0].examples.count, "error": store.errorMessage != nil]
        }
        if mode == "senseAlias" {
            _ = try await archive.createSense(entryID: id(1), senseID: id(4))
            _ = await store.mergeSenses(entryID: id(1), source: id(2), target: id(4))
            var explicit = capture; explicit.senseID = id(2)
            return ["contains": store.contains(explicit), "destination": store.explanationDestination(explicit)?.senseID == id(4)]
        }
        if mode == "failureThenSuccess" {
            _ = await store.removeSense(entryID: id(999), senseID: id(888)); let error = store.errorMessage != nil
            await store.save(capture)
            return ["beforeError": error, "afterError": store.errorMessage != nil, "saved": store.contains(capture)]
        }
        fatalError("Unknown store probe")
    } catch { fatalError("Unexpected store probe error: \(type(of: error))") }
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
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-store-v1.json')
    parser.add_argument('--capture-output', type=Path)
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text()); rows = corpus['cases']; ids = [r.get('id') for r in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or len(set(ids)) != len(ids) or any(not i for i in ids): parser.error('Store corpus requires fixed pin and unique IDs')
    if any(r.get('provenanceKind') != 'source-policy-extension' or r.get('macSource') != 'Cotabby/Services/Vocabulary/VocabularyStore.swift' for r in rows): parser.error('Store probes require exact provenance')
    with tempfile.TemporaryDirectory(prefix='lingrove-store-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(vocabulary_store_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER); binary = root / 'store-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-parse-as-library', '-O', '-lsqlite3', *inputs, str(driver), '-o', str(binary)], check=True)
        observed = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x') as output: json.dump(observed, output, indent=2, ensure_ascii=False); output.write('\n')
        print(f'Captured {len(observed)} original Mac store observations'); return
    if observed != [{'id': r['id'], 'expected': r['expected']} for r in rows]: raise SystemExit('Original store observation mismatch')
    print(f'Verified {len(observed)} cases against original Mac store methods and actual archive')


if __name__ == '__main__': main()
