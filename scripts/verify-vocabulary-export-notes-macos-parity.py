#!/usr/bin/env python3
"""Execute actual pinned Mac TXT/CSV rendering; no JSON/package/archive/UI claim."""
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
func renderProbe(_ row: [String: Any]) throws -> Data {
    let c = configuration(row), mode = row["mode"] as! String
    var entries = exportEntries()
    switch mode {
    case "empty": entries = []
    case "noExamples": for i in entries.indices { for j in entries[i].senses.indices { entries[i].senses[j].examples = [] } }
    case "manyExamples":
        var usage = entries[0].senses[0].examples[0]; usage = .init(id: id(8), text: usage.text, translation: usage.translation, ranges: usage.ranges, completeness: usage.completeness, meaning: usage.meaning, createdAt: now)
        entries[0].senses[0].examples.append(usage)
    case "unicodeQuotes":
        entries[0].expression = "=😀,\"語\"\nform"
        entries[0].senses[0].meaningLanguage = .chinese
        entries[0].senses[0].note = " \t=1,\"note\"\r\nline"; entries[0].senses[0].tags = ["A", "B;C"]
        entries[0].senses[0].examples[0].text = "😀,\"bank\"\nline"
        entries[0].senses[0].examples[0].translationLanguage = .chinese
        entries[0].senses[0].examples[0].meaningLanguage = .english
    case "normalizedList":
        entries[0].expression = "  café\u{A0}world "; entries[1].expression = "cafe\u{301} world"
        entries.append(.init(id: id(10), expression: "café world", dictionaryForm: "bank", language: .french, kind: .word, senses: [], createdAt: now, updatedAt: now))
        entries.append(.init(id: id(12), expression: "Café world", dictionaryForm: "bank", language: .english, kind: .word, senses: [], createdAt: now, updatedAt: now))
    case "canonicalForm": entries[0].expression = "é"; entries[0].dictionaryForm = "e\u{301}"
    case "emptyTextBlock": entries[0].senses[0].content = [.init(id: id(24), content: .text(.init()))]
    case "dates":
        let s = entries[0].senses[0], u = s.examples[0]
        let usage = VocabularyExample(id: u.id, text: u.text, translation: u.translation, translationLanguage: u.translationLanguage,
            translationVariants: u.translationVariants, ranges: u.ranges, completeness: u.completeness, meaning: u.meaning,
            meaningLanguage: u.meaningLanguage, meaningOrigin: u.meaningOrigin, appBundleIdentifier: u.appBundleIdentifier,
            createdAt: Date(timeIntervalSince1970: 1.875), explanations: u.explanations)
        entries[0].senses[0] = .init(id: s.id, meaning: s.meaning, meaningLanguage: s.meaningLanguage,
            note: s.note, tags: s.tags, examples: [usage], standaloneMeanings: s.standaloneMeanings,
            explanations: s.explanations, alternateMeanings: s.alternateMeanings, content: s.content,
            createdAt: Date(timeIntervalSince1970: -0.125), updatedAt: s.updatedAt)
    default: break
    }
    return try VocabularyExportFormatter.render(VocabularyExportPolicy.document(entries, configuration: c), configuration: c)
}
func observeRender(_ row: [String: Any]) async -> [String: Any] {
    if row["cancelled"] as? Bool == true {
        // Capture only Sendable values across the task boundary; it invokes the real render loop.
        let format = row["format"] as! String, mode = row["mode"] as! String
        let task = Task { () -> Data? in
            do { try await Task.sleep(nanoseconds: 1_000_000_000) } catch { }
            do { return try renderProbe(["format": format, "mode": mode]) }
            catch is CancellationError { return nil }
            catch { fatalError("Unexpected cancelled render error") }
        }
        task.cancel()
        if let data = await task.value { return ["text": String(decoding: data, as: UTF8.self), "byteCount": data.count] }
        return ["error": "cancelled"]
    }
    do { let data = try renderProbe(row); return ["text": String(decoding: data, as: UTF8.self), "byteCount": data.count] }
    catch let error as VocabularyExportError { return ["error": String(describing: error)] }
    catch { fatalError("Unexpected render error") }
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
var results = [[String: Any]]()
for row in corpus["cases"] as! [[String: Any]] { results.append(["id": row["id"]!, "expected": await observeRender(row)]) }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-export-notes-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not v for v in ids) or len(set(ids)) != len(ids): parser.error('Renderer probes require pinned version and unique IDs')
    if any(row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != 'Cotabby/Support/Vocabulary/VocabularyExportFormatter.swift' for row in rows): parser.error('Renderer probes require exact source provenance')
    if not args.capture_output and any('expected' not in row for row in rows): parser.error('Every render probe needs captured expected values')
    with tempfile.TemporaryDirectory(prefix='lingrove-export-notes-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(export_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'export-notes-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output: json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac TXT/CSV observations'); return
    if observations != [{'id': r['id'], 'expected': r['expected']} for r in rows]:
        bad = next((r['id'] for r, o in zip(rows, observations) if o != {'id': r['id'], 'expected': r['expected']}), 'count')
        raise SystemExit(f'Mac TXT/CSV mismatch: {bad}')
    print(f'Verified {len(rows)} byte-exact TXT/CSV cases against actual pinned Mac formatter')


if __name__ == '__main__': main()
