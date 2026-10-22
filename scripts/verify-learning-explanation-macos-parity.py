#!/usr/bin/env python3
"""Run the pinned Mac explanation input/prompt/parser rules, without a model or app.

Compile slices exclude capture/notebook construction. Model-gate rules include the actual settings
availability property in fixture-only value wrappers, without preferences/Combine ownership.
Every exercised rule and dependency is original code;
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
    var bool: Bool { guard case let .boolean(v) = self else { fatalError("Expected boolean") }; return v }
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
if CommandLine.arguments[1] == "--models" {
    let corpus = try JSONDecoder().decode(Value.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[2])))
    let observations = corpus["cases"].values.map { row -> Value in
        if row["mode"].string == "output" {
            let configured: Int?
            if case .null = row["configured"] { configured = nil } else { configured = row["configured"].int }
            return .object(["id": row["id"], "tokens": .integer(explanationOutput(configured))])
        }
        let template = LearningChatTemplate(rawValue: row["template"].string)!
        if row["mode"].string == "supports" {
            return .object(["id": row["id"], "available": .boolean(LearningExplanationPolicy.supportsGeneralInstructions(
                model: row["model"].string, template: template))])
        }
        let resolved = row["resolvedTemplate"].optionalString.flatMap(LearningChatTemplate.init(rawValue:))
        let settings = ExplanationFixtureSettings(activeSelection: .init(
            backend: row["local"].bool ? .local : .endpoint, localModel: row["model"].string,
            endpointModel: row["model"].string, template: template),
            resolvedTemplates: resolved.map { ["fixture": $0] } ?? [:])
        return .object(["id": row["id"], "available": settings.generalInstructionsAvailable.map(Value.boolean) ?? .null])
    }
    let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]
    FileHandle.standardOutput.write(try encoder.encode(observations))
    exit(0)
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
    parser.add_argument("--capture-model-output", type=Path, help="Write actual Mac model-gate observations to a NEW file")
    args = parser.parse_args()
    if args.capture_output and args.capture_model_output:
        parser.error("Capture one corpus at a time")
    model_fixture = Path(__file__).resolve().parents[1] / "fixtures/parity/learning-explanation-model-v1.json"
    model_corpus = json.loads(model_fixture.read_text(encoding="utf-8"))
    model_ids = [row.get("id") for row in model_corpus["cases"]]
    if model_corpus.get("version") != 1 or model_corpus.get("macReference") != BASELINE or any(
            not value for value in model_ids) or len(set(model_ids)) != len(model_ids) or any(
            row.get("mode") not in ("supports", "availability", "output") or
            row.get("macSource") != ("Cotabby/Support/LanguageLearning/LearningExplanationPolicy.swift" if row.get("mode") == "supports"
                else "Cotabby/Models/LanguageLearning/LearningModelSettings.swift") or
            (row.get("mode") == "output" and row.get("macSecondarySource") !=
             "Cotabby/Services/LanguageLearning/LearningExplanationService.swift") or
            row.get("provenanceKind") not in ("regression-adaptation", "source-policy-extension") or
            (row.get("provenanceKind") == "regression-adaptation" and
             row.get("macTest") != "CotabbyTests/Support/LanguageLearning/LearningExplanationPolicyTests.swift")
            for row in model_corpus["cases"]):
        parser.error("Model cases require pinned schema, unique IDs and original method/test provenance")
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
    settings = source(args.mac_repo, "Cotabby/Models/LanguageLearning/LearningModelSettings.swift")
    runtime = source(args.mac_repo, "Cotabby/Models/Runtime/LlamaRuntimeModels.swift")
    service = source(args.mac_repo, "Cotabby/Services/LanguageLearning/LearningExplanationService.swift")
    output_clamp = between(settings, "maximumOutputTokens: value.maximumOutputTokens.map { ", " }),\n            for: contextIdentity)")
    output_clamp = output_clamp.removeprefix("maximumOutputTokens: value.maximumOutputTokens.map { ")
    output_default = between(service, "                let output = profile.maximumOutputTokens ?? ", "                let prompt = try LearningExplanationPolicy.prompt")
    output_default = output_default.strip()
    slices = ["import Foundation\n" + between(models, "nonisolated enum LearningExplanationContext:",
               "/// A durable, context-specific learning artifact."),
        "import Foundation\nnonisolated enum VocabularyEntry {\n" +
            between(vocabulary, "    enum Kind:", "    var expression: String").split("    let id: UUID")[0] + "}\n" +
            between(vocabulary, "nonisolated struct VocabularyTextRange:", "/// Short-lived card input."),
        "import Foundation\nnonisolated enum VocabularyPolicy {\n" +
            between(vocabulary_policy, "    static func valid(_ range:", "    /// The window's two completeness flags") +
            "}\n" + vocabulary_policy[vocabulary_policy.index("nonisolated enum VocabularyError:"):],
        "import Foundation\nnonisolated enum LearningExplanationPolicy {\n" +
            between(policy, "    static func supportsGeneralInstructions(", "    /// The target and its occurrence quotes") +
            policy[policy.index("    static func isValid(_ input:"):],
        "import Foundation\n" + between(settings, "nonisolated enum LearningModelRole:", "/// Retained by one request."),
        "import Foundation\nnonisolated enum RuntimeModelCatalog {\n" +
            between(runtime, "    nonisolated static func isKnownBaseModel(", "    static func displayName(") + "}\n",
        # These fixture DTO fields replace only preferences/Combine ownership. The computed
        # availability property below is copied intact and is the only settings algorithm run.
        "import Foundation\nstruct ExplanationFixtureSettings {\n" +
            "struct Selection { let backend: LearningModelBackend; let localModel: String; " +
            "let endpointModel: String; let template: LearningChatTemplate }\n" +
            "let activeSelection: Selection; let resolvedTemplates: [String: LearningChatTemplate]\n" +
            "let contextIdentity = \"fixture\"\n" +
            between(settings, "    var generalInstructionsAvailable:", "    var recommendedSampling:") + "}\n",
        # Extract the actual clamp expression and service default. The fixture wrapper only
        # supplies a profile value instead of preferences/Combine and returns its effective output.
        "struct ExplanationOutputProfile { let maximumOutputTokens: Int? }\n" +
            "func explanationOutput(_ value: Int?) -> Int {\n" +
            "let profile = ExplanationOutputProfile(maximumOutputTokens: value.map { " + output_clamp + " })\n" +
            output_default + "\nreturn output\n}\n"]
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
        model_observations = json.loads(subprocess.check_output([str(binary), "--models", str(model_fixture)]))
    capture = args.capture_output or args.capture_model_output
    if capture:
        with capture.open("x", encoding="utf-8") as output:
            values = observations if args.capture_output else model_observations
            json.dump(values, output, ensure_ascii=False, indent=2)
            output.write("\n")
        print(f"Captured {len(values)} original Mac explanation observations to {capture}")
        return
    if len(observations) != len(corpus["cases"]):
        raise SystemExit("Observation count changed")
    for row, observation in zip(corpus["cases"], observations):
        expected = {"id": row["id"], **row["expected"]}
        if observation != expected:
            raise SystemExit(f"Mac explanation mismatch: {row['id']}")
    print(f"Verified {len(observations)} explanation cases against actual pinned Mac rules")
    if len(model_observations) != len(model_corpus["cases"]):
        raise SystemExit("Model observation count changed")
    for row, observation in zip(model_corpus["cases"], model_observations):
        if observation != {"id": row["id"], **row["expected"]}:
            raise SystemExit(f"Mac explanation model-gate mismatch: {row['id']}")
    print(f"Verified {len(model_observations)} explanation model-gate cases against actual pinned Mac methods")


if __name__ == "__main__":
    main()
