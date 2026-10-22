#!/usr/bin/env python3
"""Run the learning behavior corpus against pure Swift from the pinned macOS commit.

The verifier compiles the actual style, language, prompt, source-language ranking, budget, and output-
policy declarations read with `git show`. Small DTO/error stubs only replace unrelated app-owned
dependencies; the rules under test remain the committed macOS source. It never builds the app, loads
a model, makes a network request, or connects to an editor.
"""

import argparse
import json
from pathlib import Path
import subprocess
import tempfile


BASELINE = "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f"
SOURCES = (
    "Cotabby/Models/Context/LanguageCatalog.swift",
    "Cotabby/Models/LanguageLearning/LearningLanguage.swift",
    "Cotabby/Support/Prompting/TokenCountEstimator.swift",
    "Cotabby/Support/Prompting/ModelContextBudget.swift",
    "Cotabby/Support/LanguageLearning/LearningFailure.swift",
    "Cotabby/Support/LanguageLearning/LearningExpressionPrompt.swift",
    "Cotabby/Support/LanguageLearning/LearningTranslationPrompt.swift",
    "Cotabby/Support/LanguageLearning/LearningTranslationBudget.swift",
    "Cotabby/Support/LanguageLearning/LearningExpressionOutputPolicy.swift",
    "Cotabby/Support/LanguageLearning/LearningSourceLanguageResolver.swift",
    "Cotabby/Support/Suggestion/Acceptance/InsertionSafetyGate.swift",
)
STYLE_SOURCE = "Cotabby/Models/LanguageLearning/LearningGenerationProfile.swift"
TRANSLATION_SERVICE = "Cotabby/Services/LanguageLearning/LearningTranslationService.swift"


STUBS = r'''
import Foundation

// These declarations stand in only for app-owned values referenced by the pure policies below.
// The style, language, prompt, budget, tokenizer estimate, output policy, and safety gate all come
// verbatim from the pinned macOS commit.
struct LearningTranslationInput {
    let source: String
    var referenceBefore: String
    var referenceAfter: String
    var style: LearningTranslationStyle

    init(source: String, referenceBefore: String = "", referenceAfter: String = "",
         style: LearningTranslationStyle = .init()) {
        self.source = source
        self.referenceBefore = referenceBefore
        self.referenceAfter = referenceAfter
        self.style = style
    }
}

enum LearningChatTemplate {
    case generic, hyMT, hyMT7, translateGemma
}

struct ModelSamplingValues {
    let temperature: Double
    let topP: Double
    let topK: Int
    let repetitionPenalty: Double
}

enum OpenAICompatibleClientError: Error {
    case invalidResponse
}
'''


