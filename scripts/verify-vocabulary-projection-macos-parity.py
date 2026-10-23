#!/usr/bin/env python3
"""Execute pinned typed relational projection/reconstruction without codecs or SQLite."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

from macos_vocabulary_reference import BASELINE, database_projection_sources

DRIVER = r'''
import Foundation
func id(_ n: Int) -> UUID { UUID(uuidString: String(format: "00000000-0000-0000-0000-%012x", n))! }
let now = Date(timeIntervalSince1970: 0)
func block(_ n: Int) -> VocabularyContentBlock { .init(id: id(n), content: .text(.init(text: "literal"))) }
func record(_ n: Int, _ capture: VocabularyCapture) -> LearningExplanationRecord {
    .init(id: id(n), input: LearningExplanationPolicy.input(capture), language: .chinese,
        result: .init(meaning: "meaning", explanation: "explanation"), model: "fixture", createdAt: now)
}
func document() -> VocabularyDocument {
    var usage = VocabularyExample(id: id(3), text: "The bank.", translation: "原句", ranges: [.init(NSRange(location: 4, length: 4))], completeness: .sentence, meaning: "context", createdAt: now)
    var sense = VocabularySense(id: id(2), meaning: "core", note: "note", tags: ["Work"], isSavedWithoutContext: true, createdAt: now, updatedAt: now)
    usage.explanations = [record(100, .init(expression: "bank", dictionaryForm: "bank", language: .english, kind: .word, example: usage))]
    sense.examples = [usage]; sense.explanations = [record(100, .init(expression: "bank", dictionaryForm: "bank", language: .english, kind: .word))]
    sense.content = [block(20)]
    let layout = ReviewLayout(front: [.init(id: id(50), source: .custom, content: [block(60)])])
    let binding = AnkiNoteBinding(id: id(40), originSenseID: id(99), endpoint: "http://localhost", profile: "profile", noteID: 1,
        model: "model", deck: "deck", identityField: "ID", mapping: [.identity: "ID"], fields: ["ID": id(99).uuidString.lowercased()])
    let review = VocabularyReview(plan: .init(id: id(30), layout: layout), bindings: [binding], activeBindingID: binding.id, preservedPlans: [.init(id: id(31))])
    sense.review = review; sense.reviewCards = [review]
    return .init(entries: [.init(id: id(1), expression: "bank", dictionaryForm: "bank", language: .english, kind: .word,
        senses: [sense], createdAt: now, updatedAt: now)], reviewTemplates: [.init(id: id(70), name: "Template", layout: layout)])
}
func describe(_ rows: [VocabularyDatabaseRecord], _ document: VocabularyDocument) -> [String: Any] {
    let sorted = rows.sorted { ($0.table.rawValue, $0.id) < ($1.table.rawValue, $1.id) }
    return ["rows": sorted.map { ["table": $0.table.rawValue, "id": $0.id, "parent": $0.parentID as Any? ?? NSNull(), "position": $0.position, "extra": $0.extra] },
        "templatesPresent": document.reviewTemplates != nil, "entryCount": document.entries.count,
        "usageCount": document.entries.flatMap(\.examples).count,
        "legacyBindings": document.entries.first?.senses.first?.review?.bindings.count ?? 0]
}
func observe(_ mode: String) -> [String: Any] {
    do {
        if mode == "metadata" {
            return ["tables": VocabularyDatabaseRecord.Table.allCases.map { ["name": $0.rawValue, "parent": $0.parent?.rawValue as Any? ?? NSNull(), "extra": $0.extraColumns] }]
        }
        var d = document()
        switch mode {
        case "emptyAbsent": d = .init()
        case "emptyPresent": d = .init(reviewTemplates: [])
        case "contentAbsent": d.entries[0].senses[0].content = nil
        case "contentEmpty": d.entries[0].senses[0].content = []
        case "cardsAbsent": d.entries[0].senses[0].reviewCards = nil
        case "cardsEmpty": d.entries[0].senses[0].reviewCards = []
        default: break
        }
        var rows = Array(try VocabularyDatabaseProjection.rows(d).values)
        let entryIndex = rows.firstIndex { $0.table == .entries }
        func replacing(_ row: VocabularyDatabaseRecord, id: String? = nil, parent: String? = nil, position: Int? = nil,
                       payload: VocabularyDatabaseRecord.Payload? = nil, extra: [String]? = nil) -> VocabularyDatabaseRecord {
            .init(table: row.table, id: id ?? row.id, parentID: parent ?? row.parentID, position: position ?? row.position,
                payload: payload ?? row.payload, extra: extra ?? row.extra)
        }
        switch mode {
        case "valid", "emptyAbsent", "emptyPresent", "contentAbsent", "contentEmpty", "cardsAbsent", "cardsEmpty": break
        case "duplicate": rows.append(rows[0])
        case "negativePosition": rows[entryIndex!] = replacing(rows[entryIndex!], position: -1)
        case "positionGap": rows[entryIndex!] = replacing(rows[entryIndex!], position: 1)
        case "wrongEntryParent": rows[entryIndex!] = replacing(rows[entryIndex!], parent: "orphan")
        case "wrongEntryID": rows[entryIndex!] = replacing(rows[entryIndex!], id: id(999).uuidString)
        case "wrongExtra": rows[entryIndex!] = replacing(rows[entryIndex!], extra: ["tampered", "en", "bank"])
        case "wrongPayload": rows[entryIndex!] = replacing(rows[entryIndex!], payload: .usage(d.entries[0].examples[0]))
        case "smuggledEntry": rows[entryIndex!] = replacing(rows[entryIndex!], payload: .entry(d.entries[0]))
        case "orphanUsage": let n = rows.firstIndex { $0.table == .usages }!; rows[n] = replacing(rows[n], parent: "orphan")
        case "smuggledSense": let n = rows.firstIndex { $0.table == .senses }!; rows[n] = replacing(rows[n], payload: .sense(d.entries[0].senses[0]))
        case "hiddenBlocks", "hiddenCards":
            let n = rows.firstIndex { $0.table == .senses }!; guard case var .sense(s) = rows[n].payload else { fatalError() }
            if mode == "hiddenBlocks" { s.content = nil } else { s.reviewCards = nil }; rows[n] = replacing(rows[n], payload: .sense(s))
        case "smuggledBindings": let n = rows.firstIndex { $0.table == .reviews }!; rows[n] = replacing(rows[n], payload: .review(d.entries[0].senses[0].review!))
        case "wrongReviewID": let n = rows.firstIndex { $0.table == .reviews }!; rows[n] = replacing(rows[n], id: "unknown")
        case "reviewOrder": for n in rows.indices where rows[n].table == .reviews { rows[n] = replacing(rows[n], position: 1 - rows[n].position) }
        case "removeUsage": rows.removeAll { $0.table == .usages || $0.table == .usageExplanations }
        case "removeBinding": rows.removeAll { $0.table == .bindings && $0.parentID?.hasSuffix(":legacy") == true }
        case "falseTemplateFlag": break
        default: fatalError("Unknown projection mode")
        }
        let result = try VocabularyDatabaseProjection.document(rows, hasTemplates: mode != "falseTemplateFlag" && d.reviewTemplates != nil)
        var output = describe(Array(try VocabularyDatabaseProjection.rows(result).values), result)
        output["equalOriginal"] = result == d
        return output
    } catch let error as VocabularyError { return ["error": String(describing: error)] }
    catch { fatalError("Unexpected projection error") }
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let results = (corpus["cases"] as! [[String: Any]]).map { row in ["id": row["id"]!, "expected": observe(row["operation"] as! String)] }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-projection-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Author actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if (corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not value for value in ids) or len(set(ids)) != len(ids)):
        parser.error('Projection cases require pinned version and unique IDs')
    if any(row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != ('Cotabby/Models/Vocabulary/VocabularyDatabaseModels.swift' if row.get('operation') == 'metadata' else 'Cotabby/Support/Vocabulary/VocabularyDatabaseProjection.swift') for row in rows):
        parser.error('Projection probes require exact source provenance')
    with tempfile.TemporaryDirectory(prefix='lingrove-projection-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(database_projection_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'projection-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac relational projection observations'); return
    if len(observations) != len(rows): raise SystemExit('Projection observation count changed')
    for row, observed in zip(rows, observations):
        if observed != {'id': row['id'], 'expected': row['expected']}: raise SystemExit(f"Mac projection mismatch: {row['id']}")
    print(f'Verified {len(observations)} relational projection cases against actual pinned Mac methods')


if __name__ == '__main__':
    main()
