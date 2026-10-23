#!/usr/bin/env python3
"""Execute complete pinned notebook/review values and their pure projections/merge, without IO."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

BASELINE = "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f"
DRIVER = r'''
import Foundation

func id(_ value: Int) -> UUID { UUID(uuidString: String(format: "00000000-0000-0000-0000-%012x", value))! }
func string(_ value: UUID) -> String { value.uuidString.lowercased() }
let now = Date(timeIntervalSince1970: 0)
func block(_ value: Int) -> VocabularyContentBlock { .init(id: id(value), content: .text(.init(text: "literal"))) }
func part(_ value: Int, _ source: ReviewContentSource, content: [VocabularyContentBlock] = []) -> ReviewCardPart {
    .init(id: id(value), source: source, content: content)
}
func layout(_ source: ReviewContentSource) -> ReviewLayout { .init(front: [part(900, source)]) }
func plan(_ value: Int, _ source: ReviewContentSource? = nil) -> ReviewPlan {
    .init(id: id(value), layout: source.map(layout))
}
func binding(_ value: Int, origin: Int, note: Int64?, endpoint: String = "loopback", profile: String = "profile") -> AnkiNoteBinding {
    .init(id: id(value), originSenseID: id(origin), endpoint: endpoint, profile: profile, noteID: note,
        model: "model", deck: "deck", identityField: "Identity", mapping: [.identity: "Identity"], fields: ["Identity": "retained"])
}
func sense(_ value: Int) -> VocabularySense { .init(id: id(value), meaning: "meaning", createdAt: now, updatedAt: now) }
func record(_ value: Int, _ meaning: String) -> LearningExplanationRecord {
    .init(id: id(value), input: .init(target: "bank", dictionaryForm: "bank", language: .english, kind: .word,
        context: "bank", ranges: [.init(NSRange(location: 0, length: 4))]), language: .chinese,
        result: .init(meaning: meaning, explanation: "Explanation"), model: "fixture", createdAt: now)
}
func usage(_ value: Int, _ records: [LearningExplanationRecord]) -> VocabularyExample {
    .init(id: id(value), text: "The bank.", translation: "", ranges: [.init(NSRange(location: 4, length: 4))],
        completeness: .sentence, meaning: "", createdAt: now, explanations: records)
}
func observe(_ row: [String: Any]) -> [String: Any] {
    switch row["operation"] as! String {
    case "defaults":
        let document = VocabularyDocument(), sense = sense(1), plan = plan(2), part = part(3, .custom)
        let snapshot = VocabularySnapshot(revision: UInt64.max, document: document)
        return ["version": document.schemaVersion, "entries": document.entries.count, "templatesAbsent": document.reviewTemplates == nil,
            "senseOrigin": sense.meaningOrigin.rawValue, "note": sense.note, "tags": sense.tags.count, "examples": sense.examples.count,
            "standalone": sense.isSavedWithoutContext, "standaloneMeanings": sense.standaloneMeanings.count,
            "alternateMeanings": sense.alternateMeanings.count, "aliases": sense.mergedFrom.count, "explanations": sense.explanations.count,
            "reviewAbsent": sense.review == nil, "cardsAbsent": sense.reviewCards == nil, "contentAbsent": sense.content == nil,
            "recognition": plan.recognition, "gap": plan.gap, "includeNotes": plan.includeNotes, "includeExplanation": plan.includeExplanation,
            "includeContentAbsent": plan.includeSenseContent == nil, "layoutAbsent": plan.layout == nil, "answerAbsent": plan.answer == nil,
            "audioKindsAbsent": plan.audioKinds == nil, "audioReceiptsAbsent": plan.audioReceipts == nil,
            "overrides": plan.fieldOverrides.count, "partBlockAbsent": part.blockID == nil, "caption": part.showCaption,
            "partContent": part.content.count, "revision": String(snapshot.revision)]
    case "optional":
        let field = row["field"] as! String
        var a = sense(1), b = a
        var x = plan(2), y = x
        var d = VocabularyDocument(), e = d
        switch field {
        case "content": b.content = []
        case "reviewCards": b.reviewCards = []
        case "reviewTemplates": e.reviewTemplates = []
        case "answer": y.answer = ""
        case "name": y.name = ""
        case "deck": y.deck = ""
        case "layout": y.layout = ReviewLayout()
        case "includeSenseContent": y.includeSenseContent = false
        case "audioKinds": y.audioKinds = []
        case "audioReceipts": y.audioReceipts = [:]
        default: fatalError("Unknown optional probe")
        }
        return ["equal": a == b && x == y && d == e]
    case "collections":
        var a = plan(1), b = a
        a.fieldOverrides = ["cafe\u{301}": "cafe\u{301}", "other": "literal"]
        b.fieldOverrides = ["other": "literal", "café": "café"]
        a.audioKinds = [.expression, .sentence]; b.audioKinds = [.sentence, .expression]
        let front = [part(10, .expression), part(11, .meaning)]
        var c = sense(2), d = c; c.tags = ["cafe\u{301}"]; d.tags = ["café"]
        return ["planEqual": a == b, "orderedLayoutEqual": ReviewLayout(front: front) == ReviewLayout(front: front.reversed()),
            "canonicalTemplateEqual": ReviewLayoutTemplate(id: id(3), name: "cafe\u{301}", layout: .init()) ==
                ReviewLayoutTemplate(id: id(3), name: "café", layout: .init()), "canonicalTagsEqual": c == d]
    case "entry":
        let first = record(40, "first wins"), changed = record(40, "later duplicate")
        var a = sense(10), b = sense(11)
        a.explanations = [first]; a.examples = [usage(60, [changed, record(41, "second")])]
        b.explanations = [record(42, "third")]; b.examples = [usage(61, [first])]
        let entry = VocabularyEntry(id: id(1), expression: "bank", dictionaryForm: "bank", language: .english,
            kind: .word, senses: [a, b, a], createdAt: now, updatedAt: now)
        return ["usages": entry.examples.map { string($0.id) }, "records": (entry.explanations ?? []).map { string($0.id) },
            "meanings": (entry.explanations ?? []).map { $0.result.meaning }, "nestedCount": entry.senses[0].examples[0].explanations.count,
            "emptyRecordsAbsent": VocabularyEntry(id: id(2), expression: "bank", dictionaryForm: "bank", language: .english,
                kind: .word, senses: [], createdAt: now, updatedAt: now).explanations == nil]
    case "slots":
        var value = sense(1)
        value.review = .init(plan: plan(10, .expression), preservedPlans: [plan(11, .notes)])
        value.reviewCards = [.init(plan: plan(20, .meaning), preservedPlans: [plan(21, .translation)])]
        let original = value
        let slot = row["slot"] as! String
        let replacement = VocabularyReview(plan: plan(slot == "replace" ? 20 : 30, .custom))
        value.setReview(replacement, cardID: slot == "legacy" ? nil : id(slot == "replace" ? 20 : 30))
        return ["legacy": value.review.map { string($0.plan.id) } as Any? ?? NSNull(),
            "cards": (value.reviewCards ?? []).map { string($0.plan.id) },
            "layouts": value.reviewLayouts.map { $0.parts[0].source.rawValue },
            "selected": value.reviewValue(slot == "legacy" ? nil : replacement.plan.id).map { string($0.plan.id) } as Any? ?? NSNull(),
            "originalCardCount": original.reviewCards?.count ?? 0]
    case "layout":
        let value = ReviewLayout(front: [part(10, .expressionAudio, content: [block(100)]), part(11, .sentenceAudio)],
            back: [part(12, .expressionAudio, content: [block(101)])])
        return ["parts": value.parts.map { string($0.id) }, "content": value.customContent.map { string($0.id) },
            "audio": value.audioKinds.map(\.rawValue).sorted(), "fields": value.audioKinds.map { $0.field.rawValue }.sorted()]
    case "merge":
        let mode = row["mode"] as! String
        let nilNote = mode.hasPrefix("nullNote")
        let a = binding(20, origin: 200, note: nilNote ? nil : 1,
            endpoint: mode == "canonicalScope" ? "loopback-cafe\u{301}" : "loopback", profile: mode == "canonicalScope" ? "cafe\u{301}" : "profile")
        let sameOrigin = mode == "sameOrigin" || mode == "nullNoteSameOrigin"
        let b = binding(30, origin: sameOrigin ? 200 : 300,
            note: nilNote ? nil : mode == "sameOrigin" || mode == "independent" || (mode == "duplicateSource" || mode == "duplicateSourceIdentity") ? 2 : 1,
            endpoint: mode == "otherEndpoint" ? "other" : mode == "canonicalScope" ? "loopback-café" : "loopback",
            profile: mode == "otherProfile" ? "other" : mode == "canonicalScope" ? "café" : "profile")
        let target: VocabularyReview? = mode == "targetNil" || mode == "bothNil" ? nil : .init(plan: plan(1000),
            bindings: mode == "noBindings" || mode == "duplicateSource" || mode == "duplicateSourceIdentity" ? [] : [a], activeBindingID: a.id, preservedPlans: [plan(1001)])
        let source: VocabularyReview? = mode == "sourceNil" || mode == "bothNil" ? nil : .init(plan: plan(2000),
            bindings: mode == "noBindings" ? [] : mode == "duplicateSource" ? [b, b] : mode == "duplicateSourceIdentity" ? [b, binding(31, origin: 301, note: 2)] : [b], activeBindingID: b.id, preservedPlans: [plan(2001)])
        guard let merged = AnkiReviewPolicy.merged(target, source) else { return ["absent": true] }
        return ["absent": false, "plan": string(merged.plan.id), "bindings": merged.bindings.map { string($0.id) },
            "origins": merged.bindings.map { string($0.originSenseID) }, "preserved": merged.preservedPlans.map { string($0.id) },
            "active": merged.activeBindingID.map(string) as Any? ?? NSNull(), "needsChoice": merged.needsBindingChoice]
    default: fatalError("Unknown model probe")
    }
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let results = (corpus["cases"] as! [[String: Any]]).map { row in ["id": row["id"]!, "expected": observe(row)] }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
'''


def source(repo, path):
    return subprocess.check_output(["git", "-C", str(repo), "show", f"{BASELINE}:{path}"], text=True)


def before(value, marker):
    if value.count(marker) != 1: raise RuntimeError("Pinned declaration marker must be unique")
    return value[:value.index(marker)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, default=Path(__file__).resolve().parents[1] / "fixtures/parity/vocabulary-models-v1.json")
    parser.add_argument("--capture-output", type=Path, help="Author actual observations into a NEW file")
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding="utf-8")); rows = corpus["cases"]
    ids = [row.get("id") for row in rows]
    if (corpus.get("version") != 1 or corpus.get("macReference") != BASELINE or not rows
            or any(not value for value in ids) or len(set(ids)) != len(ids)):
        parser.error("Model cases require pinned version and unique IDs")
    for row in rows:
        if (row.get("provenanceKind") != "source-policy-extension" or row.get("macSource") not in (
                "Cotabby/Models/Vocabulary/VocabularyModels.swift", "Cotabby/Models/Review/ReviewModels.swift",
                "Cotabby/Models/Review/ReviewLayout.swift", "Cotabby/Support/Review/AnkiReviewPolicy.swift")):
            parser.error("Model probes require explicit source provenance")
    paths = ["Cotabby/Models/Context/LanguageCatalog.swift", "Cotabby/Models/LanguageLearning/LearningLanguage.swift",
        "Cotabby/Models/Vocabulary/VocabularyModels.swift", "Cotabby/Models/Vocabulary/VocabularyContentModels.swift",
        "Cotabby/Models/Review/ReviewModels.swift", "Cotabby/Models/Review/ReviewLayout.swift"]
    values = [source(args.mac_repo, path) for path in paths]
    explanation = source(args.mac_repo, "Cotabby/Models/LanguageLearning/LearningExplanationModels.swift")
    values.append("import Foundation\n" + before(explanation[explanation.index("nonisolated enum LearningExplanationContext:"):], "/// UI sessions borrow this app-owned boundary."))
    values.append(before(source(args.mac_repo, "Cotabby/Models/Review/ReviewAudioModels.swift"), "/// A preparation sheet retains only its explicitly generated audio."))
    merge = source(args.mac_repo, "Cotabby/Support/Review/AnkiReviewPolicy.swift")
    values.append("import Foundation\nnonisolated enum AnkiReviewPolicy {\n" + merge[merge.index("    static func merged("):merge.rindex("\n}")] + "\n}\n")
    # Complete notebook/review/layout/content declarations, original audio values (excluding its
    # SpeechExportAudio draft), pure explanation values and the exact original merged function.
    with tempfile.TemporaryDirectory(prefix="lingrove-model-graph-") as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(values):
            path = root / f"source{index}.swift"; path.write_text(value, encoding="utf-8"); inputs.append(str(path))
        driver = root / "main.swift"; driver.write_text(DRIVER, encoding="utf-8")
        binary = root / "models-parity"
        subprocess.run(["swiftc", "-swift-version", "6", "-O", *inputs, str(driver), "-o", str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open("x", encoding="utf-8") as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write("\n")
        print(f"Captured {len(observations)} actual Mac notebook/review model observations")
        return
    if len(observations) != len(rows): raise SystemExit("Model observation count changed")
    for row, observed in zip(rows, observations):
        if observed != {"id": row["id"], "expected": row["expected"]}: raise SystemExit(f"Mac model mismatch: {row['id']}")
    print(f"Verified {len(observations)} notebook/review model cases against actual pinned Mac values/merge")


if __name__ == "__main__":
    main()
