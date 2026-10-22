#!/usr/bin/env python3
"""Compare lexical snapshots with the actual pinned Mac recognizer and NLTokenizer.

Only pure declaration slices are extracted to omit UI anchors and unrelated dictionary APIs.
No policy is stubbed. Inputs are synthetic dictionary entries; no dictionary/model is downloaded.
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
    "Cotabby/Support/LanguageLearning/LearningUnitRecognizer.swift",
]
UNIT_MODELS = "Cotabby/Models/LanguageLearning/LearningUnitModels.swift"
WORD_MODELS = "Cotabby/Models/LanguageLearning/LearningDictionaryWord.swift"

DRIVER = r'''
import Foundation

// This executable only projects the committed policy's values into JSON. String keys are
// deduplicated by their UTF-8 bytes: Swift String equality itself treats NFC/NFD as equivalent.
func rangeJSON(_ range: NSRange) -> [String: Int] {
    ["location": range.location, "length": range.length]
}
func wordJSON(_ word: LearningDictionaryWord) -> [String: Any] {
    ["text": word.text, "range": rangeJSON(word.range)]
}
func unitJSON(_ unit: LearningUnit) -> [String: Any] {
    ["id": unit.id, "surface": unit.surface, "query": unit.query,
     "kind": unit.kind.rawValue, "ranges": unit.ranges.map(rangeJSON),
     "words": unit.words.map(wordJSON), "entryID": unit.entryID as Any? ?? NSNull()]
}
let data = try Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))
let corpus = try JSONSerialization.jsonObject(with: data) as! [String: Any]
var observations = [[String: Any]]()
for row in corpus["cases"] as! [[String: Any]] {
    let text = row["text"] as! String
    let language = LearningLanguage(rawValue: row["language"] as! String)!
    let entries = (row["entries"] as! [[String: Any]]).map { item in
        LearningLexiconEntry(id: item["id"] as! String,
            language: item["language"] as! String, headword: item["headword"] as! String,
            forms: item["forms"] as! [String], senses: [], sourceURL: "https://example.invalid/fixture")
    }
    let recognizer = LearningUnitRecognizer(entries: entries)
    let words = LearningUnitPolicy.words(in: text, language: language)
    let units = recognizer.recognize(text, language: language)
    let queries = row["queries"] as! [String]
    var inputs = entries.flatMap { [$0.headword] + $0.forms } + queries + words.map(\.text)
    let value = text as NSString
    for start in words.indices {
        for end in start..<words.count {
            let range = NSRange(location: words[start].range.location,
                length: NSMaxRange(words[end].range) - words[start].range.location)
            inputs.append(value.substring(with: range))
        }
    }
    var seen = Set<Data>()
    let facts: [[String: Any]] = inputs.filter { seen.insert(Data($0.utf8)).inserted }.map { input in
        ["text": input, "key": LearningUnitPolicy.key(input),
         "patternText": input.allSatisfy { $0.isLetter || $0.isWhitespace || $0 == "'" || $0 == "’" },
         "firstUppercase": input.first?.isUppercase == true]
    }
    let hits: [[String: Any]] = (row["hitLocations"] as! [Int]).map { location in
        let unit = LearningUnitPolicy.unit(at: location, units: units)
        return ["location": location, "id": unit?.id as Any? ?? NSNull(),
                "kind": unit?.kind.rawValue as Any? ?? NSNull()]
    }
    let lookups: [[String: Any]] = queries.map {
        ["query": $0, "ids": recognizer.lookup($0).map(\.id)]
    }
    observations.append(["id": row["id"] as! String, "words": words.map(wordJSON),
        "units": units.map(unitJSON), "facts": facts, "hits": hits, "lookups": lookups])
}
let result = try JSONSerialization.data(withJSONObject: observations, options: [.sortedKeys])
FileHandle.standardOutput.write(result)
'''

def source_at(repo: Path, path: str) -> str:
    return subprocess.check_output(["git", "-C", str(repo), "show", f"{BASELINE}:{path}"], text=True)

def between(source: str, start: str, end: str) -> str:
    if source.count(start) != 1 or source.count(end) != 1:
        raise RuntimeError("Pinned pure declaration boundaries are not unique")
    return source[source.index(start):source.index(end)]

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", type=Path, required=True)
    parser.add_argument("--fixture", type=Path,
        default=Path(__file__).resolve().parents[1] / "fixtures/parity/learning-lexical-v1.json")
    parser.add_argument("--capture-output", type=Path,
        help="Write reference observations to a new file for explicit fixture authoring, without overwriting it")
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding="utf-8"))
    if corpus.get("macReference") != BASELINE:
        parser.error("Fixture must name the pinned Mac commit")
    if corpus.get("version") != 1:
        parser.error("Unsupported lexical fixture version")
    if any(type(row.get("nativeWordParity")) is not bool or
           (row["nativeWordParity"] and row.get("language") not in ("en", "es", "fr"))
           for row in corpus["cases"]):
        parser.error("Native word parity must be explicit and is scoped to en/es/fr cases")
    ids = [row.get("id") for row in corpus["cases"]]
    if any(not value for value in ids) or len(set(ids)) != len(ids):
        parser.error("Fixture IDs must be nonempty and unique")
    if any(not row.get("macSource") or row.get("provenanceKind") not in
           ("regression-adaptation", "source-policy-extension") for row in corpus["cases"]):
        parser.error("Every case requires explicit Mac source/provenance classification")
    if any(row["provenanceKind"] == "regression-adaptation" and not row.get("macTest")
           for row in corpus["cases"]):
        parser.error("Regression adaptations require the corresponding Mac test path")
    with tempfile.TemporaryDirectory(prefix="lingrove-lexical-parity-") as temporary:
        directory = Path(temporary)
        sources = []
        for path in SOURCES:
            target = directory / Path(path).name
            target.write_text(source_at(args.mac_repo, path), encoding="utf-8")
            sources.append(str(target))
        models = source_at(args.mac_repo, UNIT_MODELS)
        word = source_at(args.mac_repo, WORD_MODELS)
        pure_models = "import Foundation\n" + between(models,
            "nonisolated struct LearningUnit:", "nonisolated enum LearningUnitStage:")
        pure_models += between(models, "nonisolated struct LearningLexiconEntry:",
            "nonisolated struct LearningLexiconResource:")
        pure_models += between(word, "nonisolated struct LearningDictionaryWord:", "/// View-local geometry")
        target = directory / "LexicalValues.swift"
        target.write_text(pure_models, encoding="utf-8")
        sources.append(str(target))
        driver = directory / "main.swift"
        driver.write_text(DRIVER, encoding="utf-8")
        executable = directory / "lexical-parity"
        subprocess.run(["xcrun", "swiftc", "-swift-version", "6", *sources, str(driver), "-o", str(executable)], check=True)
        actual = json.loads(subprocess.check_output([str(executable), str(args.fixture)], text=True))
    if args.capture_output:
        with args.capture_output.open("x", encoding="utf-8") as output:
            json.dump(actual, output, ensure_ascii=False, indent=2)
        print(f"Captured {len(actual)} pinned Mac lexical observations.")
        return
    if len(actual) != len(corpus["cases"]):
        raise SystemExit("FAIL: pinned Mac case count differs from the fixture")
    for row, observed in zip(corpus["cases"], actual):
        expected = row.get("expected")
        if expected != observed:
            raise SystemExit(f"FAIL {row['id']}: actual pinned Mac observation differs from the fixture")
    print(f"PASS: {len(actual)} lexical parity cases against macOS {BASELINE}")

if __name__ == "__main__":
    main()
