#!/usr/bin/env python3
"""Exercise actual frozen Swift row Payload UUID dispatch on shared value probes.

Unused non-UUID payload types are inert Codable stand-ins, never selected by these cases.
This narrow probe does not verify any non-UUID payload, graph or SQLite transaction.
"""
import argparse
import json
from pathlib import Path
import re
import subprocess
import tempfile

REVISION = '35b7fbe5c788c5524936ade1dfb7c89a3a285b04'
ROOT = Path(__file__).resolve().parents[1]


def section(text, start, end):
    if text.count(start) != 1 or text.count(end) != 1:
        raise RuntimeError('Frozen declaration boundary changed')
    return text[text.index(start):text.index(end)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=ROOT / 'fixtures/parity/vocabulary-sqlite-frozen-uuid-v1.json')
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding='utf-8-sig'))
    if corpus['macReference'] != REVISION or not corpus['cases']:
        raise SystemExit('Unexpected frozen probe reference')
    if any(row['table'] not in ('collection_interpretations', 'mapping_evidence') for row in corpus['cases']):
        raise SystemExit('Probe supports only UUID payload tables')
    source = subprocess.check_output(['git', '-C', str(args.mac_repo), 'show',
        REVISION + ':Lingrove/Models/Vocabulary/VocabularyDatabaseModels.swift'], text=True)
    table = section(source, '    enum Table: String, CaseIterable, Sendable {', '    struct Key: Hashable')
    payload = section(source, '    enum Payload: Equatable, Sendable {', '    let table: Table')
    types = sorted(set(re.findall(r'\b\w+\((\w+)\)', payload.split('        // Keep')[0])) - {'UUID'})
    if len(types) != 26:
        raise RuntimeError(f'Unexpected frozen payload type set: {types}')
    stubs = '\n'.join('struct ' + name + ': Codable, Equatable, Sendable {}' for name in types)
    wrapper = 'import Foundation\n' + stubs + '\nstruct VocabularyDatabaseRecord {\n' + table + payload + '}\n'
    driver = r'''
import Foundation
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
for row in corpus["cases"] as! [[String: String]] {
    let table = VocabularyDatabaseRecord.Table(rawValue: row["table"]!)!
    let decoded = try VocabularyDatabaseRecord.Payload.decode(Data(row["input"]!.utf8), table: table)
    guard case let .evidence(value) = decoded, value.uuidString == row["expected"]! else { fatalError("UUID decode mismatch") }
    let encoded = try decoded.encoded()
    guard let string = try JSONSerialization.jsonObject(with: encoded, options: .fragmentsAllowed) as? String,
          string == row["expected"]! else { fatalError("UUID encode mismatch") }
}
print("Verified frozen Swift UUID row payloads")
'''
    with tempfile.TemporaryDirectory(prefix='lingrove-frozen-uuid-') as tmp:
        root = Path(tmp); declaration = root / 'declarations.swift'; declaration.write_text(wrapper, encoding='utf-8')
        main_file = root / 'main.swift'; main_file.write_text(driver, encoding='utf-8')
        binary = root / 'probe'
        subprocess.run(['swiftc', '-swift-version', '6', str(declaration), str(main_file), '-o', str(binary)], check=True)
        subprocess.run([str(binary), str(args.fixture)], check=True)


if __name__ == '__main__':
    main()
