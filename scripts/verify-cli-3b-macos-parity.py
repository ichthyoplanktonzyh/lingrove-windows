#!/usr/bin/env python3
"""Compare 3b Swift CLI Codable fingerprints, stable IDs and NSString range logic.

Uses the committed CLIProtocol/CLICaptureModels declarations and extracts the exact
fingerprint, OperationContext and auto-range bodies. The only stand-in is a range value
that wraps NSRange; no archive, image, lifecycle or Store behavior is exercised.
"""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

REVISION = '3b53402f2cbc196b0398d133177fd80006d165b2'
ROOT = Path(__file__).resolve().parents[1]


def source(repo, path):
    return subprocess.check_output(['git', '-C', str(repo), 'show', REVISION + ':' + path], text=True, encoding='utf-8')


def section(text, start, end):
    if text.count(start) != 1 or text.count(end) != 1:
        raise RuntimeError('3b CLI extraction boundary changed')
    return text[text.index(start):text.index(end)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=ROOT / 'fixtures/parity/cli-3b-source-v1.json')
    args = parser.parse_args()
    data = json.loads(args.fixture.read_text(encoding='utf-8-sig'))
    if data['macReference'] != REVISION or not data['batches'] or not data['ranges']:
        raise SystemExit('Unexpected CLI probe corpus')
    protocol = source(args.mac_repo, 'Lingrove/Models/CLI/CLIProtocol.swift')
    capture = source(args.mac_repo, 'Lingrove/Models/CLI/CLICaptureModels.swift')
    plan = source(args.mac_repo, 'Lingrove/Support/Vocabulary/VocabularyCLIPlan.swift')
    fingerprint = section(plan, '    static func fingerprint(_ batch: CLIBatch) throws -> String {', '    init(batch: CLIBatch, snapshot: VocabularySnapshot, fingerprint: String,')
    context = section(plan, '    private struct OperationContext {', '    private mutating func apply(')
    start = '            let range = (text as NSString).range(of: expression)'
    final = '            ranges = range.location == NSNotFound ? [] : [.init(range)]'
    if plan.count(start) != 1 or plan.count(final) != 1:
        raise RuntimeError('3b range extraction boundary changed')
    ranges = plan[plan.index(start):plan.index(final) + len(final)]
    declarations = '''import Foundation
import CryptoKit
struct VocabularyTextRange { let range: NSRange; init(_ range: NSRange) { self.range = range } }
enum SourceProbe {
''' + fingerprint + context + '''
    static func stableID(_ key: String, _ fingerprint: String) -> UUID {
        OperationContext(index: 0, batchKey: key, fingerprint: fingerprint, now: Date()).id("entry")
    }
    static func locate(_ text: String, _ expression: String) throws -> String {
        let ranges: [VocabularyTextRange]
''' + ranges + '''
        guard let range = ranges.first?.range else { return "missing" }
        return "\\(range.location):\\(range.length)"
    }
}
'''
    driver = r'''import Foundation
struct Fixture: Decodable {
    struct Batch: Decodable { let name: String; let batch: CLIBatch; let expectedFingerprint: String; let expectedId: String }
    struct Range: Decodable { let name: String; let text: String; let expression: String; let expected: String }
    let macReference: String; let batches: [Batch]; let ranges: [Range]
}
@main struct Main {
    static func main() throws {
        let data = try Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))
        let fixture = try JSONDecoder().decode(Fixture.self, from: data)
        for row in fixture.batches {
            let fingerprint = try SourceProbe.fingerprint(row.batch)
            guard fingerprint == row.expectedFingerprint else { fatalError("fingerprint mismatch \(row.name): \(fingerprint)") }
            let id = SourceProbe.stableID(row.batch.idempotencyKey, fingerprint).uuidString
            guard id == row.expectedId else { fatalError("ID mismatch \(row.name): \(id)") }
        }
        for row in fixture.ranges {
            let actual: String
            do { actual = try SourceProbe.locate(row.text, row.expression) } catch { actual = "ambiguous" }
            guard actual == row.expected else { fatalError("range mismatch \(row.name): \(actual)") }
        }
        print("Verified 3b Swift CLI Codable fingerprint, operation ID and NSString range probes")
    }
}
'''
    with tempfile.TemporaryDirectory(prefix='lingrove-cli-3b-') as tmp:
        path = Path(tmp)
        (path / 'protocol.swift').write_text(protocol, encoding='utf-8')
        (path / 'capture.swift').write_text(capture, encoding='utf-8')
        (path / 'declarations.swift').write_text(declarations, encoding='utf-8')
        (path / 'driver.swift').write_text(driver, encoding='utf-8')
        binary = path / 'probe'
        subprocess.run(['swiftc', '-swift-version', '6', str(path / 'protocol.swift'), str(path / 'capture.swift'), str(path / 'declarations.swift'), str(path / 'driver.swift'), '-o', str(binary)], check=True)
        subprocess.run([str(binary), str(args.fixture)], check=True)


if __name__ == '__main__':
    main()
