#!/usr/bin/env python3
"""Run actual pinned Mac writing-capture -> explanation-input composition.

This corpus covers the current Windows manual-writing preview only: readingSource=nil,
targetIsSelection=false, unit(original:false). It does not pretend to port reading/notebook UI.
Only pure declaration slices omit unrelated app/storage types; every exercised algorithm is
original. Token facts come from real NLTokenizer and are test inputs, never production fakes.
"""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

BASELINE = "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f"
SOURCES = [
    "Cotabby/Models/Context/LanguageCatalog.swift",
    "Cotabby/Models/LanguageLearning/LearningLanguage.swift",
    "Cotabby/Support/LanguageLearning/LearningUnitPolicy.swift",
    "Cotabby/Support/Vocabulary/VocabularyCaptureFactory.swift",
    "Cotabby/Support/Prompting/ModelContextBudget.swift",
    "Cotabby/Support/Prompting/TokenCountEstimator.swift",
    "Cotabby/Support/LanguageLearning/LearningFailure.swift",
]

DRIVER = r'''
import Foundation
import NaturalLanguage

// Native Swift Codable strings retain a leading U+FEFF, unlike NSString JSON bridges.
indirect enum Value: Codable {
    case string(String), integer(Int), boolean(Bool), array([Value]), object([String: Value]), null
    init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if c.decodeNil() { self = .null }
        else if let v = try? c.decode(String.self) { self = .string(v) }
        else if let v = try? c.decode(Int.self) { self = .integer(v) }
        else if let v = try? c.decode(Bool.self) { self = .boolean(v) }
        else if let v = try? c.decode([Value].self) { self = .array(v) }
        else { self = .object(try c.decode([String: Value].self)) }
    }
    func encode(to encoder: Encoder) throws {
        var c = encoder.singleValueContainer()
        switch self {
        case let .string(v): try c.encode(v)
        case let .integer(v): try c.encode(v)
        case let .boolean(v): try c.encode(v)
        case let .array(v): try c.encode(v)
        case let .object(v): try c.encode(v)
        case .null: try c.encodeNil()
        }
    }
    subscript(key: String) -> Value { guard case let .object(v) = self else { fatalError("Expected object") }; return v[key] ?? .null }
    var string: String { guard case let .string(v) = self else { fatalError("Expected string") }; return v }
    var int: Int { guard case let .integer(v) = self else { fatalError("Expected integer") }; return v }
    var values: [Value] { guard case let .array(v) = self else { fatalError("Expected array") }; return v }
}
func range(_ value: Value) -> NSRange { NSRange(location: value["location"].int, length: value["length"].int) }
func rangeJSON(_ value: NSRange) -> Value { .object(["location": .integer(value.location), "length": .integer(value.length)]) }
func word(_ value: Value) -> LearningDictionaryWord { .init(text: value["text"].string, range: range(value["range"])) }
func unit(_ value: Value) -> LearningUnit {
    .init(id: value["id"].int, surface: value["surface"].string, query: value["query"].string,
        kind: LearningUnit.Kind(rawValue: value["kind"].string)!, ranges: value["ranges"].values.map(range), words: value["words"].values.map(word))
}
let corpus = try JSONDecoder().decode(Value.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
var observations = [Value]()
for row in corpus["cases"].values {
    let value = row["presentation"], status = value["status"].string
    var presentation = LearningSentencePresentation(id: UUID(), source: value["source"].string,
        translation: value["translation"].string,
        sourceLanguage: LearningLanguage(rawValue: value["sourceLanguage"].string)!,
        targetLanguage: LearningLanguage(rawValue: value["targetLanguage"].string)!,
        caret: .zero, isExpanded: false, isGenerating: false, message: "", previousTranslation: nil, shortcutLabel: "")
    // Fixture lifecycle projection only: Windows Idle/Cancelled have no current presentation;
    // Loading is translating, Failed is error. The actual Mac factory performs the rejection.
    presentation.isStale = status == "Cancelled" || status == "Idle"
    presentation.isError = status == "Failed"
    presentation.isTranslating = status == "Loading"
    let capture: VocabularyCapture?
    switch row["mode"].string {
    case "preview": capture = VocabularyCaptureFactory.preview(presentation)
    case "unit": capture = VocabularyCaptureFactory.unit(unit(row["unit"]), in: presentation)
    case "component":
        let target = unit(row["unit"])
        capture = VocabularyCaptureFactory.unit(target, in: presentation).flatMap {
            VocabularyCaptureFactory.component(word(row["clicked"]), unit: target, capture: $0)
        }
    default: fatalError("Unknown capture fixture mode")
    }
    var projected: Value = .null
    if let capture {
        let input = LearningExplanationPolicy.input(capture)
        projected = .object(["value": try JSONDecoder().decode(Value.self, from: JSONEncoder().encode(input)),
            "valid": .boolean(LearningExplanationPolicy.isValid(input))])
    }
    let text = presentation.translation, language = presentation.targetLanguage
    let words = LearningUnitPolicy.words(in: text, language: language)
    let tokenizer = NLTokenizer(unit: .sentence)
    tokenizer.string = text; tokenizer.setLanguage(language.naturalLanguage)
    var sentences = [Value]()
    tokenizer.enumerateTokens(in: text.startIndex..<text.endIndex) { span, _ in sentences.append(rangeJSON(NSRange(span, in: text))); return true }
    let facts: Value = .object(["text": .string(text), "language": .string(language.rawValue),
        "words": .array(words.map { .object(["text": .string($0.text), "range": rangeJSON($0.range)]) }),
        "sentences": .array(sentences)])
    observations.append(.object(["id": row["id"], "expected": projected, "facts": facts]))
}
let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]
FileHandle.standardOutput.write(try encoder.encode(observations))
'''


