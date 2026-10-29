#!/usr/bin/env python3
"""Compare frozen Swift SQLite DDL against a Windows-generated schema manifest.

Only schema declarations are compiled; the separate row interchange gate is not implied.
"""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

REVISION = '35b7fbe5c788c5524936ade1dfb7c89a3a285b04'
ROOT = Path(__file__).resolve().parents[1]


def source(repo, path):
    return subprocess.check_output(['git', '-C', str(repo), 'show', f'{REVISION}:{path}'], text=True)


def section(text, start, end):
    if text.count(start) != 1 or text.count(end) != 1:
        raise RuntimeError(f'Frozen declaration boundary changed: {start}')
    return text[text.index(start):text.index(end)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', required=True, type=Path)
    parser.add_argument('--manifest', type=Path, default=ROOT / 'fixtures/parity/vocabulary-sqlite-frozen-schema-v1.json')
    args = parser.parse_args()
    model = source(args.mac_repo, 'Lingrove/Models/Vocabulary/VocabularyDatabaseModels.swift')
    database = source(args.mac_repo, 'Lingrove/Services/Vocabulary/VocabularySQLiteDatabase.swift')
    connection = source(args.mac_repo, 'Lingrove/Services/Vocabulary/VocabularySQLiteConnection.swift')
    if connection.count('import Logging\n') != 1 or connection.count('LingroveLogger.learning.error(') != 2:
        raise RuntimeError('Frozen SQLite logging boundary changed')
    connection = '\n'.join(line for line in connection.split('\n')
                           if line != 'import Logging' and 'LingroveLogger.learning.error(' not in line)
    table = section(model, '    enum Table: String, CaseIterable, Sendable {', '    struct Key: Hashable')
    schema = database[database.index('    private static func createSchema('):database.rindex('\n}')]
    schema = schema.replace('private static func createSchema(', 'static func createSchema(', 1)
    wrapper = ('import Foundation\nenum VocabularyError: Error { case invalidArchive, invalidContent, storageBusy, oversizedArchive }\n'
               'struct VocabularyDatabaseRecord {\n' + table + '}\n'
               'enum SchemaProbe {\n    typealias Row = VocabularyDatabaseRecord\n'
               '    static let applicationID: Int64 = 0x4C47564E\n    static let schemaVersion: Int64 = 5\n'
               + schema + '\n}\n')
    driver = r'''
import Foundation
let db = try VocabularySQLiteConnection(url: nil)
try SchemaProbe.createSchema(db)
func schema(_ db: VocabularySQLiteConnection) throws -> [String: String] {
    var result: [String: String] = [:]
    for row in try db.rows("SELECT type,name,sql FROM sqlite_master WHERE type IN ('table','index','trigger','view') AND substr(name,1,7) <> 'sqlite_'") {
        guard let type = row[0].string, let name = row[1].string, let sql = row[2].string else { fatalError("unexpected schema") }
        result[type + ":" + name] = sql
    }
    return result
}
let five = try schema(db)
for table in VocabularySQLiteClassificationSchema.tables.reversed() { try db.execute("DROP TABLE " + table.rawValue) }
try db.execute("PRAGMA user_version=4")
let four = try schema(db)
try VocabularySQLiteClassificationSchema.migrate(db)
let migrated = try schema(db)
guard migrated == five, try db.rows("PRAGMA user_version") == [[.integer(5)]] else { fatalError("migration mismatch") }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: ["schema4": four, "schema5": five], options: [.sortedKeys]))
'''
    with tempfile.TemporaryDirectory(prefix='lingrove-frozen-schema-') as tmp:
        root = Path(tmp)
        texts = [connection, wrapper] + [source(args.mac_repo, 'Lingrove/Services/Vocabulary/' + name + '.swift')
                                        for name in ('VocabularySQLiteMeaningSchema', 'VocabularySQLiteClassificationSchema')]
        files = []
        for index, text in enumerate(texts):
            path = root / f'source{index}.swift'; path.write_text(text, encoding='utf-8'); files.append(str(path))
        main_file = root / 'main.swift'; main_file.write_text(driver, encoding='utf-8')
        executable = root / 'probe'
        subprocess.run(['swiftc', '-swift-version', '6', '-lsqlite3', *files, str(main_file), '-o', str(executable)], check=True)
        observed = json.loads(subprocess.check_output([str(executable)]))
    expected = json.loads(args.manifest.read_text(encoding='utf-8'))
    if expected.get('macReference') != REVISION or observed != expected.get('schemas'):
        raise SystemExit('Frozen native SQLite schema differs from the Windows manifest')
    print('Verified frozen Swift schema 5 and 4-to-5 schema migration against Windows manifest')


if __name__ == '__main__':
    main()
