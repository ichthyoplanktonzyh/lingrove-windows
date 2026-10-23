#!/usr/bin/env python3
"""Run actual pinned Mac Payload encoded/decode; fixture construction is shared with projection probes."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile

from macos_vocabulary_reference import BASELINE, database_projection_sources

# Reuse fixed value construction only. The projection driver's observation/policy calls are excluded.
_spec = importlib.util.spec_from_file_location('projection_fixture', Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
_builder = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_builder)
if _builder.DRIVER.count('func describe(') != 1: raise RuntimeError('Projection fixture boundary changed')
DRIVER = _builder.DRIVER[:_builder.DRIVER.index('func describe(')] + r'''
func identity(_ p: VocabularyDatabaseRecord.Payload) -> String {
    switch p {
    case let .entry(v): v.id.uuidString.lowercased()
    case let .sense(v): v.id.uuidString.lowercased()
    case let .usage(v): v.id.uuidString.lowercased()
    case let .explanation(v): v.id.uuidString.lowercased()
    case let .block(v): v.id.uuidString.lowercased()
    case let .review(v): v.plan.id.uuidString.lowercased()
    case let .binding(v): v.id.uuidString.lowercased()
    case let .template(v): v.id.uuidString.lowercased()
    }
}
func time(_ p: VocabularyDatabaseRecord.Payload) -> Any {
    switch p {
    case let .entry(v): v.createdAt.timeIntervalSince1970
    case let .sense(v): v.createdAt.timeIntervalSince1970
    case let .usage(v): v.createdAt.timeIntervalSince1970
    case let .explanation(v): v.createdAt.timeIntervalSince1970
    case let .binding(v): v.syncedAt?.timeIntervalSince1970 as Any? ?? NSNull()
    default: NSNull()
    }
}
func observe(_ row: [String: Any]) throws -> [String: Any] {
    let table = VocabularyDatabaseRecord.Table(rawValue: row["table"] as! String)!, mode = row["mode"] as! String
    let d = document(), rows = try VocabularyDatabaseProjection.rows(d)
    var payload = rows.values.first { $0.table == table }!.payload
    if mode == "image" { payload = .block(.init(id: id(21), content: .image(.init(digest: String(repeating: "a", count: 64), width: 2, height: 3, byteCount: 4), caption: "Caption"))) }
    if mode == "heading" { payload = .block(.init(id: id(21), content: .text(.init(text: "cafe\u{301}", style: .heading, bold: true, italic: true)))) }
    if mode == "audio" { payload = .review(.init(plan: .init(id: id(30), name: "", answer: "", includeSenseContent: false, audioKinds: [.expression],
        audioReceipts: [.expression: .init(fingerprint: String(repeating: "a", count: 64), textHash: String(repeating: "b", count: 64), endpoint: "http://localhost", profile: "profile", filename: "lingrove-" + String(repeating: "a", count: 64) + ".wav")]))) }
    if mode == "meaningNil" { var s = d.entries[0].senses[0]; s.standaloneMeanings = [.init(text: "variant", language: nil)]; payload = .sense(s) }
    if mode == "provenance" { var u = d.entries[0].examples[0]; u.appBundleIdentifier = "fixture.app"; payload = .usage(u) }
    if mode == "fractionDate" { let e = d.entries[0]; payload = .entry(.init(id: e.id, expression: e.expression, dictionaryForm: e.dictionaryForm,
        language: e.language, kind: e.kind, senses: [], createdAt: Date(timeIntervalSince1970: 0.125), updatedAt: now)) }
    var bytes = try payload.encoded()
    if row["operation"] as! String == "decode" {
        var json = try JSONSerialization.jsonObject(with: bytes) as! [String: Any]
        switch mode {
        case "missingId": json.removeValue(forKey: "id")
        case "missingNote": json.removeValue(forKey: "note")
        case "unknownField": json["future"] = ["literal": true]
        case "enumCase": json["kind"] = "WORD"
        case "enumInteger": json["kind"] = 0
        case "mappingCase": json["mapping"] = ["lingroveid", "ID"]
        case "mappingObject": json["mapping"] = ["LingroveID": "ID"]
        case "mappingOdd": json["mapping"] = ["LingroveID"]
        case "mappingDuplicate": json["mapping"] = ["LingroveID", "first", "LingroveID", "last"]
        case "unionBoth": json["content"] = ["text": ["_0": ["text":"x","style":"paragraph","bold":false,"italic":false]], "image": [:]]
        case "unionUnknown": json["content"] = ["other": [:]]
        case "styleCase": json["content"] = ["text": ["_0": ["text":"x","style":"HEADING","bold":false,"italic":false]]]
        case "missingStyle": json["content"] = ["text": ["_0": ["text":"x","bold":false,"italic":false]]]
        case "duplicateNote", "bom", "utf16LE", "utf16BE", "utf16NoBomLE", "utf16NoBomBE", "utf32LE", "utf32BE", "utf32NoBomLE", "utf32NoBomBE", "escapedLone", "canonicalFields": break
        default: fatalError("Unknown decoding mutation")
        }
        if mode == "canonicalFields" { json["fields"] = [String: String]() }
        bytes = try JSONSerialization.data(withJSONObject: json, options: [.sortedKeys])
        if mode == "escapedLone" {
            let text = String(data: bytes, encoding: .utf8)!
            bytes = Data(text.replacingOccurrences(of: "\"note\":\"note\"", with: "\"note\":\"\\uD800\"").utf8)
        }
        if mode == "canonicalFields" {
            let text = String(data: bytes, encoding: .utf8)!
            bytes = Data(text.replacingOccurrences(of: "\"fields\":{}", with: "\"fields\":{\"caf\\u00e9\":\"first\",\"cafe\\u0301\":\"last\"}").utf8)
        }
        if mode == "duplicateNote" {
            let text = String(data: bytes, encoding: .utf8)!
            let marker = "\"note\":\"note\""; guard text.components(separatedBy: marker).count == 2 else { fatalError("Missing duplicate seed") }
            bytes = Data(text.replacingOccurrences(of: marker, with: "\"note\":\"first\",\"note\":\"last\"").utf8)
        }
        if mode == "bom" { bytes = Data([0xef, 0xbb, 0xbf]) + bytes }
        if mode == "utf16LE" { bytes = Data([0xff, 0xfe]) + String(data: bytes, encoding: .utf8)!.data(using: .utf16LittleEndian)! }
        if mode == "utf32LE" { bytes = Data([0xff, 0xfe, 0, 0]) + String(data: bytes, encoding: .utf8)!.data(using: .utf32LittleEndian)! }
    }
        if mode == "utf16BE" { bytes = Data([0xfe, 0xff]) + String(data: bytes, encoding: .utf8)!.data(using: .utf16BigEndian)! }
        if mode == "utf16NoBomLE" { bytes = String(data: bytes, encoding: .utf8)!.data(using: .utf16LittleEndian)! }
        if mode == "utf16NoBomBE" { bytes = String(data: bytes, encoding: .utf8)!.data(using: .utf16BigEndian)! }
        if mode == "utf32BE" { bytes = Data([0, 0, 0xfe, 0xff]) + String(data: bytes, encoding: .utf8)!.data(using: .utf32BigEndian)! }
        if mode == "utf32NoBomLE" { bytes = String(data: bytes, encoding: .utf8)!.data(using: .utf32LittleEndian)! }
        if mode == "utf32NoBomBE" { bytes = String(data: bytes, encoding: .utf8)!.data(using: .utf32BigEndian)! }
    let input = String(data: bytes, encoding: .utf8) ?? ""
    do {
        let decoded = try VocabularyDatabaseRecord.Payload.decode(bytes, table: table)
        let json = try JSONSerialization.jsonObject(with: decoded.encoded())
        return ["id": row["id"]!, "input": input, "inputBase64": bytes.base64EncodedString(), "expected": ["valid": true, "json": json, "identity": identity(decoded), "unixTime": time(decoded)]]
    } catch { return ["id": row["id"]!, "input": input, "inputBase64": bytes.base64EncodedString(), "expected": ["valid": false]] }
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let results = try (corpus["cases"] as! [[String: Any]]).map(observe)
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-codec-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Author actual input bytes/observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if (corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not value for value in ids) or len(set(ids)) != len(ids)):
        parser.error('Codec cases require pinned version and unique IDs')
    if any(row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != 'Cotabby/Models/Vocabulary/VocabularyDatabaseModels.swift' for row in rows):
        parser.error('Codec probes require exact source provenance')
    with tempfile.TemporaryDirectory(prefix='lingrove-codec-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(database_projection_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'codec-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac payload codec inputs/observations'); return
    if len(observations) != len(rows): raise SystemExit('Codec observation count changed')
    for row, observed in zip(rows, observations):
        if observed != {'id': row['id'], 'input': row['input'], 'inputBase64': row['inputBase64'], 'expected': row['expected']}: raise SystemExit(f"Mac codec mismatch: {row['id']}")
    print(f'Verified {len(observations)} payload codec cases against actual pinned Mac Codable methods')


if __name__ == '__main__':
    main()
