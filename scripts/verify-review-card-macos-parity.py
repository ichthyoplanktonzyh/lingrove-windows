#!/usr/bin/env python3
"""Execute original review content/layout/template/JSON values; no UI, media or Anki server."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

from macos_vocabulary_reference import BASELINE, sense_policy_sources, source

SOURCES = (
    "Cotabby/Support/Review/ReviewCardPolicy.swift",
    "Cotabby/Support/Review/ReviewLayoutPolicy.swift",
    "Cotabby/Support/Review/AnkiNoteTemplate.swift",
    "Cotabby/Models/Review/AnkiWireModels.swift",
)
TESTS = "CotabbyTests/Support/Review/ReviewCardPolicyTests.swift"
DRIVER = r'''
import Foundation
// Keep native Swift strings at the driver boundary. JSONSerialization's NSString bridge can
// consume a leading BOM, which would change the input before the original policy ever sees it.
func inputValue(_ value: AnkiValue) -> Any {
    switch value {
    case let .object(map): return map.mapValues(inputValue)
    case let .array(values): return values.map(inputValue)
    case let .string(value): return value
    case let .number(value): return value >= Double(Int.min) && value < Double(Int.max) && value.rounded(.towardZero) == value ? Int(value) as Any : value
    case let .bool(value): return value
    case .null: return NSNull()
    }
}
func outputValue(_ value: Any) -> AnkiValue {
    let mirror = Mirror(reflecting: value)
    if mirror.displayStyle == .optional { return mirror.children.first.map { outputValue($0.value) } ?? .null }
    switch value {
    case is NSNull: return .null
    case let value as String: return .string(value)
    case let value as Bool where type(of: value) == Bool.self: return .bool(value)
    case let value as Int: return .number(Double(value))
    case let value as Int64: return .number(Double(value))
    case let value as Double: return .number(value)
    case let value as [String: Any]: return .object(value.mapValues(outputValue))
    case let value as [Any]: return .array(value.map(outputValue))
    default: fatalError("Unexpected observation projection type: \(type(of: value))")
    }
}
func id(_ n: Int) -> UUID { UUID(uuidString: String(format: "00000000-0000-0000-0000-%012x", n))! }
func uuid(_ value: UUID?) -> Any { value.map { $0.uuidString.lowercased() } as Any? ?? NSNull() }
func text(_ row: [String: Any], _ key: String, _ fallback: String) -> String { row[key] as? String ?? fallback }
func flag(_ row: [String: Any], _ key: String, _ fallback: Bool) -> Bool { row[key] as? Bool ?? fallback }
func ranges(_ row: [String: Any]) -> [VocabularyTextRange] {
    ((row["ranges"] as? [[Int]]) ?? [[4, 4]]).map { .init(NSRange(location: $0[0], length: $0[1])) }
}
let now = Date(timeIntervalSince1970: 0)
func blocks(_ mode: String, start: Int = 201) -> [VocabularyContentBlock] {
    let plain = VocabularyContentBlock(id: id(start), content: .text(.init(text: "Authored <hint>&", style: .heading, bold: true, italic: true)))
    let image = VocabularyContentBlock(id: id(start + 1), content: .image(.init(digest: String(repeating: "a", count: 64), width: 20, height: 30, byteCount: 50), caption: "Answer <bank>&"))
    switch mode {
    case "none": return []
    case "text": return [plain]
    case "image": return [image]
    case "whitespace": return [.init(id: id(start), content: .text(.init(text: " \u{200b}\n")))]
    case "duplicate": return [plain, plain]
    case "invalid": return [.init(id: id(start), content: .image(.init(digest: "../unsafe", width: 20, height: 30, byteCount: 50), caption: ""))]
    default: return [plain, image]
    }
}
func record(_ n: Int, _ prose: String) -> LearningExplanationRecord {
    .init(id: id(n), input: .init(target: "bank", dictionaryForm: "bank", language: .english, kind: .word, context: "The bank.", ranges: [.init(NSRange(location: 4, length: 4))]),
        language: .chinese, result: .init(meaning: "meaning", explanation: prose), model: "fixture", createdAt: now)
}
func records(_ row: [String: Any], _ key: String, _ fallback: String, n: Int) -> [LearningExplanationRecord] {
    if row[key] is NSNull { return [] }
    return [record(n, "previous"), record(n + 1, text(row, key, fallback))]
}
func part(_ row: [String: Any], _ n: Int) -> ReviewCardPart {
    .init(id: id(row["id"] as? Int ?? n), source: ReviewContentSource(rawValue: row["source"] as! String)!,
        blockID: (row["block"] as? Int).map(id), showCaption: flag(row, "caption", false),
        content: row["content"] is String ? blocks(row["content"] as! String, start: 1000 + n * 2) : [])
}
func layout(_ row: [String: Any]) -> ReviewLayout {
    let front = row["front"] as? [[String: Any]] ?? [["source": "expression"]]
    let back = row["back"] as? [[String: Any]] ?? [["source": "meaning"]]
    return .init(front: front.enumerated().map { part($0.element, 100 + $0.offset) }, back: back.enumerated().map { part($0.element, 200 + $0.offset) })
}
func seed(_ row: [String: Any]) -> (VocabularyEntry, VocabularySense, ReviewPlan) {
    let example = VocabularyExample(id: id(10), text: text(row, "sentence", "The bank."), translation: text(row, "translation", "translation <&>"), ranges: ranges(row),
        completeness: .sentence, meaning: text(row, "usageMeaning", " contextual answer "), createdAt: now,
        explanations: records(row, "usageExplanation", "USAGE last", n: 60))
    var sense = VocabularySense(id: id(2), meaning: text(row, "senseMeaning", " core & <meaning> "),
        meaningOrigin: VocabularyMeaningOrigin(rawValue: text(row, "origin", "captured"))!, note: text(row, "note", "note\n<&>"),
        examples: flag(row, "usagePresent", true) ? [example] : [],
        explanations: records(row, "senseExplanation", "SENSE last", n: 50),
        content: blocks(text(row, "senseBlocks", "both")), createdAt: now, updatedAt: now)
    if flag(row, "existingReview", false) { sense.review = .init(plan: .init(id: id(77), answer: "saved", recognition: false, gap: true)) }
    let entry = VocabularyEntry(id: id(1), expression: text(row, "expression", "bank <&>"), dictionaryForm: "bank", language: .english,
        kind: VocabularyEntry.Kind(rawValue: text(row, "kind", "word"))!, senses: [sense], createdAt: now, updatedAt: now)
    var plan = ReviewPlan(id: id(80), exampleID: text(row, "example", "first") == "none" ? nil : id(text(row, "example", "first") == "stale" ? 11 : 10),
        answer: row["answer"] as? String, recognition: flag(row, "recognition", true), gap: flag(row, "gap", false),
        includeNotes: flag(row, "includeNotes", false), includeExplanation: flag(row, "includeExplanation", false), includeSenseContent: row["includeContent"] as? Bool)
    if flag(row, "flexible", false) { plan.layout = layout(row) }
    return (entry, sense, plan)
}
func blockValue(_ block: VocabularyContentBlock) -> [String: Any] {
    switch block.content {
    case let .text(value): return ["id": uuid(block.id), "kind": "text", "text": value.text, "style": value.style.rawValue, "bold": value.bold, "italic": value.italic]
    case let .image(image, caption): return ["id": uuid(block.id), "kind": "image", "filename": image.filename, "caption": caption]
    }
}
func parts(_ values: [ReviewResolvedPart]?) -> Any {
    guard let values else { return NSNull() }
    return values.map { part -> [String: Any] in
        switch part {
        case let .text(text): return ["kind": "text", "value": text]
        case let .blocks(blocks): return ["kind": "blocks", "value": blocks.map(blockValue)]
        case let .audio(kind): return ["kind": "audio", "value": kind.rawValue]
        }
    }
}
func blockPayload(_ block: VocabularyContentBlock) -> [String: Any] {
    var value = blockValue(block); value.removeValue(forKey: "id")
    if case let .image(image, _) = block.content { value["digest"] = image.digest; value["width"] = image.width; value["height"] = image.height; value["byteCount"] = image.byteCount }
    return value
}
func contentValue(_ c: ReviewCardContent) -> [String: Any] {
    ["expression": c.expression, "answer": c.answer, "sentence": c.sentence, "ranges": c.ranges.map { [$0.location, $0.length] },
     "translation": c.translation, "explanation": c.explanation, "notes": c.notes, "gapPrompt": c.gapPrompt, "gapAnswer": c.gapAnswer,
     "recognition": c.recognition, "gap": c.gap, "blocks": c.senseContent.map(blockValue), "front": parts(c.front), "back": parts(c.back)]
}
func json(_ value: some Encodable) throws -> Any {
    inputValue(try JSONDecoder().decode(AnkiValue.self, from: JSONEncoder().encode(value)))
}
func wire(_ row: [String: Any]) -> [String: Any] {
    let value: AnkiValue
    if let numeric = row["numeric"] as? String {
        value = .number(numeric == "nan" ? .nan : numeric == "infinity" ? .infinity : -.infinity)
    } else {
        guard let decoded = try? JSONDecoder().decode(AnkiValue.self, from: Data((row["json"] as! String).utf8)) else { return ["decoded": false] }
        value = decoded
    }
    let kind: String
    switch value { case .object: kind = "object"; case .array: kind = "array"; case .string: kind = "string"; case .number: kind = "number"; case .bool: kind = "bool"; case .null: kind = "null" }
    let data = try? JSONEncoder().encode(value)
    let projected = data.flatMap { try? JSONDecoder().decode(AnkiValue.self, from: $0) }.map(inputValue)
    return ["decoded": true, "kind": kind, "integer": value.integer.map(String.init) as Any? ?? NSNull(), "encoded": data != nil,
        "value": flag(row, "omitValue", false) ? NSNull() : projected ?? NSNull(), "objectCount": value.object?.count as Any? ?? NSNull(), "arrayCount": value.array?.count as Any? ?? NSNull()]
}
func observe(_ row: [String: Any]) throws -> [String: Any] {
    let operation = row["operation"] as! String
    if operation == "wire" { return wire(row) }
    if operation == "wireEqual" {
        let a = try JSONDecoder().decode(AnkiValue.self, from: Data((row["a"] as! String).utf8))
        let b = try JSONDecoder().decode(AnkiValue.self, from: Data((row["b"] as! String).utf8))
        return ["equal": a == b]
    }
    if operation == "gap" {
        let gap = ReviewCardPolicy.gap(in: row["sentence"] as! String, ranges: ranges(row), kind: VocabularyEntry.Kind(rawValue: text(row, "kind", "word"))!)
        return ["prompt": gap?.prompt as Any? ?? NSNull(), "answer": gap?.answer as Any? ?? NSNull()]
    }
    if operation == "template" {
        return ["html": AnkiNoteTemplate.preview(fields: row["fields"] as? [String: String] ?? [:], gap: flag(row, "gap", false), back: flag(row, "back", false),
            css: text(row, "css", AnkiNoteTemplate.css), legacy: flag(row, "legacy", false))]
    }
    if operation == "constants" {
        return ["css": AnkiNoteTemplate.css, "legacyCSS": AnkiNoteTemplate.legacyCSS, "templates": try json(AnkiNoteTemplate.templates), "legacyTemplates": try json(AnkiNoteTemplate.legacyTemplates),
            "flexibleCSS": ReviewLayoutPolicy.css, "flexibleTemplates": try json(ReviewLayoutPolicy.templates), "model": ReviewLayoutPolicy.model,
            "mapping": Dictionary(uniqueKeysWithValues: ReviewLayoutPolicy.mapping.map { ($0.key.rawValue, $0.value) })]
    }
    let (entry, sense, plan) = seed(row)
    if operation == "initial" {
        let a = ReviewCardPolicy.initialPlan(sense), b = ReviewCardPolicy.initialPlan(sense)
        return ["saved": a.id == id(77), "sameAcrossCalls": a.id == b.id, "example": uuid(a.exampleID), "includeContent": a.includeSenseContent as Any? ?? NSNull(),
            "answer": a.answer as Any? ?? NSNull(), "recognition": a.recognition, "gap": a.gap]
    }
    if operation == "preset" { let value = ReviewLayoutPolicy.preset(row["index"] as! Int)
        return ["front": value.front.map { $0.source.rawValue }, "back": value.back.map { $0.source.rawValue }, "unique": Set(value.parts.map(\.id)).count == value.parts.count] }
    if operation == "portable" || operation == "applying" {
        let original = layout(row), result = operation == "portable" ? ReviewLayoutPolicy.portable(original) : ReviewLayoutPolicy.applying(original, to: sense)
        return ["front": result.front.map { $0.source.rawValue }, "back": result.back.map { $0.source.rawValue },
            "newParts": zip(original.parts, result.parts).allSatisfy { $0.id != $1.id }, "unique": Set(result.parts.map(\.id)).count == result.parts.count,
            "newBlocks": zip(original.customContent, result.customContent).allSatisfy { $0.id != $1.id },
            "references": result.parts.map { uuid($0.blockID) }, "captions": result.customContent.compactMap { if case let .image(_, caption) = $0.content { caption } else { nil } },
            "partCount": result.parts.count, "blockCount": result.customContent.count, "payloads": result.customContent.map(blockPayload)]
    }
    let content: ReviewCardContent
    if operation == "sentence" { content = .init(expression: entry.expression, answer: "", sentence: entry.senses[0].examples.first?.text ?? text(row, "sentence", ""), ranges: ranges(row),
        translation: "", explanation: "", notes: "", gapPrompt: "", gapAnswer: "", recognition: true, gap: false) }
    else if operation == "flexible" { content = try ReviewLayoutPolicy.content(entry: entry, sense: sense, plan: plan, previewBack: row["previewBack"] as? Bool) }
    else { content = try ReviewCardPolicy.content(entry: entry, sense: sense, plan: plan) }
    if operation == "sentence" { return ["html": ReviewCardPolicy.sentenceHTML(content)] }
    let audio: [ReviewField: String] = [.expressionAudio: "[sound:expression.wav]", .sentenceAudio: "[sound:sentence.wav]"]
    return ["valid": true, "content": contentValue(content), "sentenceHTML": ReviewCardPolicy.sentenceHTML(content),
        "values": Dictionary(uniqueKeysWithValues: ReviewCardPolicy.values(content, identity: id(2)).map { ($0.key.rawValue, $0.value) }),
        "frontHTML": content.front.map { ReviewLayoutPolicy.html($0, audio: audio) } as Any? ?? NSNull(),
        "backHTML": content.back.map { ReviewLayoutPolicy.html($0, audio: audio) } as Any? ?? NSNull(),
        "frontPreview": content.front.map(ReviewLayoutPolicy.preview) as Any? ?? NSNull(), "backPreview": content.back.map(ReviewLayoutPolicy.preview) as Any? ?? NSNull()]
}
let corpus = inputValue(try JSONDecoder().decode(AnkiValue.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))) as! [String: Any]
let results = (corpus["cases"] as! [[String: Any]]).map { row -> [String: Any] in
    let result: [String: Any]
    do { result = try observe(row) } catch { result = ["valid": false, "error": error is VocabularyError ? String(describing: error) : error.localizedDescription] }
    return ["id": row["id"]!, "expected": result]
}
let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]
FileHandle.standardOutput.write(try encoder.encode(results.map(outputValue)))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, default=Path(__file__).resolve().parents[1] / "fixtures/parity/review-card-v1.json")
    parser.add_argument("--capture-output", type=Path, help="Author observations into a NEW file only")
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding="utf-8")); rows = corpus["cases"]
    ids = [row.get("id") for row in rows]
    if corpus.get("version") != 1 or corpus.get("macReference") != BASELINE or not rows or any(not item for item in ids) or len(ids) != len(set(ids)):
        parser.error("Review cases need pinned version and unique IDs")
    tests = source(args.mac_repo, TESTS)
    for row in rows:
        if row.get("macSource") not in SOURCES or row.get("provenanceKind") not in ("source-policy-extension", "regression-adaptation"):
            parser.error("Explicit review source provenance required")
        if row["provenanceKind"] == "regression-adaptation" and (row.get("macTest") != TESTS or "func " + row.get("macTestMethod", "?") + "(" not in tests):
            parser.error("Review regression provenance must name an existing original test")
    values = sense_policy_sources(args.mac_repo)
    if "nonisolated enum ReviewCardPolicy" not in values[-1]: raise RuntimeError("Original escaping dependency boundary changed")
    # Replace only the earlier escaping-only declaration with all complete original review files.
    values = values[:-1] + [source(args.mac_repo, path) for path in SOURCES]
    with tempfile.TemporaryDirectory(prefix="lingrove-review-card-") as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(values):
            path = root / f"source{index}.swift"; path.write_text(value, encoding="utf-8"); inputs.append(str(path))
        driver = root / "main.swift"; driver.write_text(DRIVER, encoding="utf-8"); binary = root / "review-card"
        subprocess.run(["swiftc", "-swift-version", "6", "-O", *inputs, str(driver), "-o", str(binary)], check=True)
        observed = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open("x", encoding="utf-8") as output: json.dump(observed, output, ensure_ascii=False, indent=2); output.write("\n")
        print(f"Captured {len(observed)} actual Mac review content/layout/template/JSON observations"); return
    if len(observed) != len(rows): raise SystemExit("Review observation count changed")
    for row, result in zip(rows, observed):
        if result != {"id": row["id"], "expected": row["expected"]}: raise SystemExit(f"Mac review mismatch: {row['id']}")
    print(f"Verified {len(observed)} review content/layout/template/JSON cases against complete original Mac policies")


if __name__ == "__main__": main()
