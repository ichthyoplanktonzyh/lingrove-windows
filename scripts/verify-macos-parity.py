#!/usr/bin/env python3
"""Run the shared corpus against committed Swift policies without building the Mac app.

The caller supplies a Git checkout containing the pinned Mac revision. Sources are read
with git show, never from that checkout's working files. A temporary executable exercises
the original pure algorithms; a small request DTO supplies only their fixture inputs.
This verifier owns no application state, model, native runtime, or editor connection.
"""

import argparse
import json
from pathlib import Path
import subprocess
import tempfile


BASELINE = "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f"
SOURCES = (
    "Cotabby/Support/Prompting/ControlTokenMarkers.swift",
    "Cotabby/Support/Suggestion/Acceptance/InsertionSafetyGate.swift",
    "Cotabby/Support/Suggestion/Output/CompletionContentPolicy.swift",
    "Cotabby/Support/Suggestion/Output/GhostSpaceBoundary.swift",
    "Cotabby/Support/Suggestion/Output/PromptPrefaceEchoFilter.swift",
    "Cotabby/Support/Suggestion/Output/SuggestionLengthPolicy.swift",
    "Cotabby/Support/Suggestion/Output/SuggestionTextNormalizer.swift",
    "Cotabby/Support/Suggestion/Output/TrailingDuplicationFilter.swift",
    "Cotabby/Support/Suggestion/Request/SentenceBoundaryClassifier.swift",
)
RECONCILER = "Cotabby/Support/Suggestion/Session/SuggestionSessionReconciler.swift"

DRIVER = r'''
import Foundation

// Fixture-only input adapter. The original normalizer reads these values but does
// not need AX identities, app settings or the production request's UI-owned state.
struct SuggestionRequest {
    struct Context { let precedingText: String; let trailingText: String }
    struct WordRange { let lowWords: Int; let highWords: Int }
    let prompt: String
    let prefixText: String
    let context: Context
    let isMultiLineEnabled: Bool
    let wordRange: WordRange? = nil
}

let data = try Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))
let corpus = try JSONSerialization.jsonObject(with: data) as! [String: Any]
var checked = 0
var failures = [String]()

// Top-level execution owns these counters on MainActor in Swift 6; the helper
// shares that isolation rather than making mutable process globals unsafe.
@MainActor func check(_ passed: Bool, id: String, detail: String) {
    checked += 1
    if !passed { failures.append("\(id): \(detail)") }
}

for row in corpus["acceptanceChunks"] as! [[String: Any]] {
    let actual = SuggestionSessionReconciler.nextAcceptanceChunk(
        from: row["tail"] as! String,
        autoAcceptTrailingPunctuation: row["autoAcceptTrailingPunctuation"] as! Bool
    )
    check(actual == row["expectedChunk"] as! String
          && actual.utf16.count == row["expectedUtf16Length"] as! Int,
          id: row["id"] as! String, detail: "actual chunk \(String(reflecting: actual))")
}
for row in corpus["ghostSpaceBoundaries"] as! [[String: Any]] {
    let actual = GhostSpaceBoundary.liveAdjusted(
        row["completion"] as! String,
        precedingText: row["precedingText"] as! String,
        requestPrecedingText: row["requestPrecedingText"] as? String
    )
    check(actual == row["expected"] as! String,
          id: row["id"] as! String, detail: "actual spacing \(String(reflecting: actual))")
}
for row in corpus["normalization"] as! [[String: Any]] {
    let prefix = row["prefixText"] as! String
    let request = SuggestionRequest(
        prompt: row["prompt"] as! String, prefixText: prefix,
        context: .init(precedingText: row["precedingText"] as! String,
                       trailingText: row["trailingText"] as? String ?? ""),
        isMultiLineEnabled: row["isMultiLineEnabled"] as! Bool
    )
    let actual = SuggestionTextNormalizer.normalizeDetailed(row["raw"] as! String, for: request)
    // C# enum names use PascalCase; Swift raw values use lowerCamelCase.
    let expectedReason = (row["expectedSuppression"] as? String).map {
        String($0.prefix(1)).lowercased() + $0.dropFirst()
    }
    let prefixLengthMatches = (row["prefixUtf16Length"] as? Int).map { $0 == prefix.utf16.count } ?? true
    check(actual.text == row["expectedText"] as! String
          && actual.suppression?.rawValue == expectedReason && prefixLengthMatches,
          id: row["id"] as! String,
          detail: "actual normalized text \(String(reflecting: actual.text)), reason \(String(describing: actual.suppression))")
}
if !failures.isEmpty {
    failures.forEach { print("FAIL: \($0)") }
    exit(1)
}
print("Swift macOS baseline: \(checked)/\(checked) shared parity cases passed.")
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
    fixture = root / "fixtures/parity/v1.json"
    corpus = json.loads(fixture.read_text(encoding="utf-8"))
    if corpus.get("schemaVersion") != 1 or corpus.get("macBaselineRevision") != BASELINE:
        parser.error("fixture schema or pinned macOS baseline does not match this verifier")
    ids = set()
    for group in ("acceptanceChunks", "ghostSpaceBoundaries", "normalization"):
        rows = corpus.get(group)
        if not isinstance(rows, list) or not rows:
            parser.error(f"fixture category {group} must contain cases")
        for row in rows:
            case_id = row.get("id")
            if not isinstance(case_id, str) or not case_id or case_id in ids or not row.get("macTest"):
                parser.error("fixture cases require unique nonempty IDs and macOS provenance")
            ids.add(case_id)

    with tempfile.TemporaryDirectory(prefix="lingrove-mac-parity-") as temporary:
        directory = Path(temporary)
        paths = []
        for source in SOURCES:
            target = directory / Path(source).name
            target.write_text(source_at(args.mac_repo, source), encoding="utf-8")
            paths.append(str(target))

        # Acceptance is a pure section of a larger state reconciler. Retain that
        # entire section and its private Character helpers verbatim, omitting only
        # unrelated session/AX methods that would require the whole application.
        reconciler = source_at(args.mac_repo, RECONCILER)
        marker = "    static func nextAcceptanceChunk("
        end_marker = "    static func overlayHideReason("
        helpers_marker = "private extension String {"
        if any(reconciler.count(value) != 1 for value in (marker, end_marker, helpers_marker)):
            raise RuntimeError("pinned acceptance section cannot be identified uniquely")
        target = directory / "SuggestionSessionReconciler.swift"
        target.write_text(
            "import Foundation\nenum SuggestionSessionReconciler {\n"
            + reconciler[reconciler.index(marker):reconciler.index(end_marker)]
            + "}\n" + reconciler[reconciler.index(helpers_marker):], encoding="utf-8"
        )
        paths.append(str(target))
        driver = directory / "main.swift"
        driver.write_text(DRIVER, encoding="utf-8")
        executable = directory / "verify-parity"
        subprocess.run(
            ["xcrun", "swiftc", "-swift-version", "6", *paths, str(driver), "-o", str(executable)],
            check=True,
        )
        subprocess.run([str(executable), str(fixture)], check=True)


if __name__ == "__main__":
    main()
