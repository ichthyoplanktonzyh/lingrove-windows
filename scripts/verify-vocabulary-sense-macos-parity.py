#!/usr/bin/env python3
"""Execute pinned Mac save/sense/attachment policies on immutable fixture inputs, without IO."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

from macos_vocabulary_reference import BASELINE, sense_policy_sources

DRIVER = r'''
import Foundation
func id(_ n: Int) -> UUID { UUID(uuidString: String(format: "00000000-0000-0000-0000-%012x", n))! }
func label(_ value: UUID) -> String {
    let text = value.uuidString.lowercased()
    return text.hasPrefix("00000000-0000-0000-0000-") ? text : "new"
}
let now = Date(timeIntervalSince1970: 0), later = Date(timeIntervalSince1970: 1)
func usage(_ n: Int = 10, _ text: String = "The bank.", _ offset: Int = 4) -> VocabularyExample {
    .init(id: id(n), text: text, translation: "银行。", translationLanguage: .chinese,
        ranges: [.init(NSRange(location: offset, length: 4))], completeness: .sentence,
        meaning: "context meaning", appBundleIdentifier: "fixture.app", createdAt: now)
}
func capture(_ example: VocabularyExample? = nil) -> VocabularyCapture {
    .init(expression: "bank", dictionaryForm: "bank", language: .english, kind: .word, example: example)
}
func sense(_ n: Int, _ examples: [VocabularyExample] = []) -> VocabularySense {
    .init(id: id(n), meaning: "", examples: examples, createdAt: now, updatedAt: now)
}
func entry(_ senses: [VocabularySense]) -> VocabularyEntry {
    .init(id: id(1), expression: "bank", dictionaryForm: "bank", language: .english,
        kind: .word, senses: senses, createdAt: now, updatedAt: now)
}
func record(_ n: Int, _ input: VocabularyCapture, _ meaning: String = "generated", _ language: LearningLanguage = .chinese) -> LearningExplanationRecord {
    .init(id: id(n), input: LearningExplanationPolicy.input(input), language: language,
        result: .init(meaning: meaning, explanation: "Explanation"), model: "fixture", createdAt: now)
}
func records(_ values: [LearningExplanationRecord]) -> [[String: Any]] {
    values.map { ["id": label($0.id), "meaning": $0.result.meaning, "language": $0.language.rawValue] }
}
func view(_ value: VocabularyEntry) -> [String: Any] {
    ["id": label(value.id), "expression": value.expression, "created": value.createdAt.timeIntervalSince1970,
     "updated": value.updatedAt.timeIntervalSince1970, "senses": value.senses.map { s -> [String: Any] in
        ["id": label(s.id), "meaning": s.meaning, "language": s.meaningLanguage?.rawValue as Any? ?? NSNull(),
         "origin": s.meaningOrigin.rawValue, "note": s.note, "tags": s.tags, "standalone": s.isSavedWithoutContext,
         "variants": s.standaloneMeanings.map { ["text": $0.text, "language": $0.language?.rawValue as Any? ?? NSNull()] },
         "alternate": s.alternateMeanings.map { $0.text }, "aliases": s.mergedFrom.map(label),
         "content": s.content.map { $0.map { label($0.id) } } as Any? ?? NSNull(),
         "review": s.review.map { label($0.plan.id) } as Any? ?? NSNull(),
         "preserved": s.review?.preservedPlans.map { label($0.id) } ?? [],
         "cards": s.reviewCards.map { $0.map { label($0.plan.id) } } as Any? ?? NSNull(),
         "created": s.createdAt.timeIntervalSince1970, "updated": s.updatedAt.timeIntervalSince1970,
         "records": records(s.explanations), "usages": s.examples.map { u -> [String: Any] in
            ["id": label(u.id), "text": u.text, "ranges": u.ranges.map { [$0.location, $0.length] },
             "translation": u.translation, "translationLanguage": u.translationLanguage?.rawValue as Any? ?? NSNull(),
             "variants": u.translationVariants.map { ["text": $0.text, "language": $0.language?.rawValue as Any? ?? NSNull()] },
             "meaning": u.meaning, "language": u.meaningLanguage?.rawValue as Any? ?? NSNull(), "origin": u.meaningOrigin.rawValue,
             "app": u.appBundleIdentifier as Any? ?? NSNull(), "created": u.createdAt.timeIntervalSince1970, "records": records(u.explanations)]
         }]
     }]
}
func observe(_ mode: String) -> [String: Any] {
    do {
        if mode.hasPrefix("save-") {
            var c = capture(usage()), d = VocabularyDocument()
            if mode == "save-new" || mode == "save-attach" {
                if mode == "save-attach" { c.explanation = record(100, c) }
                let result = try VocabularyPolicy.save(c, in: &d, now: later)
                return ["returnedMatches": result == d.entries[0].id, "contains": VocabularyPolicy.contains(c, in: d.entries), "entries": d.entries.map(view)]
            }
            if mode == "save-standalone" { c = capture(); c.meaning = "first"; c.meaningLanguage = .chinese }
            _ = try VocabularyPolicy.save(c, in: &d, now: now)
            switch mode {
            case "save-duplicate": c.example!.translation = "Banco."; c.example!.translationLanguage = .spanish
            case "save-context": c.example = usage(11, "A bank opened.", 2)
            case "save-cross": c = .init(expression: "other", dictionaryForm: "other", language: .english, kind: .word, example: usage())
            case "save-occurrence":
                d = VocabularyDocument(); c.example = usage(10, "bank and bank.", 0)
                _ = try VocabularyPolicy.save(c, in: &d, now: now); c.example!.ranges = [.init(NSRange(location: 9, length: 4))]
            case "save-standalone":
                d.entries[0].senses[0].meaning = "User summary"; d.entries[0].senses[0].meaningOrigin = .user
                c.meaning = "second"; c.meaningLanguage = .spanish
            case "save-missing": c.senseID = id(999)
            case "save-conflict": d.entries[0].senses.append(sense(3)); c.senseID = id(3)
            case "save-standalone-conflict":
                d.entries[0].senses[0].isSavedWithoutContext = true; d.entries[0].senses.append(sense(3)); c = capture(); c.senseID = id(3)
            default: fatalError("Unknown save mode")
            }
            let firstEntry = d.entries[0].id, firstSense = d.entries[0].senses[0].id
            let result = try VocabularyPolicy.save(c, in: &d, now: later)
            return ["sameEntry": result == firstEntry, "returnedEntryMatches": d.entries.first { $0.id == result }?.expression == c.expression,
                "sameFirstSense": d.entries[0].senses[0].id == firstSense,
                "usageIDsUnique": Set(d.entries.flatMap(\.examples).map(\.id)).count == d.entries.flatMap(\.examples).count, "entries": d.entries.map(view)]
        }
        var a = sense(2, [usage()]), b = sense(3), e = entry([])
        if mode == "destination-alias" || mode == "destination-ambiguous" {
            a.mergedFrom = [id(99)]; e = entry([a]); var c = capture(usage())
            if mode == "destination-alias" { c.senseID = id(99) } else { b.examples = [usage(11)]; e.senses.append(b) }
            let dest = VocabularySensePolicy.destination(c, in: e)
            return ["contains": VocabularyPolicy.contains(c, in: [e]), "sense": dest.map { label($0.senseID) } as Any? ?? NSNull(),
                "usage": dest?.exampleID.map(label) as Any? ?? NSNull(), "aliasIndex": VocabularySensePolicy.index(id(99), in: e) as Any? ?? NSNull()]
        }
        if mode.hasPrefix("merge-") {
            a.note = "Source note"; b.note = "Target note"
            switch mode {
            case "merge-full":
                a.meaning = "Source"; a.meaningLanguage = .chinese; a.tags = ["f", "café"]
                a.alternateMeanings = [.init(text: "Older source", language: nil)]; a.mergedFrom = [id(99)]
                a.isSavedWithoutContext = true; a.standaloneMeanings = [.init(text: "standalone", language: .spanish)]
                a.content = [.init(id: id(200), content: .text(.init(text: "target"))), .init(id: id(201), content: .text(.init(text: "source")))]
                a.review = .init(plan: .init(id: id(300))); a.reviewCards = [.init(plan: .init(id: id(302)))]
                a.explanations = [record(100, capture(), "source record")]
                b.meaning = "Target"; b.meaningOrigin = .user; b.examples = [usage(11, "A bank opened.", 2)]
                b.tags = ["😀", "cafe\u{301}", "\u{e000}"]; b.content = [.init(id: id(200), content: .text(.init(text: "target")))]
                b.review = .init(plan: .init(id: id(301))); b.reviewCards = []
            case "merge-adopt": a.meaning = "Adopt"; a.meaningLanguage = .spanish; a.meaningOrigin = .user
            case "merge-empty": a.examples = []; a.note = ""; b.note = ""
            case "merge-duplicate": b.examples = [usage(11)]
            case "merge-alias": a.mergedFrom = [id(99)]
            default: fatalError("Unknown merge mode")
            }
            e = entry([a, b]); try VocabularySensePolicy.merge(source: mode == "merge-alias" ? id(99) : a.id, into: b.id, in: &e, now: later)
            return ["entry": view(e)]
        }
        if mode.hasPrefix("move-") {
            a.note = "Keep source"; a.review = .init(plan: .init(id: id(300))); a.mergedFrom = [id(99)]; b.mergedFrom = [id(98)]
            if mode == "move-duplicate" { b.examples = [usage(11)] }
            e = entry([a, b])
            let destination = try VocabularySensePolicy.move(exampleID: mode == "move-missing" ? id(999) : id(10),
                from: mode == "move-source-alias" ? id(99) : a.id,
                to: mode == "move-new" ? nil : mode == "move-target-alias" ? id(98) : mode == "move-same" ? a.id : b.id, in: &e, now: later)
            return ["target": label(destination), "targetOwnsUsage": e.senses.first { $0.id == destination }?.examples.contains { $0.id == id(10) } == true,
                "entry": view(e)]
        }
        if mode.hasPrefix("record-") {
            var c = capture(usage()), r = record(100, capture(usage()))
            switch mode {
            case "record-moved": e = entry([a, b]); _ = try VocabularySensePolicy.move(exampleID: id(10), from: a.id, to: b.id, in: &e, now: later)
            case "record-deleted": a.examples = []; e = entry([a])
            case "record-edited": a.examples[0].text = "The bank changed."; e = entry([a])
            case "record-user": a.examples[0].meaning = "User wording"; a.examples[0].meaningOrigin = .user; e = entry([a])
            case "record-general", "record-nonempty", "record-userblank":
                c = capture(); r = record(100, c, "first"); a.examples = []; a.isSavedWithoutContext = true
                if mode == "record-nonempty" { a.meaning = "Captured core" }
                if mode == "record-userblank" { a.meaningOrigin = .user }
                e = entry([a]); VocabularyPolicy.storeExplanation(r, in: &e); r = record(101, c, "second")
            case "record-bounded":
                a.examples[0].explanations = (0..<24).map { record(200 + $0, c, "old \($0)", .spanish) }; e = entry([a])
            case "record-invalid":
                e = entry([a]); r = .init(id: r.id, input: r.input, language: r.language,
                    result: .init(meaning: "", explanation: ""), model: r.model, createdAt: r.createdAt)
            default: fatalError("Unknown record mode")
            }
            let accepts = VocabularyPolicy.accepts(r, entry: e)
            VocabularyPolicy.storeExplanation(r, in: &e)
            if mode == "record-bounded" { VocabularyPolicy.storeExplanation(record(101, c, "refresh"), in: &e) }
            return ["accepts": accepts, "entry": view(e)]
        }
        fatalError("Unknown mode")
    } catch let error as VocabularyError { return ["error": String(describing: error)] }
    catch { fatalError("Unexpected fixture error") }
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let results = (corpus["cases"] as! [[String: Any]]).map { row in ["id": row["id"]!, "expected": observe(row["operation"] as! String)] }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-sense-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Author actual observations into a NEW file')
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if (corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows
            or any(not value for value in ids) or len(set(ids)) != len(ids)):
        parser.error('Sense cases require pinned version and unique IDs')
    for row in rows:
        if (row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') not in (
                'Cotabby/Support/Vocabulary/VocabularyPolicy.swift', 'Cotabby/Support/Vocabulary/VocabularySensePolicy.swift')):
            parser.error('Sense probes require explicit source provenance')
    with tempfile.TemporaryDirectory(prefix='lingrove-sense-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(sense_policy_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'sense-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac sense transaction observations'); return
    if len(observations) != len(rows): raise SystemExit('Sense observation count changed')
    for row, observed in zip(rows, observations):
        if observed != {'id': row['id'], 'expected': row['expected']}: raise SystemExit(f"Mac sense mismatch: {row['id']}")
    print(f'Verified {len(observations)} sense transaction cases against actual pinned Mac policies')


if __name__ == '__main__':
    main()
