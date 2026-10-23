#!/usr/bin/env python3
"""Execute actual pinned Mac export scope/configuration/projection; no archive or file export."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, sense_policy_sources, source

# Reuse fixed graph INPUT construction, never its expected results or projection behavior.
_spec = importlib.util.spec_from_file_location('projection_inputs', Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
_inputs = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_inputs)
if _inputs.DRIVER.count('func describe(') != 1: raise RuntimeError('Fixed input boundary must be unique')
INPUTS = _inputs.DRIVER.split('func describe(')[0]
DRIVER = INPUTS + r'''
func image(_ width: Int = 1) -> VocabularyImageReference {
    .init(digest: String(repeating: "a", count: 64), width: width, height: 1, byteCount: 70)
}
func exportEntries() -> [VocabularyEntry] {
    var d = document()
    d.entries[0].senses[0].mergedFrom = [id(90)]
    d.entries[0].senses[0].examples[0].appBundleIdentifier = "private.host"
    d.entries[0].senses[0].examples[0].meaningOrigin = .ai
    d.entries[0].senses[0].content = [block(20), .init(id: id(21), content: .image(image(), caption: "literal caption")),
        .init(id: id(22), content: .image(image(), caption: "second occurrence")),
        .init(id: id(23), content: .image(image(2), caption: "different metadata"))]
    d.entries[0].senses.append(.init(id: id(4), meaning: "second", createdAt: now, updatedAt: now))
    d.entries.append(.init(id: id(5), expression: "bank", dictionaryForm: "bank", language: .english, kind: .word,
        senses: [.init(id: id(6), meaning: "third", createdAt: now, updatedAt: now)], createdAt: now, updatedAt: now))
    return d.entries
}
func configuration(_ row: [String: Any]) -> VocabularyExportConfiguration {
    var c = VocabularyExportConfiguration()
    if let v = row["format"] as? String { c.format = .init(rawValue: v)! }
    if let v = row["style"] as? String { c.textStyle = .init(rawValue: v)! }
    if let v = row["meanings"] as? Bool { c.meanings = v }; if let v = row["examples"] as? Bool { c.examples = v }
    if let v = row["notes"] as? Bool { c.notes = v }; if let v = row["explanation"] as? Bool { c.explanation = v }
    if let v = row["content"] as? Bool { c.content = v }; if let v = row["images"] as? Bool { c.images = v }
    if let v = row["spreadsheetCompatibility"] as? Bool { c.spreadsheetCompatibility = v }
    return c
}
func encoded<T: Encodable>(_ value: T) -> Any {
    let encoder = JSONEncoder(); encoder.dateEncodingStrategy = .secondsSince1970
    return try! JSONSerialization.jsonObject(with: encoder.encode(value), options: [.fragmentsAllowed])
}
func selectedFacts(_ entries: [VocabularyEntry]) -> Any {
    entries.map { ["entry": $0.id.uuidString, "senses": $0.senses.map { $0.id.uuidString }] }
}
func observe(_ row: [String: Any]) async -> Any {
    let operation = row["operation"] as! String, c = configuration(row), mode = row["mode"] as? String ?? "normal"
    if operation == "configuration" {
        return ["format": c.format.rawValue, "extension": c.format.fileExtension, "style": c.textStyle.rawValue,
            "meanings": c.meanings, "examples": c.examples, "notes": c.notes, "explanation": c.explanation,
            "content": c.content, "images": c.images, "spreadsheetCompatibility": c.spreadsheetCompatibility,
            "expressionList": c.isExpressionList, "includesImages": c.includesImages, "maximumDataBytes": VocabularyExportPolicy.maximumDataBytes]
    }
    if operation == "request" {
        let a = VocabularySelection(entryID: id(1), senseID: id(2)), b = VocabularySelection(entryID: id(5), senseID: id(6))
        var all = [a, b], filtered = [b], selected = [a]
        let request = VocabularyExportRequest(all: all, filtered: filtered, selected: selected, initialScope: .filtered)
        all.removeAll(); filtered.removeAll(); selected.removeAll()
        func facts(_ scope: VocabularyExportScope) -> Any { request.selections(scope).map { [$0.entryID.uuidString, $0.senseID.uuidString] } }
        return ["all": facts(.all), "filtered": facts(.filtered), "selected": facts(.selected), "initial": request.initialScope.rawValue]
    }
    var entries = exportEntries()
    if operation == "selection" {
        var selections = [VocabularySelection(entryID: id(1), senseID: id(2))]
        switch mode {
        case "empty", "cancelledEmpty": selections = []
        case "missingEntry": selections = [.init(entryID: id(99), senseID: id(2))]
        case "missingSense": selections = [.init(entryID: id(1), senseID: id(99))]
        case "alias": selections = [.init(entryID: id(1), senseID: id(90))]
        case "duplicateEntry": entries.append(entries[0])
        case "ordered": selections = [.init(entryID: id(5), senseID: id(6)), .init(entryID: id(1), senseID: id(4)), .init(entryID: id(1), senseID: id(2)), .init(entryID: id(1), senseID: id(4))]
        default: break
        }
        if mode.hasPrefix("cancelled") {
            let capturedEntries = entries, capturedSelections = selections
            let task = Task { () -> String in
                do { try await Task.sleep(nanoseconds: 1_000_000_000) } catch { }
                do { _ = try VocabularyExportPolicy.selected(capturedEntries, selections: capturedSelections); return "completed" }
                catch let error as VocabularyExportError { return String(describing: error) }
                catch is CancellationError { return "cancelled" }
                catch { fatalError("Unexpected export selection error") }
            }
            task.cancel(); return ["error": await task.value]
        }
        do { return selectedFacts(try VocabularyExportPolicy.selected(entries, selections: selections)) }
        catch let error as VocabularyExportError { return ["error": String(describing: error)] }
        catch { fatalError("Unexpected export selection error") }
    }
    switch mode {
    case "empty": entries = []
    case "absentContent": entries[0].senses[0].content = nil
    case "emptyContent": entries[0].senses[0].content = []
    case "variants":
        entries[0].senses[0].meaning = "é"; entries[0].senses[0].meaningLanguage = .french
        entries[0].senses[0].standaloneMeanings = [.init(text: "e\u{301}", language: .french), .init(text: "é", language: .english), .init(text: "other", language: .french), .init(text: "", language: nil), .init(text: " ", language: nil)]
        entries[0].senses[0].alternateMeanings = [.init(text: "other", language: .french), .init(text: "e\u{301}", language: nil), .init(text: "é", language: nil), .init(text: "other wording", language: .french)]
        entries[0].senses[0].examples[0].translation = "é"; entries[0].senses[0].examples[0].translationLanguage = nil
        entries[0].senses[0].examples[0].translationVariants = [.init(text: "e\u{301}", language: nil), .init(text: "é", language: .french), .init(text: " ", language: nil), .init(text: "other", language: nil), .init(text: "other", language: nil)]
    case "unicodeRanges":
        entries[0].senses[0].examples = [.init(id: id(7), text: "😀 bank bank", translation: "", ranges: [.init(NSRange(location: 3, length: 4)), .init(NSRange(location: 8, length: 4))], completeness: .fragment, meaning: "", createdAt: Date(timeIntervalSince1970: 1.125))]
    case "styledText":
        entries[0].senses[0].content = [.init(id: id(24), content: .text(.init(text: "literal <&>\n拾语", style: .heading, bold: true, italic: true)))]
    case "reviewOnly":
        let content: [VocabularyContentBlock] = [.init(id: id(61), content: .image(image(), caption: "private review"))]
        entries[0].senses[0].content = []
        entries[0].senses[0].review!.plan.layout = .init(front: [.init(id: id(51), source: .custom, content: content)])
    default: break
    }
    if operation == "projection" { return encoded(VocabularyExportPolicy.document(entries, configuration: c)) }
    if operation == "images" {
        return encoded(VocabularyExportPolicy.images(entries, configuration: c).sorted { ($0.digest, $0.width, $0.height, $0.byteCount) < ($1.digest, $1.width, $1.height, $1.byteCount) })
    }
    fatalError("Unknown export probe")
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
var results = [[String: Any]]()
for row in corpus["cases"] as! [[String: Any]] { results.append(["id": row["id"]!, "expected": await observe(row)]) }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-export-projection-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not v for v in ids) or len(set(ids)) != len(ids):
        parser.error('Export probes require the pinned version and unique IDs')
    for row in rows:
        expected_source = 'Cotabby/Models/Vocabulary/VocabularyExportModels.swift' if row['operation'] in ('configuration', 'request') else 'Cotabby/Support/Vocabulary/VocabularyExportPolicy.swift'
        if row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != expected_source:
            parser.error('Export probes require exact source provenance')
    if not args.capture_output and any('expected' not in row for row in rows):
        parser.error('Every export probe needs captured expected values')
    values = sense_policy_sources(args.mac_repo) + [source(args.mac_repo, p) for p in (
        'Cotabby/Models/Vocabulary/VocabularyExportModels.swift', 'Cotabby/Support/Vocabulary/VocabularyExportPolicy.swift',
        'Cotabby/Support/Vocabulary/VocabularyExportFormatter.swift')]
    with tempfile.TemporaryDirectory(prefix='lingrove-export-projection-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(values):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'export-projection-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac export projection observations'); return
    if observations != [{'id': row['id'], 'expected': row['expected']} for row in rows]:
        bad = next((row['id'] for row, observed in zip(rows, observations) if observed != {'id': row['id'], 'expected': row['expected']}), 'count')
        raise SystemExit(f'Mac export projection mismatch: {bad}')
    print(f'Verified {len(rows)} export projection cases against actual pinned Mac methods')


if __name__ == '__main__': main()
