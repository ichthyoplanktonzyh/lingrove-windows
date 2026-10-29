#!/usr/bin/env python3
"""Run frozen Swift relational projection and populated named SQLite probes."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location('frozen_document_probe', ROOT / 'scripts/verify-vocabulary-document-frozen-macos-parity.py')
DOC = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DOC)
REVISION = DOC.REVISION
FULL_PATHS = (
    'Lingrove/Models/Vocabulary/VocabularyDatabaseModels.swift',
    'Lingrove/Models/Vocabulary/VocabularyCLIReceipt.swift',
    'Lingrove/Support/Vocabulary/VocabularyDatabaseProjection.swift',
    'Lingrove/Support/Vocabulary/VocabularySemanticDatabaseProjection.swift',
    'Lingrove/Support/Vocabulary/VocabularyClassificationRows.swift',
    'Lingrove/Support/Vocabulary/VocabularyLifecyclePolicy.swift',
    'Lingrove/Support/Vocabulary/VocabularySemanticPolicy.swift',
    'Lingrove/Support/Vocabulary/VocabularyClassificationPolicy.swift',
    'Lingrove/Support/Vocabulary/VocabularyMeaningLabelPolicy.swift',
    'Lingrove/Support/Suggestion/Acceptance/InsertionSafetyGate.swift',
    'Lingrove/Services/Vocabulary/VocabularySQLiteFileLock.swift',
    'Lingrove/Services/Vocabulary/VocabularySQLiteDatabase.swift',
    'Lingrove/Services/Vocabulary/VocabularySQLiteMeaningSchema.swift',
    'Lingrove/Services/Vocabulary/VocabularySQLiteClassificationSchema.swift',
)
CONNECTION = 'Lingrove/Services/Vocabulary/VocabularySQLiteConnection.swift'
POLICY = 'Lingrove/Support/Vocabulary/VocabularyPolicy.swift'
EXPLANATION = 'Lingrove/Support/LanguageLearning/LearningExplanationPolicy.swift'
BUDGET = 'Lingrove/Support/Prompting/ModelContextBudget.swift'


def sources(repo):
    result = DOC.sources(repo) + [DOC.source(repo, path) for path in FULL_PATHS]
    connection = DOC.source(repo, CONNECTION)
    if connection.count('import Logging\n') != 1 or connection.count('LingroveLogger.learning.error(') != 2:
        raise RuntimeError('Frozen SQLite logging boundary changed')
    result.append('\n'.join(line for line in connection.split('\n')
                  if line != 'import Logging' and 'LingroveLogger.learning.error(' not in line))
    policy = DOC.source(repo, POLICY)
    result.append('import Foundation\nimport CryptoKit\nnonisolated enum VocabularyPolicy {\n' +
        DOC.between(policy, '    static func normalized(_ text:', '    static func exampleKey(') +
        DOC.between(policy, '    static func valid(_ range:', '    /// Reconciles') +
        DOC.between(policy, '    private static func key(_ parts:', '\n}\n\nnonisolated enum VocabularyError') + '\n}\n')
    explanation = DOC.source(repo, EXPLANATION)
    result.append('import Foundation\nnonisolated enum LearningExplanationPolicy {\n' +
        DOC.between(explanation, '    static func isValid(_ input:', '    static func prompt(') +
        DOC.between(explanation, '    static func isValid(_ result:', '    static func parse(') + '}\n')
    budget = DOC.source(repo, BUDGET)
    line = '    static let maximumTextBytes = 262_144'
    if budget.count(line) != 1: raise RuntimeError('Frozen text budget boundary changed')
    result.append('nonisolated enum ModelContextBudget {\n' + line + '\n}\n')
    return result


DRIVER = DOC.DRIVER[:DOC.DRIVER.index('var observations:')] + r'''
func describe(_ rows: [VocabularyDatabaseRecord]) throws -> [[String: Any]] {
    try rows.map { row in
        let payload = try JSONSerialization.jsonObject(with: row.payload.encoded(), options: .fragmentsAllowed)
        return ["table": row.table.rawValue, "id": row.id, "parent": row.parentID as Any? ?? NSNull(),
            "position": row.position, "extra": row.extra.map { $0 as Any? ?? NSNull() }, "payload": payload]
    }
}
func storedRows(_ connection: VocabularySQLiteConnection) throws -> [[String: Any]] {
    var result: [[String: Any]] = []
    for table in VocabularyDatabaseRecord.Table.allCases {
        let columns = ["id", "parent_id", "position", "payload"] + table.extraColumns
        for row in try connection.rows("SELECT " + columns.joined(separator: ",") + " FROM " + table.rawValue) {
            guard let id = row[0].string, let position = row[2].integer, let data = row[3].data,
                  row[1] == .null || row[1].string != nil,
                  row.dropFirst(4).allSatisfy({ $0 == .null || $0.string != nil }) else { throw VocabularyError.invalidArchive }
            let payload = try JSONSerialization.jsonObject(with: data, options: .fragmentsAllowed)
            result.append(["table": table.rawValue, "id": id, "parent": row[1].string as Any? ?? NSNull(),
                "position": position, "extra": row.dropFirst(4).map { $0.string as Any? ?? NSNull() }, "payload": payload])
        }
    }
    return result
}
func observe(_ input: URL, _ database: URL, _ migrate: Bool) throws -> [String: Any] {
    let document = try JSONDecoder().decode(VocabularyDocument.self, from: Data(contentsOf: input))
    let projected = try VocabularyDatabaseProjection.rows(document)
    let reconstructed = try VocabularyDatabaseProjection.document(Array(projected.values), hasTemplates: document.reviewTemplates != nil)
    guard reconstructed == document else { throw VocabularyError.invalidArchive }
    let writer = try VocabularySQLiteDatabase(url: database)
    let revision = try writer.write(document, previous: VocabularyDocument(), expectedRevision: 0)
    guard revision == 1 else { throw VocabularyError.invalidArchive }
    writer.connection.close()
    let reader = try VocabularySQLiteDatabase(url: database)
    let snapshot = try reader.read()
    guard snapshot.revision == 1, snapshot.document == document else { throw VocabularyError.invalidArchive }
    var rejected = false
    do { _ = try reader.write(document, previous: document, expectedRevision: 0) }
    catch VocabularyError.editConflict { rejected = true }
    guard rejected, try reader.revision() == 1 else { throw VocabularyError.invalidArchive }
    let actual = try storedRows(reader.connection)
    var migrationRevision: UInt64? = nil
    var migratedDates: [String: String]? = nil
    var migratedRows: [[String: Any]]? = nil
    if migrate {
        guard document.classification == VocabularyClassificationIndex() else { throw VocabularyError.invalidArchive }
        for table in VocabularySQLiteClassificationSchema.tables.reversed() { try reader.connection.execute("DROP TABLE " + table.rawValue) }
        try reader.connection.execute("PRAGMA user_version=4")
        reader.connection.close()
        let reopened = try VocabularySQLiteDatabase(url: database)
        let migrated = try reopened.read()
        guard migrated.revision == 1, migrated.document == document,
              try reopened.connection.rows("PRAGMA user_version") == [[.integer(5)]] else { throw VocabularyError.invalidArchive }
        migrationRevision = migrated.revision; migratedDates = dateBits(migrated.document)
        migratedRows = try storedRows(reopened.connection)
        reopened.connection.close()
    } else { reader.connection.close() }
    return ["projection": try describe(Array(projected.values)), "stored": actual,
        "dates": dateBits(snapshot.document), "revision": snapshot.revision, "staleRejected": rejected,
        "migrationRevision": migrationRevision as Any? ?? NSNull(), "migrationDates": migratedDates as Any? ?? NSNull(),
        "migrationRows": migratedRows as Any? ?? NSNull()]
}
var output: [[String: Any]] = []
let args = Array(CommandLine.arguments.dropFirst())
guard args.count % 3 == 0 else { fatalError("bad arguments") }
for offset in stride(from: 0, to: args.count, by: 3) {
    output.append(try observe(URL(fileURLWithPath: args[offset]), URL(fileURLWithPath: args[offset + 1]), args[offset + 2] == "migrate"))
}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: output, options: [.sortedKeys]))
'''


def ordered(rows):
    return sorted(rows, key=lambda row: (row['table'], row['id']))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--documents', type=Path, default=ROOT / 'fixtures/parity/vocabulary-document-frozen-v1.json')
    parser.add_argument('--rows', type=Path, default=ROOT / 'fixtures/parity/vocabulary-relational-frozen-v1.json')
    args = parser.parse_args()
    docs = json.loads(args.documents.read_text(encoding='utf-8'))
    corpus = json.loads(args.rows.read_text(encoding='utf-8'))
    if docs.get('macReference') != REVISION or corpus.get('macReference') != REVISION or corpus.get('provenance') != 'windows-projection-source-shaped':
        raise SystemExit('Unexpected frozen relational probe provenance')
    cases = docs['cases']; expected = {case['id']: case['rows'] for case in corpus['cases']}
    if len(expected) != len(corpus['cases']) or len({case['id'] for case in cases}) != len(cases) or set(expected) != {case['id'] for case in cases}:
        raise SystemExit('Document and row case IDs differ')
    if len({row['table'] for row in expected['full-v8']}) != 29: raise SystemExit('Full case must cover all 29 tables')
    with tempfile.TemporaryDirectory(prefix='lingrove-frozen-relational-') as tmp:
        root = Path(tmp); files = []
        for index, value in enumerate(sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); files.append(str(path))
        main_file = root / 'main.swift'; main_file.write_text(DRIVER, encoding='utf-8'); binary = root / 'probe'
        subprocess.run(['swiftc', '-swift-version', '6', '-lsqlite3', *files, str(main_file), '-o', str(binary)], check=True)
        probes = []
        for index, case in enumerate(cases):
            path = root / f'case{index}.json'; path.write_text(json.dumps(case['input'], ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
            probes.extend([str(path), str(root / f'case{index}.sqlite'), 'migrate' if case['id'] == 'upgrade-v7' else 'normal'])
        results = json.loads(subprocess.check_output([str(binary), *probes]))
    if len(results) != len(cases): raise SystemExit('Native observation count mismatch')
    for case, actual in zip(cases, results):
        target = ordered(expected[case['id']]); bits = DOC.expected_bits(case['input'])
        if ordered(actual['projection']) != target or ordered(actual['stored']) != target or actual['dates'] != bits or actual['revision'] != 1 or not actual['staleRejected']:
            raise SystemExit('Native relational projection/SQLite mismatch: ' + case['id'])
        if case['id'] == 'upgrade-v7' and (actual['migrationRevision'] != 1 or actual['migrationDates'] != bits or ordered(actual['migrationRows']) != target):
            raise SystemExit('Native populated schema 4-to-5 mismatch: ' + case['id'])
    print(f'Verified {len(cases)} frozen relational cases, all 29 tables, named SQLite reopen and populated 4-to-5 migration')


if __name__ == '__main__': main()
