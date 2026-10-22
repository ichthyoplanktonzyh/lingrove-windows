#!/usr/bin/env python3
"""Run local learning template behavior against pure Swift from the pinned macOS commit.

The harness executes the committed GGUF metadata reader, template resolver, delimiters, token
budget, sampler mapping, catalog recommendations, and settings value defaults. Only DTOs and
unrelated settings value types are stubbed. It creates tiny GGUF metadata headers in a temporary
directory; it never opens model weights, runs inference, builds the app, or makes network calls.
"""

import argparse
import json
from pathlib import Path
import subprocess
import tempfile


BASELINE = "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f"
MODEL_SETTINGS = "Cotabby/Models/LanguageLearning/LearningModelSettings.swift"
LOCAL_CONFIGURATION = "Cotabby/Models/LanguageLearning/LearningLocalModelConfiguration.swift"
LOCAL_PROMPT = "Cotabby/Support/LanguageLearning/LearningLocalPrompt.swift"
TRANSLATION_PROMPT = "Cotabby/Support/LanguageLearning/LearningTranslationPrompt.swift"
RUNTIME_CATALOG = "Cotabby/Models/LanguageLearning/LearningRuntimeCatalog.swift"
MODEL_CONTEXT_BUDGET = "Cotabby/Support/Prompting/ModelContextBudget.swift"
TOKEN_ESTIMATOR = "Cotabby/Support/Prompting/TokenCountEstimator.swift"


STUBS = r'''
import Foundation

// These types only satisfy unrelated app-owned dependencies. The methods and values under test
// come from the pinned macOS declarations included below.
enum LearningFailure: Error {
    case message(String)
}

struct LearningTranslationStyle: Equatable, Hashable, Sendable {}
struct ModelSamplingOverrides: Equatable, Hashable, Sendable {}

struct ModelSamplingValues {
    let temperature: Double
    let topP: Double
    let topK: Int
    let repetitionPenalty: Double
}

struct DownloadableRuntimeModel: Identifiable {
    let filename: String
    let displayName: String
    let downloadURL: URL
    let approximateSizeInGigabytes: Double
    let expectedSizeBytes: Int64?
    let sha256: String?
    let recommendation: String?

    var id: String { downloadURL.absoluteString }

    init(
        filename: String,
        displayName: String,
        downloadURL: URL,
        approximateSizeInGigabytes: Double,
        expectedSizeBytes: Int64? = nil,
        sha256: String? = nil,
        recommendation: String? = nil
    ) {
        self.filename = filename
        self.displayName = displayName
        self.downloadURL = downloadURL
        self.approximateSizeInGigabytes = approximateSizeInGigabytes
        self.expectedSizeBytes = expectedSizeBytes
        self.sha256 = sha256
        self.recommendation = recommendation
    }
}
'''