def source(repo, path):
    return subprocess.check_output(["git", "-C", str(repo), "show", f"{BASELINE}:{path}"], text=True)


def between(value, start, end):
    if value.count(start) != 1 or value.count(end) != 1:
        raise RuntimeError("Pinned declaration boundaries must be unique")
    return value[value.index(start):value.index(end)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, default=Path(__file__).resolve().parents[1] / "fixtures/parity/learning-explanation-capture-v1.json")
    parser.add_argument("--capture-output", type=Path, help="Write actual reference observations to a NEW authoring file")
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding="utf-8"))
    ids = [row.get("id") for row in corpus["cases"]]
    if corpus.get("version") != 1 or corpus.get("macReference") != BASELINE or any(not value for value in ids) or len(set(ids)) != len(ids):
        parser.error("Capture cases must use the pinned schema and unique IDs")
    for row in corpus["cases"]:
        if (row.get("mode") not in ("preview", "unit", "component") or type(row.get("nativeTextParity")) is not bool
                or (row.get("nativeTextParity") and row["presentation"]["targetLanguage"] not in ("en", "es", "fr"))
                or row.get("macSource") != "Cotabby/Support/Vocabulary/VocabularyCaptureFactory.swift"
                or row.get("macSecondarySource") != "Cotabby/Support/LanguageLearning/LearningExplanationPolicy.swift"
                or row.get("provenanceKind") not in ("regression-adaptation", "source-policy-extension")
                or (row.get("provenanceKind") == "regression-adaptation" and row.get("macTest") !=
                    "CotabbyTests/Support/Vocabulary/VocabularyCaptureFactoryTests.swift")):
            parser.error("Capture cases require explicit source/test/native-scope provenance")
    vocabulary = source(args.mac_repo, "Cotabby/Models/Vocabulary/VocabularyModels.swift")
    policy = source(args.mac_repo, "Cotabby/Support/Vocabulary/VocabularyPolicy.swift")
    explanation = source(args.mac_repo, "Cotabby/Models/LanguageLearning/LearningExplanationModels.swift")
    explanation_policy = source(args.mac_repo, "Cotabby/Support/LanguageLearning/LearningExplanationPolicy.swift")
    sentences = source(args.mac_repo, "Cotabby/Models/LanguageLearning/LearningSentenceModels.swift")
    reading = source(args.mac_repo, "Cotabby/Models/LanguageLearning/LearningReadingSelection.swift")
    units = source(args.mac_repo, "Cotabby/Models/LanguageLearning/LearningUnitModels.swift")
    words = source(args.mac_repo, "Cotabby/Models/LanguageLearning/LearningDictionaryWord.swift")
    # The vocabulary declarations below are complete original types; notebook entry/sense/review
    # graphs are not needed by these capture methods and are omitted rather than stubbed.
    slices = ["import Foundation\n" + between(explanation, "nonisolated enum LearningExplanationContext:", "/// UI sessions borrow this app-owned boundary."),
        "import Foundation\nnonisolated enum VocabularyEntry {\n" +
            between(vocabulary, "    enum Kind:", "    var expression: String").split("    let id: UUID")[0] + "}\n" +
            between(vocabulary, "nonisolated enum VocabularyMeaningOrigin:", "/// The immutable notebook value"),
        "import Foundation\nimport CryptoKit\nimport NaturalLanguage\nnonisolated enum VocabularyPolicy {\n" +
            between(policy, "    static func exampleKey(", "    static func captureKey(") +
            between(policy, "    static func valid(", "    static func context(") +
            between(policy, "    private static func key(", "}\n\nnonisolated enum VocabularyError:") + "}\n",
        "import Foundation\nnonisolated enum LearningExplanationPolicy {\n" +
            between(explanation_policy, "    static func input(_ capture:", "    static func capture(_ entry:") +
            between(explanation_policy, "    static func input(_ presentation:", "    static func supportsGeneralInstructions(") +
            between(explanation_policy, "    static func isValid(_ input:", "    static func prompt(") + "}\n",
        "import Foundation\nimport CoreGraphics\n" + between(sentences, "struct LearningSentencePresentation {", "@MainActor\nprotocol LearningSentencePresenting:") +
            between(reading, "nonisolated enum LearningReadingSource:", "/// Memory-only read receipt."),
        "import Foundation\n" + between(units, "nonisolated struct LearningUnit:", "/// Recognition and lookup share a resource,") +
            between(words, "nonisolated struct LearningDictionaryWord:", "/// View-local geometry exists")]
    with tempfile.TemporaryDirectory(prefix="lingrove-capture-parity-") as temporary:
        root = Path(temporary); paths = []
        for index, value in enumerate([source(args.mac_repo, path) for path in SOURCES] + slices):
            path = root / f"source{index}.swift"; path.write_text(value, encoding="utf-8"); paths.append(str(path))
        driver = root / "main.swift"; driver.write_text(DRIVER, encoding="utf-8")
        binary = root / "capture-parity"
        subprocess.run(["swiftc", "-O", *paths, str(driver), "-o", str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open("x", encoding="utf-8") as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write("\n")
        print(f"Captured {len(observations)} actual Mac writing-capture observations")
        return
    if len(observations) != len(corpus["cases"]): raise SystemExit("Capture observation count changed")
    for row, observation in zip(corpus["cases"], observations):
        if observation != {"id": row["id"], "expected": row["expected"], "facts": row["facts"]}:
            raise SystemExit(f"Mac capture mismatch: {row['id']}")
    print(f"Verified {len(observations)} writing-capture cases against actual pinned Mac composition")


if __name__ == "__main__":
    main()
