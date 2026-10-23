#!/usr/bin/env python3
"""Run the complete original in-memory speech export policy with synthetic encoded/PCM bytes."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, sense_policy_sources, source
POLICY = 'Cotabby/Support/Speech/SpeechExportPolicy.swift'
DRIVER = r'''
import Foundation
func text(_ r: [String: AnkiValue], _ k: String, _ fallback: String = "") -> String { r[k]?.string ?? fallback }
func number(_ r: [String: AnkiValue], _ k: String, _ fallback: Int = 0) -> Int { if case let .number(n) = r[k] { return Int(n) }; return fallback }
func data(_ r: [String: AnkiValue]) -> Data {
    if let base64 = r["bytes"]?.string { return Data(base64Encoded: base64)! }
    if r["zeroBytes"] != nil { return Data(base64Encoded: text(r, "prefix"))! + Data(repeating: 0, count: number(r, "zeroBytes")) }
    var data = Data()
    if let bits = r["bits"]?.array { for item in bits { if case let .number(n) = item { var u = UInt32(n).littleEndian; withUnsafeBytes(of: &u) { data.append(contentsOf: $0) } } }; return data }
    for sample in r["samples"]?.array ?? [] { if case let .number(n) = sample { var u = Float(n).bitPattern.littleEndian; withUnsafeBytes(of: &u) { data.append(contentsOf: $0) } } }
    return data
}
func snapshot(_ audio: SpeechExportAudio) -> AnkiValue {
    var value: [String: AnkiValue] = ["bytes": .number(Double(audio.data.count)), "extension": .string(audio.fileExtension), "digest": .string(SpeechExportPolicy.digest(audio.data)), "filename": .string(SpeechExportPolicy.filename(audio))]
    if audio.data.count <= 256 { value["hex"] = .string(audio.data.map { String(format: "%02x", $0) }.joined()) }
    return .object(value)
}
func observe(_ r: [String: AnkiValue]) -> AnkiValue {
    do {
        switch text(r, "kind", "encoded") {
        case "filename": return .object(["valid": .bool(SpeechExportPolicy.validFilename(text(r, "name")))])
        case "identity": return .object(["valid": .bool(true), "audio": snapshot(.init(data: data(r), fileExtension: text(r, "extension", "wav")))])
        case "pcm": return .object(["valid": .bool(true), "audio": snapshot(try SpeechExportPolicy.wav(data(r), sampleRate: number(r, "rate", 24000)))])
        default: return .object(["valid": .bool(true), "audio": snapshot(try SpeechExportPolicy.encoded(data(r)))])
        }
    } catch { return .object(["valid": .bool(false), "error": .string(error.localizedDescription)]) }
}
let corpus = try JSONDecoder().decode(AnkiValue.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
let results = corpus.object!["cases"]!.array!.map { value -> AnkiValue in let r = value.object!; return .object(["id": r["id"]!, "expected": observe(r)]) }
let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]; FileHandle.standardOutput.write(try encoder.encode(AnkiValue.array(results)))
'''
def main():
    p = argparse.ArgumentParser(description=__doc__); p.add_argument('--mac-repo', type=Path, required=True)
    p.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/speech-export-v1.json'); p.add_argument('--capture-output', type=Path)
    a = p.parse_args(); corpus = json.loads(a.fixture.read_text()); rows = corpus['cases']; ids = [r['id'] for r in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or len(ids) != len(set(ids)): p.error('Pinned unique export cases required')
    if hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest() != '1970b9f0aeff8650b250d8e920eed9273b57f2ab7c86e5d00f2d0f6a0bb77789': p.error('The reviewed73-case ID matrix changed')
    if any(r.get('macSource') != POLICY or r.get('provenanceKind') != 'source-policy-extension' for r in rows): p.error('Explicit export provenance required')
    models = source(a.mac_repo, 'Cotabby/Models/Speech/SpeechExportModels.swift'); errors = source(a.mac_repo, 'Cotabby/Models/Speech/LearningSpeechModels.swift')
    values = sense_policy_sources(a.mac_repo) + [source(a.mac_repo, 'Cotabby/Models/Review/AnkiWireModels.swift'),
        'import Foundation\n' + models[models.index('nonisolated struct SpeechExportAudio:'):],
        'import Foundation\n' + between(errors, 'nonisolated enum SpeechError:', '/// The system catalog'), source(a.mac_repo, POLICY)]
    with tempfile.TemporaryDirectory(prefix='lingrove-speech-export-') as temp:
        root = Path(temp); files = []
        for i, text in enumerate(values):
            path = root / f'source{i}.swift'; path.write_text(text); files.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER); binary = root / 'speech-export'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *files, str(driver), '-o', str(binary)], check=True)
        actual = json.loads(subprocess.check_output([str(binary), str(a.fixture)], timeout=120))
    if a.capture_output:
        with a.capture_output.open('x', encoding='utf-8') as out: json.dump(actual, out, ensure_ascii=False, indent=2); out.write('\n')
        print(f'Captured {len(actual)} original in-memory speech export observations'); return
    if len(actual) != len(rows): raise SystemExit('Export count changed')
    for row, result in zip(rows, actual):
        if result != {'id': row['id'], 'expected': row['expected']}: raise SystemExit(f'Mac speech export mismatch: {row["id"]}')
    print(f'Verified {len(rows)} original in-memory speech export observations')
if __name__ == '__main__': main()