DRIVER = r'''
import Foundation

let data = try Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))
let corpus = try JSONSerialization.jsonObject(with: data) as! [String: Any]
var checked = 0
var failures = [String]()

@MainActor func check(_ passed: Bool, id: String, detail: String) {
    checked += 1
    if !passed { failures.append("\(id): \(detail)") }
}

func string(_ row: [String: Any], _ key: String) -> String {
    guard let value = row[key] as? String else { fatalError("missing string \(key)") }
    return value
}

func integer(_ row: [String: Any], _ key: String) -> Int {
    guard let value = row[key] as? Int else { fatalError("missing integer \(key)") }
    return value
}

func boolean(_ row: [String: Any], _ key: String) -> Bool {
    guard let value = row[key] as? Bool else { fatalError("missing bool \(key)") }
    return value
}

func text(_ value: Any) -> String {
    if let literal = value as? String { return literal }
    guard let object = value as? [String: Any] else { fatalError("invalid text spec") }
    if let repeatSpec = object["repeat"] as? [String: Any] {
        guard let fragment = repeatSpec["text"], let count = repeatSpec["count"] as? Int else {
            fatalError("invalid repeat text spec")
        }
        return String(repeating: text(fragment), count: count)
    }
    if let parts = object["concat"] as? [Any] { return parts.map(text).joined() }
    fatalError("unknown text spec")
}

func optionalText(_ value: Any) -> String? {
    value is NSNull ? nil : text(value)
}

func register(_ name: String) -> LearningTranslationStyle.Register {
    guard let value = LearningTranslationStyle.Register(rawValue: name) else { fatalError("unknown register \(name)") }
    return value
}

func family(_ name: String) -> LearningTranslationPrompt.Family {
    switch name {
    case "generic": .generic
    case "hyMT": .hyMT
    case "translateGemma": .translateGemma
    default: fatalError("unknown prompt family \(name)")
    }
}

func language(_ code: String) -> LearningLanguage {
    guard let value = LearningLanguage(rawValue: code) else { fatalError("unknown fixture language \(code)") }
    return value
}

let languageCodes = corpus["languageCodes"] as! [String]
check(LearningLanguage.allCases.map(\.rawValue) == languageCodes,
      id: "language-codes", detail: "pinned macOS language identifiers or their order changed")
let unsupportedCodes = corpus["unsupportedLanguageCodes"] as! [String]
for code in unsupportedCodes {
    check(LearningLanguage(rawValue: code) == nil,
          id: "unsupported-language-\(code)", detail: "unsupported code unexpectedly resolved")
}
let encodedChinese = String(decoding: try JSONEncoder().encode(LearningLanguage.chinese), as: UTF8.self)
check(encodedChinese == corpus["serializedChinese"] as! String,
      id: "language-json", detail: "Chinese identifier encoding changed to \(encodedChinese)")

for row in corpus["styleCases"] as! [[String: Any]] {
    let id = string(row, "id")
    let style = LearningTranslationStyle(
        register: register(string(row, "register")),
        customInstruction: text(row["customInput"]!))
    let normalized = style.normalized.customInstruction
    let expectedNormalized = text(row["expectedNormalized"]!)
    let expectedInstruction = row["expectedInstruction"] as? String
    let effective = style.effective
    check(normalized == expectedNormalized
          && style.instruction == expectedInstruction
          && effective.register.rawValue == string(row, "expectedEffectiveRegister")
          && effective.customInstruction == text(row["expectedEffectiveCustom"]!),
          id: id, detail: "normalization/instruction/effective style differed")
}

for row in corpus["styleBounds"] as! [[String: Any]] {
    let id = string(row, "id")
    let style = LearningTranslationStyle(
        register: register(string(row, "register")),
        customInstruction: text(row["customInput"]!))
    let actual = style.normalized.customInstruction
    let expected = text(row["expectedNormalized"]!)
    let hasReplacement = actual.unicodeScalars.contains("\u{FFFD}")
    check(actual == expected
          && actual.utf8.count == integer(row, "expectedUtf8Bytes")
          && actual.count == integer(row, "expectedGraphemes")
          && hasReplacement == boolean(row, "expectedContainsReplacement"),
          id: id, detail: "actual UTF-8 bytes \(actual.utf8.count), graphemes \(actual.count), replacement \(hasReplacement)")
}

let constants = corpus["budgetConstants"] as! [String: Int]
check(ModelContextBudget.maximumLocalTokens == constants["maximumLocalTokens"]
      && ModelContextBudget.maximumEndpointTokens == constants["maximumEndpointTokens"]
      && ModelContextBudget.maximumTextBytes == constants["maximumTextBytes"]
      && ModelContextBudget.maximumTranslationOutputTokens == constants["maximumTranslationOutputTokens"],
      id: "budget-constants", detail: "pinned context limits changed")

for row in corpus["tokenEstimates"] as! [[String: Any]] {
    let actual = ModelContextBudget.estimatedTokens(text(row["text"]!))
    check(actual == integer(row, "expectedTokens"),
          id: string(row, "id"), detail: "actual estimate \(actual)")
}

for row in corpus["inputAllowances"] as! [[String: Any]] {
    let actual = ModelContextBudget.inputAllowance(
        total: integer(row, "total"), output: integer(row, "output"), estimated: boolean(row, "estimated"))
    check(actual == integer(row, "expectedAllowance"),
          id: string(row, "id"), detail: "actual allowance \(actual)")
}

for row in corpus["outputReservations"] as! [[String: Any]] {
    let id = string(row, "id")
    do {
        let actual = try ModelContextBudget.translationOutputTokens(
            source: text(row["source"]!), total: integer(row, "total"))
        check(!boolean(row, "expectedFailure") && actual == integer(row, "expectedTokens"),
              id: id, detail: "actual output reserve \(actual)")
    } catch {
        check(boolean(row, "expectedFailure"), id: id, detail: "unexpected output-reserve rejection: \(error)")
    }
}

for row in corpus["promptCases"] as! [[String: Any]] {
    let id = string(row, "id")
    let style = LearningTranslationStyle(
        register: register(row["register"] as? String ?? "natural"),
        customInstruction: row["customInput"].map(text) ?? "")
    let expectedFailure = boolean(row, "expectedFailure")
    do {
        let request = try LearningTranslationPrompt.render(
            source: text(row["source"]!), sourceLanguage: language(string(row, "sourceLanguage")),
            targetLanguage: language(string(row, "targetLanguage")), family: family(string(row, "family")), style: style)
        if expectedFailure {
            check(false, id: id, detail: "expected prompt rejection but got output")
            continue
        }
        let sampling = row["sampling"] as! [String: Any]
        check(request.prompt == text(row["expectedPrompt"]!)
              && request.userOnly == boolean(row, "expectedUserOnly")
              && request.sampling.temperature == sampling["temperature"] as! Double
              && request.sampling.topP == sampling["topP"] as! Double
              && request.sampling.topK == sampling["topK"] as! Int
              && request.sampling.repetitionPenalty == sampling["repetitionPenalty"] as! Double,
              id: id, detail: "prompt, transport role, or sampler differed")
    } catch {
        check(expectedFailure, id: id, detail: "unexpected prompt rejection: \(error)")
    }
}

for row in corpus["promptBudgets"] as! [[String: Any]] {
    let id = string(row, "id")
    let source = text(row["source"]!)
    let countMode = string(row, "countMode")
    let style = LearningTranslationStyle(
        register: register(row["register"] as? String ?? "natural"),
        customInstruction: row["customInput"].map(text) ?? "")
    let counter: (String) -> Int = { prompt in
        switch countMode {
        case "characters": prompt.count
        case "utf8Bytes": prompt.utf8.count
        case "estimatedTokens": ModelContextBudget.estimatedTokens(prompt)
        default: fatalError("unknown count mode \(countMode)")
        }
    }
    do {
        let request = try LearningTranslationBudget.prompt(
            input: LearningTranslationInput(source: source, style: style),
            sourceLanguage: language(string(row, "sourceLanguage")),
            targetLanguage: language(string(row, "targetLanguage")), family: family(string(row, "family")),
            total: integer(row, "total"), output: integer(row, "output"),
            estimated: boolean(row, "estimated"), count: counter)
        if !boolean(row, "expectedAccepted") {
            check(false, id: id, detail: "expected whole-target rejection but the prompt fit")
            continue
        }
        let suffix = row["expectedSourceSuffix"] as? String
        check(suffix.map(request.prompt.hasSuffix) ?? true,
              id: id, detail: "accepted prompt did not preserve the exact target suffix")
    } catch {
        check(!boolean(row, "expectedAccepted"), id: id, detail: "unexpected prompt-budget rejection: \(error)")
    }
}

for row in corpus["outputValidation"] as! [[String: Any]] {
    let id = string(row, "id")
    let raw = text(row["raw"]!)
    do {
        let actual = try LearningExpressionOutputPolicy.validatedText(raw)
        check(boolean(row, "expectedValid")
              && actual == text(row["expectedText"]!)
              && actual.utf8.count == integer(row, "expectedUtf8Bytes"),
              id: id, detail: "validated output differed or exceeded its expected byte count")
    } catch {
        check(!boolean(row, "expectedValid"), id: id, detail: "unexpected output rejection: \(error)")
    }
}

for row in corpus["sourceLanguageScoreCases"] as! [[String: Any]] {
    let id = string(row, "id")
    var scores = [LearningLanguage: Double]()
    for score in row["scores"] as! [[String: Any]] {
        guard let value = score["score"] as? Double else { fatalError("missing score value") }
        scores[language(string(score, "language"))] = value
    }
    let actual = LearningSourceLanguageResolver.confidentLanguage(from: scores)?.rawValue
    let expected = row["expectedLanguage"] as? String
    check(actual == expected, id: id, detail: "actual confident language \(String(describing: actual))")
}

let sourceResolver = LearningSourceLanguageResolver()
for row in corpus["sourceLanguageResolutionCases"] as! [[String: Any]] {
    let id = string(row, "id")
    let source = string(row, "text")
    let enabled = (row["enabledSourceLanguages"] as! [String]).map(language)
    let target = language(string(row, "targetLanguage"))

    // These shared cases cover the resolver's deterministic empty/single-source branches. Keep
    // multi-source recognition out of the corpus: NLModel probabilities vary by OS release.
    var seen = Set<LearningLanguage>()
    let candidates = enabled.filter { $0 != target && seen.insert($0).inserted }
    if candidates.count > 1 {
        fatalError("source resolution fixtures must bypass NaturalLanguage recognition")
    }
    let actual = sourceResolver.resolve(
        text: source, enabledSourceLanguages: enabled, targetLanguage: target)?.rawValue
    check(actual == (row["expectedLanguage"] as? String),
          id: id, detail: "actual explicit source resolution \(String(describing: actual))")
}

for row in corpus["streamAppendCases"] as! [[String: Any]] {
    let id = string(row, "id")
    let chunks = row["chunks"] as! [Any]
    let expectedPrefixes = row["expectedPrefixes"] as! [Any]
    let expectedFailureAt = row["expectedFailureAt"] as? Int
    guard chunks.count == expectedPrefixes.count else { fatalError("stream fixture chunk/prefix counts differ") }
    var stream = LearningExpressionOutputPolicy.Stream()
    var failureAt: Int?
    for index in chunks.indices {
        do {
            let actual = try stream.append(text(chunks[index]))
            if expectedFailureAt == index {
                check(false, id: id, detail: "expected invalid streamed output rejection")
                continue
            }
            check(actual == optionalText(expectedPrefixes[index]),
                  id: "\(id)-prefix-\(index)", detail: "stream prefix differed")
        } catch {
            failureAt = index
            check(expectedFailureAt == index,
                  id: id, detail: "unexpected stream rejection at chunk \(index): \(error)")
            break
        }
    }
    check(failureAt == expectedFailureAt, id: id, detail: "stream rejection occurred at the wrong chunk")
}

if !failures.isEmpty {
    failures.forEach { print("FAIL: \($0)") }
    exit(1)
}
print("Swift macOS learning baseline: \(checked)/\(checked) shared parity checks passed.")
'''


