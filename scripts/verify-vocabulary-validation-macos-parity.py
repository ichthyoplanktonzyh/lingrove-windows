#!/usr/bin/env python3
"""Execute original pinned archive/layout/receipt admission methods without storage or networking."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

from macos_vocabulary_reference import BASELINE, archive_validation_sources, source

DRIVER = r'''
import Foundation
// JSONDecoder supplies native Swift strings, as notebook/wire decoding does. Keep the old
// NSString bridge as a separately observed diagnostic, not as the production admission oracle.
func nativeInput(_ value: AnkiValue) -> Any {
    switch value {
    case let .object(map): return map.mapValues(nativeInput)
    case let .array(values): return values.map(nativeInput)
    case let .string(value): return value
    case let .number(value): return Int(value)
    case let .bool(value): return value
    case .null: return NSNull()
    }
}
func id(_ n: Int) -> UUID { UUID(uuidString: String(format: "00000000-0000-0000-0000-%012x", n))! }
let now = Date(timeIntervalSince1970: 0)
func usage(_ n: Int = 3) -> VocabularyExample {
    .init(id: id(n), text: "The bank.", translation: "", ranges: [.init(NSRange(location: 4, length: 4))], completeness: .sentence, meaning: "", createdAt: now)
}
func sense() -> VocabularySense { .init(id: id(2), meaning: "", examples: [usage()], createdAt: now, updatedAt: now) }
func entry() -> VocabularyEntry { .init(id: id(1), expression: "bank", dictionaryForm: "bank", language: .english, kind: .word, senses: [sense()], createdAt: now, updatedAt: now) }
func block(_ n: Int) -> VocabularyContentBlock { .init(id: id(n), content: .text(.init(text: "literal"))) }
func part(_ n: Int) -> ReviewCardPart { .init(id: id(n), source: .custom) }
func binding(_ n: Int = 20, _ note: Int64? = 1, _ endpoint: String = "http://localhost:8765/") -> AnkiNoteBinding {
    .init(id: id(n), originSenseID: id(0xabcdef), endpoint: endpoint, profile: " ", noteID: note, model: " ", deck: "", identityField: "ID",
        mapping: [.identity: "ID"], fields: ["ID": id(0xabcdef).uuidString.lowercased()])
}
func record(_ n: Int, _ input: LearningExplanationInput) -> LearningExplanationRecord {
    .init(id: id(n), input: input, language: .chinese, result: .init(meaning: "meaning", explanation: "explanation"), model: "", createdAt: now)
}
func observe(_ row: [String: Any]) -> [String: Any] {
    let operation = row["operation"] as! String, mode = row["mode"] as? String ?? ""
    let count = row["count"] as? Int ?? 0
    if operation == "endpoint" { return ["valid": AnkiReviewPolicy.endpoint(row["text"] as! String) != nil] }
    if operation == "filename" { return ["valid": SpeechExportPolicy.validFilename(row["text"] as! String)] }
    do {
        let validator = VocabularyArchive()
        switch operation {
        case "document":
            var d = VocabularyDocument(entries: [entry()])
            switch mode {
            case "empty": d.entries = []
            case "valid": break
            case "schema": d.schemaVersion = 4
            case "noSenses": d.entries[0].senses = []
            case "expressionBlank": d.entries[0].expression = " \n"
            case "expressionLong": d.entries[0].expression = String(repeating: "a", count: 262145)
            case "duplicateKey": var e = entry(); e = .init(id: id(10), expression: e.expression, dictionaryForm: e.dictionaryForm, language: e.language, kind: e.kind, senses: [sense()], createdAt: now, updatedAt: now); d.entries.append(e)
            case "aliasEntry": d.entries[0].senses[0].mergedFrom = [id(1)]
            case "aliasSelf": d.entries[0].senses[0].mergedFrom = [id(2)]
            case "blockSense": d.entries[0].senses[0].content = [block(2)]
            case "blockInvalid": d.entries[0].senses[0].content = [.init(id: id(10), content: .text(.init(text: String(repeating: "a", count: 262145))))]
            case "duplicateUsageKey": d.entries[0].senses[0].examples.append(usage(4))
            case "duplicateUsageID":
                var u = usage(); u.text = "A bank opened."; u.ranges = [.init(NSRange(location: 2, length: 4))]; d.entries[0].senses[0].examples.append(u)
            case "twoStandalone": var s = sense(); s = .init(id: id(4), meaning: "", isSavedWithoutContext: true, createdAt: now, updatedAt: now); d.entries[0].senses[0].isSavedWithoutContext = true; d.entries[0].senses.append(s)
            case "usageBlank": d.entries[0].senses[0].examples[0].text = "\u{200b}"; d.entries[0].senses[0].examples[0].ranges = []
            case "usageBOM": d.entries[0].senses[0].examples[0].text = "\u{feff}"; d.entries[0].senses[0].examples[0].ranges = []
            case "usageLong": d.entries[0].senses[0].examples[0].text = String(repeating: "a", count: count); d.entries[0].senses[0].examples[0].ranges = []
            case "emptyRanges": d.entries[0].senses[0].examples[0].ranges = []
            case "overlap": d.entries[0].senses[0].examples[0].ranges = [.init(NSRange(location: 4, length: 4)), .init(NSRange(location: 4, length: 4))]
            case "partialGrapheme": d.entries[0].senses[0].examples[0].text = "cafe\u{301}"; d.entries[0].senses[0].examples[0].ranges = [.init(NSRange(location: 3, length: 1))]
            case "invalidRange": d.entries[0].senses[0].examples[0].ranges = [.init(NSRange(location: 999, length: 1))]
            case "senseRecords":
                let input = LearningExplanationPolicy.input(LearningExplanationPolicy.capture(d.entries[0], sense: d.entries[0].senses[0], example: nil))
                d.entries[0].senses[0].explanations = (0..<count).map { record(100 + $0, input) }
            case "usageRecords", "staleRecord":
                let input = LearningExplanationPolicy.input(LearningExplanationPolicy.capture(d.entries[0], sense: d.entries[0].senses[0], example: d.entries[0].examples[0]))
                d.entries[0].senses[0].examples[0].explanations = (0..<max(1,count)).map { record(100 + $0, input) }
                if mode == "staleRecord" { d.entries[0].senses[0].examples[0].text += " changed" }
            case "cardNoLayout": d.entries[0].senses[0].reviewCards = [.init(plan: .init(id: id(10)))]
            case "cardEntryID": d.entries[0].senses[0].reviewCards = [.init(plan: .init(id: id(1), layout: .init()))]
            case "legacyEntryID": d.entries[0].senses[0].review = .init(plan: .init(id: id(1)))
            case "sharedLegacyCardPlan":
                let review = VocabularyReview(plan: .init(id: id(10), layout: .init())); d.entries[0].senses[0].review = review; d.entries[0].senses[0].reviewCards = [review]
            case "templateEntryID": d.reviewTemplates = [.init(id: id(1), name: "Template", layout: .init())]
            default: fatalError("Unknown document mode")
            }
            try validator.validate(d)
        case "layout":
            var layout = ReviewLayout()
            switch mode {
            case "empty": break
            case "parts": layout.front = (0..<count).map { part(100 + $0) }
            case "duplicateParts": layout.front = [part(100)]; layout.back = [part(100)]
            case "foreignContent": layout.front = [.init(id: id(100), source: .expression, content: [block(200)])]
            case "duplicateBlocks": layout.front = [.init(id: id(100), source: .custom, content: [block(200)]), .init(id: id(101), source: .custom, content: [block(200)])]
            case "aggregateBlocks": layout.front = (0..<2).map { i in .init(id: id(100 + i), source: .custom, content: (0..<33).map { block(200 + i*33 + $0) }) }
            case "dangling": layout.front = [.init(id: id(100), source: .senseImage, blockID: id(999))]
            default: fatalError("Unknown layout mode")
            }
            try ReviewLayoutPolicy.validate(layout)
        case "templates":
            var templates = [ReviewLayoutTemplate(id: id(1), name: "Template", layout: .init())]
            switch mode {
            case "count": templates = (0..<count).map { .init(id: id(100 + $0), name: "Same name", layout: .init()) }
            case "duplicate": templates.append(templates[0])
            case "blank": templates[0].name = "\u{200b}"
            case "bom": templates[0].name = "\u{feff}"
            case "name": templates[0].name = String(repeating: "é", count: count)
            default: fatalError("Unknown template mode")
            }
            try validator.validateTemplates(templates)
        case "review":
            var review = VocabularyReview(plan: .init(id: id(10)))
            switch mode {
            case "default": break
            case "name": review.plan.name = String(repeating: "é", count: count)
            case "answer": review.plan.answer = String(repeating: "a", count: count)
            case "overrides": review.plan.fieldOverrides = Dictionary(uniqueKeysWithValues: (0..<count).map { ("field\($0)", "") })
            case "overrideKey": review.plan.fieldOverrides = [String(repeating: "a", count: count): ""]
            case "overrideValue": review.plan.fieldOverrides = ["": String(repeating: "a", count: count)]
            case "preserved": review.preservedPlans = (0..<count).map { _ in .init(id: id(10)) }
            case "bindings": review.bindings = (0..<count).map { binding(100 + $0) }
            case "duplicateBinding": review.bindings = [binding(), binding()]
            case "missingActive": review.activeBindingID = id(20)
            case "bindingValid": review.bindings = [binding()]
            case "noteZero": review.bindings = [binding(20, 0)]
            case "noteNull": review.bindings = [binding(20, nil)]
            case "endpoint": review.bindings = [binding(20, 1, "https://localhost")]
            case "identityCase": var b = binding(); b.fields["ID"] = b.originSenseID.uuidString; review.bindings = [b]
            case "receipt", "receiptEmoji", "receiptCombining", "receiptShort":
                let token = mode == "receiptEmoji" ? "😀" : mode == "receiptCombining" ? "e\u{301}" : "x"
                let digest = String(repeating: token, count: mode == "receiptShort" ? 63 : 64)
                review.plan.audioReceipts = [.expression: .init(fingerprint: digest, textHash: digest, endpoint: "http://localhost", profile: " ", filename: "lingrove-" + String(repeating: "a", count: 64) + ".wav")]
            default: fatalError("Unknown review mode")
            }
            try validator.validateReview(review)
        default: fatalError("Unknown validation operation")
        }
        return ["error": "none"]
    } catch let error as VocabularyError { return ["error": String(describing: error)] }
    catch { fatalError("Unexpected validation failure") }
}
let inputData = try Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))
let corpus = nativeInput(try JSONDecoder().decode(AnkiValue.self, from: inputData)) as! [String: Any]
let nativeRows = corpus["cases"] as! [[String: Any]]
let bridgedCorpus = try JSONSerialization.jsonObject(with: inputData) as! [String: Any]
let bridgedRows = bridgedCorpus["cases"] as! [[String: Any]]
let results = nativeRows.enumerated().map { index, row -> [String: Any] in
    var value: [String: Any] = ["id": row["id"]!, "expected": observe(row)]
    if row["operation"] as! String == "filename" {
        precondition((row["text"] as! String).utf8.elementsEqual((bridgedRows[index]["text"] as! String).utf8))
        value["bridgedExpected"] = observe(bridgedRows[index])
    }
    return value
}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-validation-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Author actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    ids = [row.get('id') for row in rows]
    if (corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not value for value in ids) or len(set(ids)) != len(ids)):
        parser.error('Validation cases require pinned version and unique IDs')
    allowed = {'document': 'Cotabby/Services/Vocabulary/VocabularyArchive.swift', 'review': 'Cotabby/Services/Vocabulary/VocabularyArchive.swift',
        'templates': 'Cotabby/Services/Vocabulary/VocabularyArchive+ReviewCards.swift', 'layout': 'Cotabby/Support/Review/ReviewLayoutPolicy.swift',
        'endpoint': 'Cotabby/Support/Review/AnkiReviewPolicy.swift', 'filename': 'Cotabby/Support/Speech/SpeechExportPolicy.swift'}
    if any(row.get('provenanceKind') != 'source-policy-extension' or row.get('macSource') != allowed.get(row.get('operation')) for row in rows):
        parser.error('Validation probes require exact source provenance')
    with tempfile.TemporaryDirectory(prefix='lingrove-validation-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(archive_validation_sources(args.mac_repo) + [source(args.mac_repo, "Cotabby/Models/Review/AnkiWireModels.swift")]):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'validation-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac archive validation observations'); return
    if len(observations) != len(rows): raise SystemExit('Validation observation count changed')
    for row, observed in zip(rows, observations):
        expected = {'id': row['id'], 'expected': row['expected']}
        if row['operation'] == 'filename': expected['bridgedExpected'] = row['bridgedExpected']
        if observed != expected: raise SystemExit(f"Mac validation mismatch: {row['id']}")
    print(f'Verified {len(observations)} archive validation cases against actual pinned Mac policies')


if __name__ == '__main__':
    main()
