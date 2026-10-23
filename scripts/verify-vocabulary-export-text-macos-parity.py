#!/usr/bin/env python3
"""Execute the pinned Mac CSV cell and byte-bounded preview methods without archive or UI."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, export_sources

DRIVER = r'''
import Foundation
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let results = (corpus["cases"] as! [[String: Any]]).map { row -> [String: Any] in
    let result: String
    switch row["operation"] as! String {
    case "cell":
        // JSONSerialization can consume an initial BOM inside a decoded string. Rebuild the
        // literal input from integer scalars so this probe exercises the formatter, not that codec.
        let text = String(String.UnicodeScalarView((row["scalars"] as! [Int]).map { UnicodeScalar($0)! }))
        result = VocabularyExportFormatter.csvCell(text, spreadsheetCompatibility: row["compatibility"] as! Bool)
    case "preview": result = VocabularyExportFormatter.preview(Data(base64Encoded: row["data"] as! String)!, format: .init(rawValue: row["format"] as! String)!)
    default: fatalError("Unknown export text probe")
    }
    return ["id": row["id"]!, "expected": result]
}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-export-text-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not v for v in ids) or len(set(ids)) != len(ids):
        parser.error('Export text cases require pinned version and unique IDs')
    if any(row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != 'Cotabby/Support/Vocabulary/VocabularyExportFormatter.swift' for row in rows):
        parser.error('Export text probes require exact source provenance')
    if any(row['operation'] == 'cell' and row.get('scalars') != [ord(c) for c in row['text']] for row in rows):
        parser.error('Cell scalar inputs must match the literal C# text')
    if not args.capture_output and any('expected' not in row for row in rows): parser.error('Every export text probe needs captured expected values')
    with tempfile.TemporaryDirectory(prefix='lingrove-export-text-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(export_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'export-text-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac export text observations'); return
    expected = [{'id': row['id'], 'expected': row['expected']} for row in rows]
    if observations != expected:
        bad = next((r['id'] for r, o in zip(rows, observations) if o != {'id': r['id'], 'expected': r['expected']}), 'count')
        raise SystemExit(f'Mac export text mismatch: {bad}')
    print(f'Verified {len(rows)} CSV cell / UTF-8 preview cases against actual pinned Mac methods')


if __name__ == '__main__': main()
