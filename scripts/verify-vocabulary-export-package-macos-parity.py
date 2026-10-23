#!/usr/bin/env python3
"""Execute original Mac package factory/value ownership without archive, media IO or UI."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, export_sources

_spec = importlib.util.spec_from_file_location('export_inputs', Path(__file__).with_name('verify-vocabulary-export-projection-macos-parity.py'))
_inputs = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_inputs)
if _inputs.DRIVER.count('func observe(') != 1: raise RuntimeError('Fixed export input boundary must be unique')
DRIVER = _inputs.DRIVER.split('func observe(')[0] + r'''
import CryptoKit
func facts(_ p: VocabularyExportPackage, configuration: VocabularyExportConfiguration) -> [String: Any] {
    ["format": p.configuration.format.rawValue, "sameConfiguration": p.configuration == configuration,
        "idNonzero": p.id != UUID(uuidString: "00000000-0000-0000-0000-000000000000")!,
        "dataFilename": p.dataFilename, "filename": p.filename, "byteCount": p.data.count,
        "sha256": SHA256.hash(data: p.data).map { String(format: "%02x", $0) }.joined(), "preview": p.preview,
        "entryCount": p.entryCount, "senseCount": p.senseCount, "exampleCount": p.exampleCount, "imageCount": p.images.count]
}
func observePackage(_ row: [String: Any]) async -> [String: Any] {
    let c = configuration(row), mode = row["mode"] as! String
    var entries = exportEntries()
    if mode == "empty" { entries = [] }
    if mode == "longPreview" { entries[0].expression = String(repeating: "拾", count: 6000) }
    if mode == "manyExamples" {
        let u = entries[0].senses[0].examples[0]
        entries[0].senses[0].examples.append(.init(id: id(8), text: u.text, translation: u.translation, ranges: u.ranges, completeness: u.completeness, meaning: u.meaning, createdAt: now))
    }
    if row["cancelled"] as? Bool == true {
        let captured = entries
        let task = Task { () -> VocabularyExportPackage? in
            do { try await Task.sleep(nanoseconds: 1_000_000_000) } catch { }
            do { return try VocabularyExportPolicy.package(captured, configuration: c) }
            catch is CancellationError { return nil }
            catch { fatalError("Unexpected cancelled package error") }
        }
        task.cancel()
        if let p = await task.value { return facts(p, configuration: c) }
        return ["error": "cancelled"]
    }
    do {
        let p = try VocabularyExportPolicy.package(entries, configuration: c)
        if mode == "ownership" {
            var exposedData = p.data; exposedData[0] ^= 0xFF
            var bytes = Data([1, 2, 3]), input = ["image.png": Data([1, 2, 3])]
            var attached = p; attached.images = input
            bytes[0] = 9; input["image.png"] = bytes
            var detached = attached.images; detached["image.png"]![1] = 8
            let second = try VocabularyExportPolicy.package(entries, configuration: c)
            return ["original": facts(p, configuration: c), "attached": facts(attached, configuration: c), "sameID": p.id == attached.id,
                "differentPreparationID": second.id != p.id, "attachedBytes": Array(attached.images["image.png"]!),
                "detachedBytes": Array(detached["image.png"]!), "dataUnchanged": exposedData != p.data]
        }
        return facts(p, configuration: c)
    } catch let error as VocabularyExportError { return ["error": String(describing: error)] }
    catch { fatalError("Unexpected package error") }
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
var results = [[String: Any]]()
for row in corpus["cases"] as! [[String: Any]] { results.append(["id": row["id"]!, "expected": await observePackage(row)]) }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-export-package-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']; ids = [r.get('id') for r in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not v for v in ids) or len(set(ids)) != len(ids): parser.error('Package probes require pinned version and unique IDs')
    for row in rows:
        expected_source = 'Cotabby/Models/Vocabulary/VocabularyExportModels.swift' if row['mode'] == 'ownership' else 'Cotabby/Support/Vocabulary/VocabularyExportPolicy.swift'
        if row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != expected_source: parser.error('Package probes require exact source provenance')
    if not args.capture_output and any('expected' not in r for r in rows): parser.error('Every package probe needs captured expected values')
    with tempfile.TemporaryDirectory(prefix='lingrove-export-package-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(export_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'export-package-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output: json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac export package observations'); return
    if observations != [{'id': r['id'], 'expected': r['expected']} for r in rows]:
        bad = next((r['id'] for r, o in zip(rows, observations) if o != {'id': r['id'], 'expected': r['expected']}), 'count')
        raise SystemExit(f'Mac package mismatch: {bad}')
    print(f'Verified {len(rows)} package/count/ownership cases against actual pinned Mac methods')


if __name__ == '__main__': main()
