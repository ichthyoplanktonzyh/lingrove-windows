#!/usr/bin/env python3
"""Execute original Anki field/conflict/removal/audio-field methods, without a server or speech."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, sense_policy_sources, source

POLICY = 'Cotabby/Support/Review/AnkiReviewPolicy.swift'
DRIVER = r'''
import Foundation
import CryptoKit
func id(_ n: Int) -> UUID { UUID(uuidString: String(format: "00000000-0000-0000-0000-%012x", n))! }
func text(_ r: [String: AnkiValue], _ k: String, _ fallback: String = "") -> String { r[k]?.string ?? fallback }
func flag(_ r: [String: AnkiValue], _ k: String, _ fallback: Bool) -> Bool { if case let .bool(v) = r[k] { return v }; return fallback }
func number(_ r: [String: AnkiValue], _ k: String, _ fallback: Int) -> Int { if case let .number(v) = r[k] { return Int(v) }; return fallback }
func map(_ r: [String: AnkiValue], _ k: String) -> [String: String] { (r[k]?.object ?? [:]).mapValues { $0.string! } }
func fieldMap(_ r: [String: AnkiValue], _ k: String, preset: Bool = false) -> [ReviewField: String] {
    guard let value = r[k]?.object else { return preset ? AnkiReviewPolicy.presetMapping : [:] }
    return Dictionary(uniqueKeysWithValues: value.map { (ReviewField(rawValue: $0.key)!, $0.value.string!) })
}
func blocks() -> [VocabularyContentBlock] { [.init(id: id(7), content: .text(.init(text: "A <hint>&"))), .init(id: id(8), content: .image(.init(digest: String(repeating: "a", count: 64), width: 2, height: 3, byteCount: 4), caption: "image<&>"))] }
func parts(_ value: AnkiValue?) -> [ReviewResolvedPart]? {
    value?.array?.map { v in let r = v.object!; switch text(r, "kind") {
    case "text": return .text(text(r, "text"))
    case "audio": return .audio(ReviewAudioKind(rawValue: text(r, "audio"))!)
    default: return .blocks(blocks())
    } }
}
func material(_ r: [String: AnkiValue]) -> ReviewCardContent {
    .init(expression: text(r, "expression", "bank<&>"), answer: text(r, "answer", "answer<&>"), sentence: "The bank.",
          ranges: [.init(NSRange(location: 4, length: 4))], translation: "translation", explanation: "explanation", notes: "notes",
          gapPrompt: "The ___.", gapAnswer: "bank", recognition: flag(r, "recognition", true), gap: flag(r, "gap", true),
          senseContent: flag(r, "blocks", false) ? blocks() : [], front: parts(r["front"]), back: parts(r["back"]))
}
func plan(_ r: [String: AnkiValue]) -> ReviewPlan {
    var p = ReviewPlan(id: id(1), recognition: flag(r, "recognition", true), gap: flag(r, "gap", true))
    p.audioKinds = r["audioKinds"]?.array.map { Set($0.map { ReviewAudioKind(rawValue: $0.string!)! }) }
    p.audioReceipts = r["receipts"]?.object.map { Dictionary(uniqueKeysWithValues: $0.map { key, v in
        (ReviewAudioKind(rawValue: key)!, ReviewAudioReceipt(fingerprint: "fingerprint", textHash: "hash", endpoint: "http://localhost", profile: "profile", filename: v.string!)) }) }
    return p
}
func snapshot(_ values: [String: String]) -> AnkiValue { .object(values.mapValues { v in
    v.utf8.count <= 4096 ? .string(v) : .object(["bytes": .number(Double(v.utf8.count)), "sha256": .string(SHA256.hash(data: Data(v.utf8)).map { String(format: "%02x", $0) }.joined())])
}) }
func observe(_ r: [String: AnkiValue]) throws -> AnkiValue {
    switch text(r, "kind", "fields") {
    case "preset": return .object(["name": .string(AnkiReviewPolicy.presetName), "mapping": .strings(Dictionary(uniqueKeysWithValues: AnkiReviewPolicy.presetMapping.map { ($0.key.rawValue, $0.value) }))])
    case "conflicts":
        return .array(AnkiReviewPolicy.conflicts(base: map(r, "base"), local: map(r, "local"), remote: map(r, "remote")).map { .object(["id": .string($0.id), "local": .string($0.local), "remote": .string($0.remote)]) })
    case "removes":
        let binding = AnkiNoteBinding(originSenseID: id(2), endpoint: "http://localhost", profile: "profile", noteID: flag(r, "hasNote", true) ? 10 : nil,
             model: "model", deck: "deck", identityField: "LingroveID", mapping: fieldMap(r, "mapping", preset: true), fields: map(r, "fields"))
        return .bool(AnkiReviewPolicy.removesExistingCard(plan(r), binding: binding))
    case "audio":
        return .strings(Dictionary(uniqueKeysWithValues: AnkiReviewPolicy.audioFields(plan(r)).map { ($0.key.rawValue, $0.value) }))
    default:
        var overrides = map(r, "overrides")
        if r["repeatCount"] != nil { overrides[text(r, "repeatField", "Meaning")] = String(repeating: text(r, "repeatText", "a"), count: number(r, "repeatCount", 0)) }
        return snapshot(try AnkiReviewPolicy.fields(content: material(r), identity: id(2), mapping: fieldMap(r, "mapping", preset: true), overrides: overrides, audio: fieldMap(r, "audio")))
    }
}
let root = try JSONDecoder().decode(AnkiValue.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
let observations: [AnkiValue] = root.object!["cases"]!.array!.map { item in
    let r = item.object!; let expected: AnkiValue
    do { expected = .object(["valid": .bool(true), "value": try observe(r)]) }
    catch { expected = .object(["valid": .bool(false), "error": .string(error.localizedDescription)]) }
    return .object(["id": r["id"]!, "expected": expected])
}
let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]
FileHandle.standardOutput.write(try encoder.encode(AnkiValue.array(observations)))
'''

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--mac-repo', type=Path, required=True)
    p.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/anki-fields-v1.json')
    p.add_argument('--capture-output', type=Path)
    a = p.parse_args(); corpus = json.loads(a.fixture.read_text()); rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or len({r['id'] for r in rows}) != len(rows): p.error('Pinned unique source cases required')
    if hashlib.sha256('\n'.join(sorted(r['id'] for r in rows)).encode()).hexdigest() != 'd07dcfb0c6b28be1e9e1f4e13e4a5dbc25323b63e1b76079ceb5d6d844dd859f': p.error('The reviewed76-case ID matrix changed')
    if any(r.get('macSource') != POLICY or r.get('provenanceKind') != 'source-policy-extension' for r in rows): p.error('Explicit original field policy provenance required')
    original = source(a.mac_repo, POLICY)
    methods = between(original, '    static let presetName', '    static func removesExistingCard(') + between(original, '    static func removesExistingCard(', '    static func endpoint(')
    methods += between(original, '    static func fields(', '    static func currentAudio(') + between(original, '    static func audioFields(', '    static func merged(')
    values = sense_policy_sources(a.mac_repo)[:-1] + [source(a.mac_repo, path) for path in ('Cotabby/Support/Review/ReviewCardPolicy.swift', 'Cotabby/Support/Review/ReviewLayoutPolicy.swift', 'Cotabby/Support/Review/AnkiNoteTemplate.swift', 'Cotabby/Models/Review/AnkiWireModels.swift')]
    values += ['import Foundation\nextension AnkiReviewPolicy {\n' + methods + '}\n']
    with tempfile.TemporaryDirectory(prefix='lingrove-anki-fields-') as temp:
        root = Path(temp); files = []
        for i, text in enumerate(values):
            path = root / f'source{i}.swift'; path.write_text(text); files.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER); binary = root / 'anki-fields'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *files, str(driver), '-o', str(binary)], check=True)
        actual = json.loads(subprocess.check_output([str(binary), str(a.fixture)], timeout=120))
    if a.capture_output:
        with a.capture_output.open('x', encoding='utf-8') as out: json.dump(actual, out, ensure_ascii=False, indent=2); out.write('\n')
        print(f'Captured {len(actual)} original Anki field/conflict/removal/audio-field observations'); return
    if len(actual) != len(rows): raise SystemExit('Field observation count changed')
    for row, result in zip(rows, actual):
        if result != {'id': row['id'], 'expected': row['expected']}: raise SystemExit(f'Mac field mismatch: {row["id"]}')
    print(f'Verified {len(rows)} original Anki field/conflict/removal/audio-field observations')

if __name__ == '__main__': main()