DRIVER = r'''
import Foundation

let corpusURL = URL(fileURLWithPath: CommandLine.arguments[1])
let corpusData = try Data(contentsOf: corpusURL)
let corpus = try JSONSerialization.jsonObject(with: corpusData) as! [String: Any]
let temporaryRoot = FileManager.default.temporaryDirectory
    .appendingPathComponent("learning-local-parity-\(UUID().uuidString)", isDirectory: true)
try FileManager.default.createDirectory(at: temporaryRoot, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: temporaryRoot) }

var checked = 0
var failures = [String]()

@MainActor func check(_ passed: Bool, id: String, detail: String) {
    checked += 1
    if !passed { failures.append("\(id): \(detail)") }
}

func string(_ object: [String: Any], _ key: String) -> String {
    guard let value = object[key] as? String else { fatalError("missing string \(key)") }
    return value
}

func integer(_ object: [String: Any], _ key: String) -> Int {
    guard let value = object[key] as? Int else { fatalError("missing integer \(key)") }
    return value
}

func boolean(_ object: [String: Any], _ key: String) -> Bool {
    guard let value = object[key] as? Bool else { fatalError("missing bool \(key)") }
    return value
}

func requiredTemplate(_ code: String) -> LearningChatTemplate {
    guard let template = LearningChatTemplate(rawValue: code) else {
        fatalError("unknown template identifier \(code)")
    }
    return template
}

func optionalInt(_ object: [String: Any], _ key: String) -> Int? {
    guard let value = object[key] else { fatalError("missing optional integer \(key)") }
    return value is NSNull ? nil : value as? Int
}

func familyCode(_ family: LearningTranslationPrompt.Family) -> String {
    switch family {
    case .generic: "generic"
    case .hyMT: "hyMT"
    case .translateGemma: "translateGemma"
    }
}

func writeInteger(_ value: UInt64, bytes: Int, into data: inout Data) {
    for shift in 0..<bytes {
        data.append(UInt8(truncatingIfNeeded: value >> (shift * 8)))
    }
}

func writeString(_ value: String, into data: inout Data) {
    let utf8 = Array(value.utf8)
    writeInteger(UInt64(utf8.count), bytes: 8, into: &data)
    data.append(contentsOf: utf8)
}

func writeMinimalGGUF(metadata: [String: Any]?, to url: URL) throws {
    var data = Data("GGUF".utf8)
    writeInteger(3, bytes: 4, into: &data)
    writeInteger(0, bytes: 8, into: &data) // tensor count; metadata resolution never reads tensors

    let candidates: [(String, String?)] = [
        ("general.architecture", metadata?["architecture"] as? String),
        ("general.name", metadata?["name"] as? String),
        ("tokenizer.chat_template", metadata?["chatTemplate"] as? String),
    ]
    let fields = candidates.compactMap { key, value -> (String, String)? in
        value.map { (key, $0) }
    }
    writeInteger(UInt64(fields.count), bytes: 8, into: &data)
    for (key, value) in fields {
        writeString(key, into: &data)
        writeInteger(8, bytes: 4, into: &data) // GGUF string metadata type
        writeString(value, into: &data)
    }
    try data.write(to: url)
}

let expectedTemplateCodes = corpus["templateIdentifiers"] as! [String]
check(
    LearningChatTemplate.allCases.map(\.rawValue) == expectedTemplateCodes,
    id: "template-identifiers",
    detail: "pinned macOS template identifiers or order changed"
)

for row in corpus["templateAvailability"] as! [[String: Any]] {
    let roleCode = string(row, "role")
    guard let role = LearningModelRole(rawValue: roleCode) else {
        fatalError("unknown model role \(roleCode)")
    }
    let expected = row["expectedTemplates"] as! [String]
    let actual = LearningChatTemplate.available(for: role).map(\.rawValue)
    check(actual == expected, id: string(row, "id"), detail: "available template list differed")
}

for row in corpus["profileCases"] as! [[String: Any]] {
    let id = string(row, "id")
    let requested = requiredTemplate(string(row, "requestedTemplate"))
    let filename = string(row, "modelFileName")
    let metadata = row["metadata"] as? [String: Any]
    let url = temporaryRoot.appendingPathComponent(filename)
    try writeMinimalGGUF(metadata: metadata, to: url)

    do {
        let resolved = try LearningLocalPrompt.resolve(requested, modelURL: url)
        if row["expectedFailure"] as? Bool == true {
            check(false, id: id, detail: "expected metadata/template rejection but resolution succeeded")
            continue
        }
        let family = LearningTranslationPrompt.Family(template: resolved)
        let sampling = LearningInstructionSampling.forTemplate(resolved)
        let expectedSampling = row["expectedSampling"] as! [String: Any]
        check(
            resolved.rawValue == string(row, "expectedTemplate")
                && familyCode(family) == string(row, "expectedFamily")
                && sampling.temperature == expectedSampling["temperature"] as! Double
                && sampling.topP == expectedSampling["topP"] as! Double
                && sampling.topK == expectedSampling["topK"] as! Int
                && sampling.repetitionPenalty == expectedSampling["repetitionPenalty"] as! Double,
            id: id,
            detail: "resolved template, task family, or recommended sampler differed"
        )
    } catch {
        check(
            row["expectedFailure"] as? Bool == true,
            id: id,
            detail: "unexpected template-resolution failure"
        )
    }
}

let literalPayload = corpus["literalPayload"] as! String
for row in corpus["delimiterCases"] as! [[String: Any]] {
    let id = string(row, "id")
    let template = requiredTemplate(string(row, "template"))
    do {
        let (prefix, suffix) = try LearningLocalPrompt.delimiters(template)
        if row["expectedFailure"] as? Bool == true {
            check(false, id: id, detail: "expected unresolved-template rejection but delimiters were returned")
            continue
        }
        let rendered = try LearningLocalPrompt.render(literalPayload, template: template)
        check(
            prefix == string(row, "expectedPrefix")
                && suffix == string(row, "expectedSuffix")
                && rendered == prefix + literalPayload + suffix,
            id: id,
            detail: "trusted delimiters or literal payload differed"
        )
    } catch {
        check(
            row["expectedFailure"] as? Bool == true,
            id: id,
            detail: "unexpected delimiter/render failure"
        )
    }
}

for row in corpus["budgetCases"] as! [[String: Any]] {
    let id = string(row, "id")
    do {
        try LearningLocalPrompt.validateTokenBudget(
            input: integer(row, "inputTokens"),
            output: integer(row, "outputTokens"),
            template: requiredTemplate(string(row, "template")),
            contextWindowTokens: integer(row, "contextWindowTokens")
        )
        check(boolean(row, "expectedAccepted"), id: id, detail: "Mac accepted a rejected token budget")
    } catch {
        check(!boolean(row, "expectedAccepted"), id: id, detail: "Mac rejected an accepted token budget")
    }
}

for row in corpus["contextRecommendations"] as! [[String: Any]] {
    let actual = LearningRuntimeCatalog.defaultTranslationContextWindowTokens(for: string(row, "filename"))
    check(
        actual == optionalInt(row, "expectedRecommendation"),
        id: string(row, "id"),
        detail: "exact-filename context recommendation differed"
    )
}

let snapshot = LearningModelSnapshot(
    backend: .local,
    localModel: "",
    template: .automatic,
    baseURL: "",
    endpointModel: "",
    credentialRevision: 0
)
check(
    snapshot.contextWindowTokens == integer(corpus, "settingsFallbackContextTokens"),
    id: "settings-fallback-context-default",
    detail: "pinned learning settings fallback context changed"
)
check(
    ModelContextBudget.maximumLocalTokens == integer(corpus, "maximumLocalTokens"),
    id: "maximum-local-context",
    detail: "pinned shared local context ceiling changed"
)

let configurationProbe = corpus["configurationProbe"] as! [String: Any]
let explicitConfiguration = LearningLocalModelConfiguration(
    modelURL: temporaryRoot.appendingPathComponent(string(configurationProbe, "modelFileName")),
    template: requiredTemplate(string(configurationProbe, "template")),
    contextWindowTokens: integer(configurationProbe, "contextWindowTokens")
)
check(
    explicitConfiguration.modelURL.lastPathComponent == string(configurationProbe, "modelFileName")
        && explicitConfiguration.template.rawValue == string(configurationProbe, "template")
        && explicitConfiguration.contextWindowTokens == integer(configurationProbe, "contextWindowTokens"),
    id: "explicit-local-configuration",
    detail: "pinned local configuration value shape changed"
)

if failures.isEmpty {
    print("PASS: \(checked) local learning parity checks against macOS \(corpus["macBaselineRevision"] as! String)")
} else {
    for failure in failures { fputs("FAIL: \(failure)\n", stderr) }
    fputs("FAILED: \(failures.count) of \(checked) local learning parity checks\n", stderr)
    exit(1)
}
'''


