#!/usr/bin/env python3
"""Execute the complete original stored-ZIP encoder over prepared export values; no IO effect."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, archive_export_sources

DRIVER = r'''
import Foundation
import CryptoKit
func package(_ row: [String: Any]) throws -> VocabularyExportPackage {
    var c = VocabularyExportConfiguration()
    c.format = VocabularyExportFormat(rawValue: row["format"] as! String)!
    c.textStyle = VocabularyExportTextStyle(rawValue: row["style"] as? String ?? "notes")!
    c.images = row["imagesFlag"] as? Bool ?? true
    var p = try VocabularyExportPolicy.package([], configuration: c)
    for item in row["images"] as? [[String: Any]] ?? [] {
        p.images[item["name"] as! String] = item["base64"].map { Data(base64Encoded: $0 as! String)! }
            ?? Data(repeating: UInt8(item["fill"] as? Int ?? 0), count: item["byteCount"] as! Int)
    }
    if let count = row["generatedFiles"] as? Int {
        for i in 0..<count { p.images["lg-image-" + String(format: "%064x", i) + ".png"] = Data() }
    }
    return p
}
func facts(_ bytes: Data, row: [String: Any]) -> [String: Any] {
    var result: [String: Any] = ["byteCount": bytes.count, "sha256": SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined()]
    if row["captureBytes"] as? Bool == true { result["base64"] = bytes.base64EncodedString() }
    return result
}
func observe(_ row: [String: Any]) async -> [String: Any] {
    do {
        let p = try package(row)
        if row["cancelled"] as? Bool == true {
            let (start, continuation) = AsyncStream<Void>.makeStream()
            let task = Task { () throws -> Data in
                for await _ in start { break }
                return try VocabularyExportZIP.data(p)
            }
            task.cancel(); continuation.finish()
            return facts(try await task.value, row: row)
        }
        return facts(try VocabularyExportZIP.data(p), row: row)
    } catch is CancellationError { return ["error": "cancelled"] }
    catch let error as VocabularyExportError { return ["error": String(describing: error)] }
    catch { fatalError("Unexpected ZIP error") }
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
var observations = [[String: Any]]()
for row in corpus["cases"] as! [[String: Any]] {
    observations.append(["id": row["id"]!, "expected": await observe(row)])
}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: observations, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-export-zip-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']; ids = [r.get('id') for r in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not value for value in ids) or len(set(ids)) != len(ids): parser.error('ZIP probes require pinned version and unique IDs')
    for row in rows:
        if row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != 'Cotabby/Support/Vocabulary/VocabularyExportZIP.swift': parser.error('ZIP probes require exact encoder provenance')
    if not args.capture_output and any('expected' not in row for row in rows): parser.error('Every ZIP case needs actual observations')
    with tempfile.TemporaryDirectory(prefix='lingrove-export-zip-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(archive_export_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'export-zip-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', '-lsqlite3', '-lz', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output: json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac ZIP observations'); return
    expected = [{'id': row['id'], 'expected': row['expected']} for row in rows]
    if observations != expected:
        bad = next((r['id'] for r, o in zip(rows, observations) if o != {'id': r['id'], 'expected': r['expected']}), 'count')
        raise SystemExit(f'Mac ZIP mismatch: {bad}')
    print(f'Verified {len(rows)} ZIP byte/path/count/budget cases against actual pinned Mac encoder')


if __name__ == '__main__': main()
