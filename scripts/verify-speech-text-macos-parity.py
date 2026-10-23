#!/usr/bin/env python3
"""Run the original speech-text enum and Anki audio freshness method without voices or models."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, sense_policy_sources, source

TEXT = 'Cotabby/Support/Speech/SpeechTextPolicy.swift'
REVIEW = 'Cotabby/Support/Review/AnkiReviewPolicy.swift'
TESTS = 'CotabbyTests/Support/Speech/SpeechTextPolicyTests.swift'
DRIVER = r'''
import Foundation
func text(_ r: [String: AnkiValue], _ k: String, _ fallback: String = "") -> String { r[k]?.string ?? fallback }
func input(_ r: [String: AnkiValue]) -> String {
    if case let .number(n) = r["repeatCount"] { return String(repeating: text(r, "repeatText", "a"), count: Int(n)) }
    return text(r, "text")
}
func unit(_ r: [String: AnkiValue]) -> LearningUnit {
    .init(id: 0, surface: input(r), query: text(r, "query", "lemma"), kind: LearningUnit.Kind(rawValue: text(r, "unitKind", "word"))!, ranges: [],
          words: (r["words"]?.array ?? []).map { .init(text: $0.string!, range: .init(location: 0, length: 0)) })
}
func observe(_ r: [String: AnkiValue]) -> AnkiValue {
    if text(r, "kind", "sanitize") == "current" {
        var p = ReviewPlan()
        p.audioKinds = r["audioKinds"]?.array.map { Set($0.map { ReviewAudioKind(rawValue: $0.string!)! }) }
        p.audioReceipts = r["receipts"]?.object.map { Dictionary(uniqueKeysWithValues: $0.map { key, hash in
            (ReviewAudioKind(rawValue: key)!, ReviewAudioReceipt(fingerprint: "ignored fingerprint", textHash: hash.string!, endpoint: "http://localhost", profile: "profile", filename: "audio.wav")) }) }
        let content = ReviewCardContent(expression: input(r), answer: "answer", sentence: text(r, "sentence"), ranges: [], translation: "", explanation: "", notes: "", gapPrompt: "", gapAnswer: "", recognition: true, gap: false)
        return .bool(AnkiReviewPolicy.currentAudio(p, content: content))
    }
    let answer = text(r, "kind", "sanitize") == "unit" ? SpeechTextPolicy.spokenText(for: unit(r)) : SpeechTextPolicy.sanitizedText(input(r))
    return answer.map(AnkiValue.string) ?? .null
}
let corpus = try JSONDecoder().decode(AnkiValue.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
let values: [AnkiValue] = corpus.object!["cases"]!.array!.map { value in let r = value.object!; return .object(["id": r["id"]!, "expected": observe(r)]) }
let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]; FileHandle.standardOutput.write(try encoder.encode(AnkiValue.array(values)))
'''

def main():
    p = argparse.ArgumentParser(description=__doc__); p.add_argument('--mac-repo', type=Path, required=True)
    p.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/speech-text-v1.json'); p.add_argument('--capture-output', type=Path)
    a = p.parse_args(); corpus = json.loads(a.fixture.read_text()); rows = corpus['cases']; ids = [r['id'] for r in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or len(ids) != len(set(ids)): p.error('Pinned unique speech cases required')
    if hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest() != '539253fa260ba1d8896a73a8020f548eb95a402145dd0fca4f61cec9c3002103': p.error('The reviewed47-case ID matrix changed')
    tests = source(a.mac_repo, TESTS)
    for r in rows:
        if r.get('macSource') not in (TEXT, REVIEW) or r.get('provenanceKind') not in ('source-policy-extension', 'regression-adaptation'): p.error('Speech provenance required')
        if r['provenanceKind'] == 'regression-adaptation' and (r.get('macTest') != TESTS or 'func ' + r.get('macTestMethod', '?') + '(' not in tests): p.error('Original speech test required')
    units = source(a.mac_repo, 'Cotabby/Models/LanguageLearning/LearningUnitModels.swift'); words = source(a.mac_repo, 'Cotabby/Models/LanguageLearning/LearningDictionaryWord.swift')
    speech = source(a.mac_repo, TEXT); review = source(a.mac_repo, REVIEW); export = source(a.mac_repo, 'Cotabby/Support/Speech/SpeechExportPolicy.swift')
    values = sense_policy_sources(a.mac_repo) + [source(a.mac_repo, 'Cotabby/Models/Review/AnkiWireModels.swift'),
        'import Foundation\n' + between(units, 'nonisolated struct LearningUnit:', '/// Recognition and lookup share a resource,') + between(words, 'nonisolated struct LearningDictionaryWord:', '/// View-local geometry exists'),
        'import Foundation\n' + between(speech, 'nonisolated enum SpeechTextPolicy {', '/// Qwen\'s fixed language inventory'),
        'import CryptoKit\nimport Foundation\nnonisolated enum SpeechExportPolicy {\n' + between(export, '    static func digest(', '    static func filename(') + '}\n',
        'import Foundation\nextension AnkiReviewPolicy {\n' + between(review, '    static func currentAudio(', '    static func audioFields(') + '}\n']
    with tempfile.TemporaryDirectory(prefix='lingrove-speech-text-') as temp:
        root = Path(temp); files = []
        for i, text in enumerate(values):
            path = root / f'source{i}.swift'; path.write_text(text); files.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER); binary = root / 'speech-text'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *files, str(driver), '-o', str(binary)], check=True)
        actual = json.loads(subprocess.check_output([str(binary), str(a.fixture)]))
    if a.capture_output:
        with a.capture_output.open('x', encoding='utf-8') as out: json.dump(actual, out, ensure_ascii=False, indent=2); out.write('\n')
        print(f'Captured {len(actual)} original speech text/audio-freshness observations'); return
    if len(actual) != len(rows): raise SystemExit('Speech count changed')
    for row, result in zip(rows, actual):
        if result != {'id': row['id'], 'expected': row['expected']}: raise SystemExit(f'Mac speech mismatch: {row["id"]}')
    print(f'Verified {len(rows)} original speech text/audio-freshness observations')

if __name__ == '__main__': main()