def git_show(mac_repo: Path, path: str) -> str:
    result = subprocess.run(
        ["git", "-C", str(mac_repo), "show", f"{BASELINE}:{path}"],
        check=True,
        text=True,
        capture_output=True,
    )
    return result.stdout


def extract_braced_declaration(source: str, marker: str) -> str:
    """Return one Swift declaration, ignoring braces in comments and ordinary string literals."""
    start = source.index(marker)
    quote: str | None = None
    escaped = False
    line_comment = False
    block_comment_depth = 0
    brace_depth = 0
    opened = False
    index = start

    while index < len(source):
        character = source[index]
        following = source[index + 1] if index + 1 < len(source) else ""

        if line_comment:
            if character == "\n":
                line_comment = False
            index += 1
            continue
        if block_comment_depth:
            if character == "/" and following == "*":
                block_comment_depth += 1
                index += 2
            elif character == "*" and following == "/":
                block_comment_depth -= 1
                index += 2
            else:
                index += 1
            continue
        if quote is not None:
            if escaped:
                escaped = False
            elif character == "\\":
                escaped = True
            elif source.startswith(quote, index):
                index += len(quote)
                quote = None
                continue
            index += 1
            continue
        if character == "/" and following == "/":
            line_comment = True
            index += 2
            continue
        if character == "/" and following == "*":
            block_comment_depth = 1
            index += 2
            continue
        if source.startswith('"""', index):
            quote = '"""'
            index += 3
            continue
        if character == '"':
            quote = '"'
            index += 1
            continue
        if character == "{":
            brace_depth += 1
            opened = True
        elif character == "}" and opened:
            brace_depth -= 1
            if brace_depth == 0:
                return source[start : index + 1]
        index += 1

    raise ValueError(f"Could not find closing brace for Swift declaration {marker!r}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", required=True, type=Path, help="macOS Git repository containing the pinned commit")
    parser.add_argument(
        "--fixture",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "fixtures" / "parity" / "learning-local-v1.json",
    )
    args = parser.parse_args()
    fixture = args.fixture.resolve()
    corpus = json.loads(fixture.read_text(encoding="utf-8"))
    if corpus.get("schemaVersion") != 1 or corpus.get("macBaselineRevision") != BASELINE:
        parser.error("fixture version or pinned macOS revision does not match this verifier")

    try:
        model_settings = git_show(args.mac_repo, MODEL_SETTINGS)
        pure_settings_start = model_settings.index("nonisolated enum LearningModelRole:")
        pure_settings_end = model_settings.index("@MainActor\nfinal class LearningModelSettings")
        settings_declarations = model_settings[pure_settings_start:pure_settings_end]
        translation_prompt = git_show(args.mac_repo, TRANSLATION_PROMPT)
        family_declaration = extract_braced_declaration(translation_prompt, "    enum Family: Sendable {")
        sampling_declaration = extract_braced_declaration(
            translation_prompt,
            "nonisolated struct LearningInstructionSampling: Sendable {",
        )
        sources = "\n".join(
            [
                STUBS,
                settings_declarations,
                "nonisolated enum LearningTranslationPrompt {\n" + family_declaration + "\n}",
                sampling_declaration,
                git_show(args.mac_repo, LOCAL_CONFIGURATION),
                git_show(args.mac_repo, TOKEN_ESTIMATOR),
                git_show(args.mac_repo, MODEL_CONTEXT_BUDGET),
                git_show(args.mac_repo, LOCAL_PROMPT),
                git_show(args.mac_repo, RUNTIME_CATALOG),
                DRIVER,
            ]
        )
    except (subprocess.CalledProcessError, ValueError) as error:
        parser.error(f"could not read the required pinned macOS sources: {error}")

    with tempfile.TemporaryDirectory(prefix="learning-local-parity-") as temporary:
        directory = Path(temporary)
        source_path = directory / "main.swift"
        binary_path = directory / "learning-local-parity"
        source_path.write_text(sources, encoding="utf-8")
        compile_result = subprocess.run(
            ["xcrun", "swiftc", "-swift-version", "6", str(source_path), "-o", str(binary_path)],
            text=True,
            capture_output=True,
        )
        if compile_result.returncode:
            print(compile_result.stdout, end="")
            print(compile_result.stderr, end="", file=__import__("sys").stderr)
            return compile_result.returncode
        result = subprocess.run(
            [str(binary_path), str(fixture)],
            text=True,
            capture_output=True,
        )
        print(result.stdout, end="")
        print(result.stderr, end="", file=__import__("sys").stderr)
        return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