def source_at(repo: Path, path: str) -> str:
    return subprocess.check_output(
        ["git", "-C", str(repo), "show", f"{BASELINE}:{path}"], encoding="utf-8"
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    fixture = root / "fixtures/parity/learning-v1.json"
    corpus = json.loads(fixture.read_text(encoding="utf-8"))
    if corpus.get("schemaVersion") != 1 or corpus.get("macBaselineRevision") != BASELINE:
        parser.error("learning fixture schema or pinned macOS baseline does not match this verifier")

    categories = (
        "styleCases", "styleBounds", "tokenEstimates", "inputAllowances", "outputReservations",
        "promptCases", "promptBudgets", "outputValidation", "sourceLanguageScoreCases",
        "sourceLanguageResolutionCases", "streamAppendCases",
    )
    ids: set[str] = set()
    for group in categories:
        rows = corpus.get(group)
        if not isinstance(rows, list) or not rows:
            parser.error(f"fixture category {group} must contain cases")
        for row in rows:
            case_id = row.get("id")
            if not isinstance(case_id, str) or not case_id or case_id in ids or not row.get("macTest"):
                parser.error("fixture cases require unique nonempty IDs and macOS provenance")
            ids.add(case_id)

    with tempfile.TemporaryDirectory(prefix="lingrove-learning-parity-") as temporary:
        directory = Path(temporary)
        paths = []
        for source in SOURCES:
            target = directory / Path(source).name
            target.write_text(source_at(args.mac_repo, source), encoding="utf-8")
            paths.append(str(target))

        # Keep the production value type exactly as committed while omitting unrelated model-profile
        # fields that would pull in sampling/settings dependencies.
        profile = source_at(args.mac_repo, STYLE_SOURCE)
        marker = "/// One role's provider/model profile."
        if profile.count(marker) != 1:
            raise RuntimeError("pinned translation-style declaration cannot be identified uniquely")
        style_file = directory / "LearningTranslationStyle.swift"
        style_file.write_text(profile[:profile.index(marker)], encoding="utf-8")
        paths.append(str(style_file))

        service = source_at(args.mac_repo, TRANSLATION_SERVICE)
        for call in ("LearningExpressionOutputPolicy.Stream()", "LearningExpressionOutputPolicy.validatedText(raw)"):
            if call not in service:
                raise RuntimeError(f"pinned translation service no longer uses the output policy: {call}")

        stubs = directory / "FixtureBoundaryStubs.swift"
        stubs.write_text(STUBS, encoding="utf-8")
        driver = directory / "main.swift"
        driver.write_text(DRIVER, encoding="utf-8")
        executable = directory / "verify-learning-parity"
        subprocess.run(
            ["xcrun", "swiftc", "-swift-version", "6", *paths, str(stubs), str(driver), "-o", str(executable)],
            check=True,
        )
        subprocess.run([str(executable), str(fixture)], check=True)


if __name__ == "__main__":
    main()
