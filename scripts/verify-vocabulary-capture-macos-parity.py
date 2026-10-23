#!/usr/bin/env python3
"""Execute the pinned Mac capture/usage identity and validation subset, without notebook effects."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

BASELINE = "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f"
SOURCE = "Cotabby/Support/Vocabulary/VocabularyPolicy.swift"
DRIVER = r'''
import Foundation
import CryptoKit

struct TextSpec: Decodable {
    let value: String
    enum Keys: String, CodingKey { case repeating, count }
    init(from decoder: Decoder) throws {
        if let text = try? decoder.singleValueContainer().decode(String.self) { value = text; return }
        let c = try decoder.container(keyedBy: Keys.self)
        let count = try c.decode(Int.self, forKey: .count)
        guard (0...300_000).contains(count) else { throw CocoaError(.coderReadCorrupt) }
        value = String(repeating: try c.decode(String.self, forKey: .repeating), count: count)
    }
}
struct Usage: Decodable {
    let text: TextSpec
    let ranges: [VocabularyTextRange]
    let completeness: VocabularyExample.Completeness
    let translation: String?
    let translationLanguage: LearningLanguage?
    let meaning: String?
    let meaningLanguage: LearningLanguage?
    let meaningOrigin: VocabularyMeaningOrigin?
    let app: String?
    func value() -> VocabularyExample {
        .init(id: UUID(uuidString: "00000000-0000-0000-0000-000000000001")!, text: text.value,
            translation: translation ?? "", translationLanguage: translationLanguage, ranges: ranges,
            completeness: completeness, meaning: meaning ?? "", meaningLanguage: meaningLanguage,
            meaningOrigin: meaningOrigin ?? .captured, appBundleIdentifier: app, createdAt: Date(timeIntervalSince1970: 0))
    }
}
struct Capture: Decodable {
    let expression: TextSpec
    let dictionaryForm: String
    let language: LearningLanguage
    let kind: VocabularyEntry.Kind
    let meaning: String?
    let meaningLanguage: LearningLanguage?
    let example: Usage?
    let senseID: UUID?
    func value() -> VocabularyCapture {
        .init(expression: expression.value, dictionaryForm: dictionaryForm, language: language, kind: kind,
            meaning: meaning ?? "", meaningLanguage: meaningLanguage, example: example?.value(), senseID: senseID)
    }
}
struct Row: Decodable { let id: String; let capture: Capture }
struct Corpus: Decodable { let cases: [Row] }
struct Observation: Encodable {
    let valid: Bool
    let explanationValid: Bool
    let explanationReceipt: String?
    let normalized: String?
    let normalizedBytes: Int
    let normalizedSHA256: String
    let entryKey: String
    let exampleKey: String
    let captureKey: String
    enum CodingKeys: String, CodingKey { case valid, explanationValid, explanationReceipt, normalized, normalizedBytes, normalizedSHA256, entryKey, exampleKey, captureKey }
    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(valid, forKey: .valid); try c.encode(explanationValid, forKey: .explanationValid)
        try c.encode(explanationReceipt, forKey: .explanationReceipt); try c.encode(normalized, forKey: .normalized)
        try c.encode(normalizedBytes, forKey: .normalizedBytes); try c.encode(normalizedSHA256, forKey: .normalizedSHA256)
        try c.encode(entryKey, forKey: .entryKey); try c.encode(exampleKey, forKey: .exampleKey); try c.encode(captureKey, forKey: .captureKey)
    }
}
struct Result: Encodable { let id: String; let expected: Observation }
let corpus = try JSONDecoder().decode(Corpus.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
let results = corpus.cases.map { row in
    let capture = row.capture.value()
    let valid: Bool
    do { try VocabularyPolicy.validate(capture); valid = true } catch { valid = false }
    let normalized = VocabularyPolicy.normalized(capture.expression), bytes = Data(normalized.utf8)
    let input = LearningExplanationPolicy.input(capture)
    return Result(id: row.id, expected: Observation(valid: valid, explanationValid: LearningExplanationPolicy.isValid(input),
        explanationReceipt: input.occurrenceIdentity, normalized: bytes.count <= 1_024 ? normalized : nil,
        normalizedBytes: bytes.count, normalizedSHA256: SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined(),
        entryKey: VocabularyPolicy.entryKey(capture), exampleKey: VocabularyPolicy.exampleKey(capture.example),
        captureKey: VocabularyPolicy.captureKey(capture)))
}
let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]
FileHandle.standardOutput.write(try encoder.encode(results))
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
    parser.add_argument("--fixture", type=Path, default=Path(__file__).resolve().parents[1] / "fixtures/parity/vocabulary-capture-v1.json")
    parser.add_argument("--capture-output", type=Path, help="Author actual observations into a NEW file")
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding="utf-8")); rows = corpus["cases"]
    ids = [row.get("id") for row in rows]
    if (corpus.get("version") != 1 or corpus.get("macReference") != BASELINE or not rows
            or any(not value for value in ids) or len(set(ids)) != len(ids)):
        parser.error("Capture cases require pinned version and unique IDs")
    for row in rows:
        if row.get("macSource") != SOURCE or row.get("provenanceKind") != "source-policy-extension":
            parser.error("These pure identity probes require explicit source-policy provenance")
    models = source(args.mac_repo, "Cotabby/Models/Vocabulary/VocabularyModels.swift")
    kind = between(models, "    enum Kind:", "    var expression: String").split("    let id: UUID")[0]
    values = "import Foundation\nnonisolated enum VocabularyEntry {\n" + kind + "}\n" + between(
        models, "nonisolated enum VocabularyMeaningOrigin:", "/// The immutable notebook value")
    explanation = source(args.mac_repo, "Cotabby/Models/LanguageLearning/LearningExplanationModels.swift")
    explanation = "import Foundation\n" + between(explanation, "nonisolated enum LearningExplanationContext:", "/// UI sessions borrow this app-owned boundary.")
    original = source(args.mac_repo, SOURCE)
    policy = "import Foundation\nimport CryptoKit\nnonisolated enum VocabularyPolicy {\n" + between(
        original, "    static func normalized(", "    static func entryKey(_ entry:") + between(
        original, "    static func exampleKey(", "    static func contains(") + between(
        original, "    static func validate(", "    /// The window's two completeness flags") + between(
        original, "    private static func key(", "}\n\nnonisolated enum VocabularyError:") + "}\n"
    error = "import Foundation\n" + original[original.index("nonisolated enum VocabularyError:"):]
    explanation_policy = source(args.mac_repo, "Cotabby/Support/LanguageLearning/LearningExplanationPolicy.swift")
    projection = "import Foundation\nnonisolated enum LearningExplanationPolicy {\n" + between(
        explanation_policy, "    static func input(_ capture:", "    static func capture(_ entry:") + between(
        explanation_policy, "    static func isValid(_ input:", "    static func prompt(") + "}\n"
    sources = [source(args.mac_repo, path) for path in ["Cotabby/Models/Context/LanguageCatalog.swift", "Cotabby/Models/LanguageLearning/LearningLanguage.swift", "Cotabby/Support/Prompting/ModelContextBudget.swift", "Cotabby/Support/Prompting/TokenCountEstimator.swift", "Cotabby/Support/LanguageLearning/LearningFailure.swift"]]
    # Complete usage/capture declarations and original pure method slices. The enclosing entry
    # contributes only its actual Kind declaration, not a fake sense/storage implementation.
    with tempfile.TemporaryDirectory(prefix="lingrove-usage-parity-") as temporary:
        root = Path(temporary); paths = []
        for index, value in enumerate([*sources, values, explanation, policy, error, projection]):
            path = root / f"source{index}.swift"; path.write_text(value, encoding="utf-8"); paths.append(str(path))
        driver = root / "main.swift"; driver.write_text(DRIVER, encoding="utf-8")
        binary = root / "usage-parity"
        subprocess.run(["swiftc", "-swift-version", "6", "-O", *paths, str(driver), "-o", str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open("x", encoding="utf-8") as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write("\n")
        print(f"Captured {len(observations)} actual Mac usage/capture observations")
        return
    if len(observations) != len(rows): raise SystemExit("Capture observation count changed")
    for row, observed in zip(rows, observations):
        if observed != {"id": row["id"], "expected": row["expected"]}: raise SystemExit(f"Mac usage mismatch: {row['id']}")
    print(f"Verified {len(observations)} usage/capture cases against actual pinned Mac identity/validation")


if __name__ == "__main__":
    main()
