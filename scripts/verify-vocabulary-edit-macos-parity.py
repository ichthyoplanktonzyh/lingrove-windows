#!/usr/bin/env python3
"""Execute original private Mac three-way merge helpers and commit pruning, without storage fakes."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

from macos_vocabulary_reference import BASELINE, archive_edit_sources

DRIVER = r'''
import Foundation
func id(_ n: Int) -> UUID { UUID(uuidString: String(format: "00000000-0000-0000-0000-%012x", n))! }
func label(_ value: UUID) -> String { value.uuidString.lowercased() }
let now = Date(timeIntervalSince1970: 0)
func usage(_ n: Int = 3, _ text: String = "The bank.", _ offset: Int = 4) -> VocabularyExample {
    .init(id: id(n), text: text, translation: "", ranges: [.init(NSRange(location: offset, length: 4))], completeness: .sentence, meaning: "draft", createdAt: now)
}
func sense(_ n: Int = 2) -> VocabularySense { .init(id: id(n), meaning: "", examples: n == 2 ? [usage()] : [], createdAt: now, updatedAt: now) }
func entry(_ senses: [VocabularySense]) -> VocabularyEntry { .init(id: id(1), expression: "bank", dictionaryForm: "bank", language: .english, kind: .word, senses: senses, createdAt: now, updatedAt: now) }
func record(_ n: Int, _ entry: VocabularyEntry, _ sense: VocabularySense, _ usage: VocabularyExample?) -> LearningExplanationRecord {
    .init(id: id(n), input: LearningExplanationPolicy.input(LearningExplanationPolicy.capture(entry, sense: sense, example: usage)),
        language: .chinese, result: .init(meaning: "meaning", explanation: "explanation"), model: "fixture", createdAt: now)
}
func view(_ senses: [VocabularySense]) -> [[String: Any]] { senses.map { s in
    ["id": label(s.id), "meaning": s.meaning, "language": s.meaningLanguage?.rawValue as Any? ?? NSNull(), "origin": s.meaningOrigin.rawValue,
     "note": s.note, "tags": s.tags, "standalone": s.isSavedWithoutContext, "variants": s.standaloneMeanings.map(\.text),
     "records": s.explanations.map { label($0.id) }, "content": s.content.map { $0.map { label($0.id) } } as Any? ?? NSNull(),
     "updated": s.updatedAt != now, "usages": s.examples.map { u in
        ["id": label(u.id), "text": u.text, "translation": u.translation, "meaning": u.meaning,
         "origin": u.meaningOrigin.rawValue, "records": u.explanations.map { label($0.id) }] }]
} }
func observe(_ mode: String) -> [String: Any] {
    let reference = VocabularyArchiveEditReference()
    var a = [sense()], current = a, edited = a
    do {
        if mode.hasPrefix("prune-") {
            var e = entry(a)
            a[0].explanations = [record(100, e, a[0], nil)]; a[0].examples[0].explanations = [record(101, e, a[0], a[0].examples[0])]
            switch mode {
            case "prune-moved": var s = sense(4); s.examples = a[0].examples; a[0].examples = []; a.append(s)
            case "prune-edited": a[0].examples[0].text = "The bank changed."
            case "prune-general": a[0].isSavedWithoutContext = true
            case "prune-form": a[0].isSavedWithoutContext = true; e.dictionaryForm = "other"
            default: fatalError("Unknown prune mode")
            }
            e.senses = a
            return ["senses": view(reference.pruneProbe(.init(entries: [e])).entries[0].senses)]
        }
        switch mode {
        case "merge-current-append": current.append(sense(4)); edited[0].note = "Edited note"
        case "merge-conflict-note": current[0].note = "Live"; edited[0].note = "Edited"
        case "merge-same-note": current[0].note = "Same"; edited[0].note = "Same"
        case "merge-unchanged": current[0].note = "Live"
        case "merge-delete-changed": current[0].note = "Live"; edited = []
        case "merge-new-alias": current[0].mergedFrom = [id(99)]; edited.append(sense(99))
        case "merge-new": edited.append(sense(4))
        case "merge-delete": a.append(sense(4)); current = a; edited = [a[0]]
        case "merge-meaning": edited[0].meaning = "My meaning"; edited[0].meaningLanguage = .chinese
        case "merge-adopt": edited[0].meaningOrigin = .user
        case "merge-standalone": a[0].isSavedWithoutContext = true; current = a; edited = a; edited[0].meaning = "My general"
        case "merge-edit-clear": a[0].examples[0].meaningOrigin = .user; current = a; edited = a; edited[0].examples[0].text = "The bank changed."; edited[0].examples[0].meaning = ""
        case "merge-usage-meaning": edited[0].examples[0].meaning = "My contextual"
        case "merge-usage-conflict": current[0].examples[0].translation = "Live"; edited[0].examples[0].meaning = "Edited"
        case "merge-usage-deleted": current[0].examples = []; edited[0].examples[0].meaning = "Edited"
        case "merge-new-duplicate": current[0].examples.append(usage(4, "A bank opened.", 2)); edited[0].examples.append(usage(5, "A bank opened.", 2))
        case "merge-usage-delete-conflict": current[0].examples[0].translation = "Live"; edited[0].examples = []
        case "merge-no-standalone": a[0].isSavedWithoutContext = true; a[0].standaloneMeanings = [.init(text: "general", language: nil)]; current = a; edited = a; current[0].isSavedWithoutContext = false; current[0].standaloneMeanings = []; edited[0].examples = []
        case "merge-canonical": a[0].note = "cafe\u{301}"; a[0].tags = ["cafe\u{301}"]; current = a; edited = a; current[0].note = "Live"; current[0].tags = ["Live"]; edited[0].note = "café"; edited[0].tags = ["café"]
        case "merge-content-conflict": current[0].content = []; edited[0].content = [.init(id: id(20), content: .text(.init(text: "literal")))]
        default: fatalError("Unknown merge mode")
        }
        return ["senses": view(try reference.mergeProbe(a, current, edited))]
    } catch let error as VocabularyError { return ["error": String(describing: error)] }
    catch { fatalError("Unexpected edit failure") }
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let results = (corpus["cases"] as! [[String: Any]]).map { row in ["id": row["id"]!, "expected": observe(row["operation"] as! String)] }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-edit-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Author actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if (corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not value for value in ids) or len(set(ids)) != len(ids)):
        parser.error('Edit cases require pinned version and unique IDs')
    if any(row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != 'Cotabby/Services/Vocabulary/VocabularyArchive.swift' for row in rows):
        parser.error('Edit probes require exact source provenance')
    with tempfile.TemporaryDirectory(prefix='lingrove-edit-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(archive_edit_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'edit-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac merge/pruning observations'); return
    if len(observations) != len(rows): raise SystemExit('Edit observation count changed')
    for row, observed in zip(rows, observations):
        if observed != {'id': row['id'], 'expected': row['expected']}: raise SystemExit(f"Mac edit mismatch: {row['id']}")
    print(f'Verified {len(observations)} merge/pruning cases against actual pinned Mac helpers')


if __name__ == '__main__':
    main()
