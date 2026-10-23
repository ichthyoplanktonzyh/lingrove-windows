#!/usr/bin/env python3
"""Execute the pinned JSON encoder and whole-export byte guard; no archive/file/UI claim."""
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
func jsonProbe(_ row: [String: Any]) throws -> Data {
    let c = configuration(row), mode = row["mode"] as! String
    if mode.hasPrefix("budget") {
        func entries(_ expression: String) -> [VocabularyEntry] {
            [.init(id: id(1), expression: expression, dictionaryForm: "", language: .english, kind: .word, senses: [], createdAt: now, updatedAt: now)]
        }
        let overhead = try VocabularyExportPolicy.package(entries(""), configuration: c).data.count
        let expression = String(repeating: "a", count: VocabularyExportPolicy.maximumDataBytes - overhead + (mode == "budgetOver" ? 1 : 0))
        return try VocabularyExportPolicy.package(entries(expression), configuration: c).data
    }
    if mode == "uuidLetters" {
        return try VocabularyExportFormatter.render(.init(entries: [.init(id: id(0xabcdef), expression: "literal", dictionaryForm: "literal", language: .english, kind: .word, senses: [])]), configuration: c)
    }
    if mode == "dates" {
        let usage = VocabularyExportDocument.Usage(id: id(3), createdAt: Date(timeIntervalSince1970: 1.875), text: "😀 bank", completeness: .fragment,
            ranges: [.init(NSRange(location: 3, length: 4))], translation: "", translationLanguage: nil, otherTranslations: [], meaning: nil, meaningLanguage: nil, explanations: nil)
        let sense = VocabularyExportDocument.Sense(id: id(2), createdAt: Date(timeIntervalSince1970: -0.125), meaning: "", meaningLanguage: nil,
            otherMeanings: [], note: "", tags: [], examples: [usage], explanations: nil, content: [])
        return try VocabularyExportFormatter.render(.init(entries: [.init(id: id(1), expression: "bank", dictionaryForm: "bank", language: .english, kind: .word, senses: [sense])]), configuration: c)
    }
    var entries = exportEntries()
    switch mode {
    case "empty": entries = []
    case "unicode": entries[0].expression = "\u{FEFF}\0\u{1}\u{1A}\u{1F}\u{8}\u{C}\n\r\t\" \\ / </script> & \u{7F}\u{85}\u{2028}\u{2029} 😀 café e\u{301}"
    case "styledText": entries[0].senses[0].content = [.init(id: id(24), content: .text(.init(text: "literal <&>\n拾语", style: .heading, bold: true, italic: true)))]
    case "emptyCaption": entries[0].senses[0].content = [.init(id: id(25), content: .image(image(), caption: ""))]
    case "variants":
        entries[0].senses[0].standaloneMeanings = [.init(text: "literal", language: nil), .init(text: "literal", language: .french)]
        entries[0].senses[0].examples[0].translationVariants = [.init(text: "other", language: nil)]
    case "integerMetadata": entries[0].senses[0].content = [.init(id: id(26), content: .image(.init(digest: String(repeating: "a", count: 64), width: Int.max, height: 1, byteCount: Int.min), caption: "literal"))]
    default: break
    }
    return try VocabularyExportFormatter.render(VocabularyExportPolicy.document(entries, configuration: c), configuration: c)
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let results = (corpus["cases"] as! [[String: Any]]).map { row -> [String: Any] in
    do {
        let data = try jsonProbe(row)
        let result: [String: Any] = (row["mode"] as! String).hasPrefix("budget") ? ["byteCount": data.count, "sha256": SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()] : ["text": String(decoding: data, as: UTF8.self), "byteCount": data.count]
        return ["id": row["id"]!, "expected": result]
    } catch let error as VocabularyExportError { return ["id": row["id"]!, "expected": ["error": String(describing: error)]] }
    catch { fatalError("Unexpected JSON export error") }
}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-export-json-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not v for v in ids) or len(set(ids)) != len(ids): parser.error('JSON probes require pinned version and unique IDs')
    for row in rows:
        expected_source = 'Cotabby/Support/Vocabulary/VocabularyExportPolicy.swift' if row['mode'].startswith('budget') else 'Cotabby/Support/Vocabulary/VocabularyExportFormatter.swift'
        if row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != expected_source: parser.error('JSON probes require exact source provenance')
    if not args.capture_output and any('expected' not in row for row in rows): parser.error('Every JSON probe needs captured expected values')
    with tempfile.TemporaryDirectory(prefix='lingrove-export-json-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(export_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'export-json-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output: json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac JSON observations'); return
    if observations != [{'id': r['id'], 'expected': r['expected']} for r in rows]:
        bad = next((r['id'] for r, o in zip(rows, observations) if o != {'id': r['id'], 'expected': r['expected']}), 'count')
        raise SystemExit(f'Mac JSON mismatch: {bad}')
    print(f'Verified {len(rows)} byte-exact JSON/budget cases against actual pinned Mac methods')


if __name__ == '__main__': main()
