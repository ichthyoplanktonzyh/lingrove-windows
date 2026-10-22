#!/usr/bin/env python3
"""Run the pinned Mac explanation input/prompt/parser rules, without a model or app.

Compile slices exclude capture/notebook construction and the model-compatibility gate, which
are outside this port's current scope. Every exercised rule and dependency is original code;
the driver only expands fixture text and projects returned values. JSON object key order is
not a Swift JSONEncoder contract, so prompts compare exact surrounding prose, semantic payload,
and exact UTF-8/token budgets rather than a serialized object order.
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
    "Cotabby/Support/Prompting/TokenCountEstimator.swift",
    "Cotabby/Support/Prompting/ModelContextBudget.swift",
    "Cotabby/Support/LanguageLearning/LearningFailure.swift",
    "Cotabby/Support/Suggestion/Acceptance/InsertionSafetyGate.swift",
]

DRIVER = r'''
import Foundation

// Codable preserves native Swift strings. JSONSerialization's NSString bridge strips a leading
// U+FEFF when cast to String, which would corrupt both the test input and payload observations.
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
    subscript(key: String) -> Value {
        guard case let .object(v) = self else { fatalError("Expected object") }
        return v[key] ?? .null
    }
    var string: String { guard case let .string(v) = self else { fatalError("Expected string") }; return v }
    var int: Int { guard case let .integer(v) = self else { fatalError("Expected integer") }; return v }
    var values: [Value] { guard case let .array(v) = self else { fatalError("Expected array") }; return v }
    var optionalString: String? { if case let .string(v) = self { return v }; return nil }
}
func text(_ spec: Value) -> String {
    if case let .string(value) = spec { return value }
    if case let .array(parts) = spec["concat"] { return parts.map(text).joined() }
    let repeated = spec["repeat"]
    return String(repeating: text(repeated["text"]), count: repeated["count"].int)
}
func input(_ row: Value) -> LearningExplanationInput {
    .init(target: text(row["target"]), dictionaryForm: text(row["dictionaryForm"]),
        language: LearningLanguage(rawValue: row["language"].string)!,
        kind: VocabularyEntry.Kind(rawValue: row["kind"].string)!, context: text(row["context"]),
        ranges: row["ranges"].values.map {
            VocabularyTextRange(NSRange(location: $0["location"].int, length: $0["length"].int))
        }, contextKind: LearningExplanationContext(rawValue: row["contextKind"].optionalString ?? "fragment")!,
        occurrenceIdentity: row["occurrenceIdentity"].optionalString)
}
let corpus = try JSONDecoder().decode(Value.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
var observations = [Value]()
for row in corpus["cases"].values {
    var result: [String: Value] = ["id": row["id"]]
    do {
        switch row["mode"].string {
        case "input": result["valid"] = .boolean(LearningExplanationPolicy.isValid(input(row["input"])))
        case "result": result["valid"] = .boolean(LearningExplanationPolicy.isValid(
            LearningExplanationResult(meaning: text(row["meaning"]), explanation: text(row["explanation"]))))
        case "parse":
            let parsed = try LearningExplanationPolicy.parse(text(row["raw"]))
            result["meaning"] = .string(parsed.meaning); result["explanation"] = .string(parsed.explanation)
        case "prompt":
            let total = row["total"].int, output = row["output"].int
            let prompt = try LearningExplanationPolicy.prompt(input(row["input"]),
                language: LearningLanguage(rawValue: row["responseLanguage"].string)!, total: total, output: output)
            let marker = prompt.range(of: "Study material:\n", options: .backwards)!
            let after = prompt[marker.upperBound...]
            let end = after.lastIndex(of: "\n")!
            result["prefix"] = .string(String(prompt[..<marker.upperBound]))
            result["payload"] = try JSONDecoder().decode(Value.self, from: Data(after[..<end].utf8))
            result["suffix"] = .string(String(after[end...]))
            result["bytes"] = .integer(prompt.utf8.count)
            result["tokens"] = .integer(ModelContextBudget.estimatedTokens(prompt))
            result["allowance"] = .integer(ModelContextBudget.inputAllowance(total: total, output: output, estimated: true))
        default: fatalError("Unknown fixture mode")
        }
    } catch VocabularyError.invalidContent {
        result["error"] = .string("invalidContent")
        result["message"] = .string(VocabularyError.invalidContent.localizedDescription)
    } catch let failure as LearningFailure {
        result["error"] = .string("message")
        result["message"] = .string(failure.localizedDescription)
    } catch {
        // A harness/encoding failure is not a normal rejection that fixture capture may bless.
        fatalError("Unexpected reference driver failure: \(error)")
    }
    observations.append(.object(result))
}
let encoder = JSONEncoder()
encoder.outputFormatting = [.sortedKeys]
FileHandle.standardOutput.write(try encoder.encode(observations))
'''


def source(repo, path):
    return subprocess.check_output(["git", "-C", str(repo), "show", f"{BASELINE}:{path}"], text=True)


def between(value, start, end):
    if value.count(start) != 1 or value.count(end) != 1:
        raise RuntimeError("Pinned source slice boundaries must be unique")
    return value[value.index(start):value.index(end)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, default=Path(__file__).resolve().parents[1] /
                        "fixtures/parity/learning-explanation-v1.json")
    parser.add_argument("--capture-output", type=Path, help="Write actual Mac observations to a NEW file for fixture authoring")
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding="utf-8"))
    if corpus.get("version") != 1 or corpus.get("macReference") != BASELINE:
        parser.error("Fixture must use schema 1 and the pinned Mac revision")
    ids = [row.get("id") for row in corpus["cases"]]
    if any(not value for value in ids) or len(set(ids)) != len(ids) or any(
            row.get("mode") not in ("input", "result", "parse", "prompt") or
            row.get("macSource") != "Cotabby/Support/LanguageLearning/LearningExplanationPolicy.swift" or
            row.get("provenanceKind") not in ("regression-adaptation", "source-policy-extension") or
            (row.get("provenanceKind") == "regression-adaptation" and
             row.get("macTest") != "CotabbyTests/Support/LanguageLearning/LearningExplanationPolicyTests.swift")
            for row in corpus["cases"]):
        parser.error("Cases need unique IDs, supported modes and explicit Mac provenance")
    models = source(args.mac_repo, "Cotabby/Models/LanguageLearning/LearningExplanationModels.swift")
    vocabulary = source(args.mac_repo, "Cotabby/Models/Vocabulary/VocabularyModels.swift")
    vocabulary_policy = source(args.mac_repo, "Cotabby/Support/Vocabulary/VocabularyPolicy.swift")
    policy = source(args.mac_repo, "Cotabby/Support/LanguageLearning/LearningExplanationPolicy.swift")
    slices = ["import Foundation\n" + between(models, "nonisolated enum LearningExplanationContext:",
               "/// A durable, context-specific learning artifact."),
        "import Foundation\nnonisolated enum VocabularyEntry {\n" +
            between(vocabulary, "    enum Kind:", "    var expression: String").split("    let id: UUID")[0] + "}\n" +
            between(vocabulary, "nonisolated struct VocabularyTextRange:", "/// Short-lived card input."),
        "import Foundation\nnonisolated enum VocabularyPolicy {\n" +
            between(vocabulary_policy, "    static func valid(_ range:", "    /// The window's two completeness flags") +
            "}\n" + vocabulary_policy[vocabulary_policy.index("nonisolated enum VocabularyError:"):],
        "import Foundation\nnonisolated enum LearningExplanationPolicy {\n" +
            policy[policy.index("    static func isValid(_ input:"):]]
    with tempfile.TemporaryDirectory(prefix="lingrove-explanation-parity-") as temporary:
        root = Path(temporary)
        paths = []
        for index, value in enumerate([source(args.mac_repo, path) for path in SOURCES] + slices):
            path = root / f"source{index}.swift"
            path.write_text(value, encoding="utf-8")
            paths.append(str(path))
        driver = root / "main.swift"
        driver.write_text(DRIVER, encoding="utf-8")
        binary = root / "explanation-parity"
        subprocess.run(["swiftc", "-O", *paths, str(driver), "-o", str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open("x", encoding="utf-8") as output:
            json.dump(observations, output, ensure_ascii=False, indent=2)
            output.write("\n")
        print(f"Captured {len(observations)} original Mac explanation observations to {args.capture_output}")
        return
    if len(observations) != len(corpus["cases"]):
        raise SystemExit("Observation count changed")
    for row, observation in zip(corpus["cases"], observations):
        expected = {"id": row["id"], **row["expected"]}
        if observation != expected:
            raise SystemExit(f"Mac explanation mismatch: {row['id']}")
    print(f"Verified {len(observations)} explanation cases against actual pinned Mac rules")


if __name__ == "__main__":
    main()
