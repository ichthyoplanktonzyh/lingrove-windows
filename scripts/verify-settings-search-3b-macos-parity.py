#!/usr/bin/env python3
"""Run the exact committed 3b ranker and Foundation folding against the shared corpus.

No SwiftUI/catalog replacement is compiled. Synthetic items expose the original pure protocol;
same-file extension exposes its unchanged private normalizer solely for boundary observations.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
REVISION = "3b53402f2cbc196b0398d133177fd80006d165b2"
SOURCE = "Lingrove/Support/Settings/SettingsSearchRanker.swift"
DRIVER = r'''import Foundation
struct SearchInput: Decodable, SettingsSearchable {
    let id: String, title: String, keywords: [String], group: String, summary: String
    var searchTitle: String { title }; var searchKeywords: [String] { keywords }
    var searchGroupLabel: String { group }; var searchSummary: String { summary }
}
struct Expected: Decodable { let id: String; let score: Double }
struct SearchCase: Decodable { let id: String, locale: String, query: String; let items: [SearchInput]; let expected: [Expected] }
struct FoldCase: Decodable { let id: String, locale: String, text: String, expected: String }
struct Corpus: Decodable { let cases: [SearchCase]; let folds: [FoldCase] }
@main struct Main {
    static func main() throws {
        let corpus = try JSONDecoder().decode(Corpus.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
        let locale = CommandLine.arguments[2]
        guard Locale.current.identifier == locale else { fatalError("Expected \(locale); actual \(Locale.current.identifier)") }
        var failures = 0, checked = 0
        for row in corpus.cases where row.locale == locale {
            let matches = SettingsSearchRanker.matches(row.query, in: row.items)
            let ids = matches.map { $0.item.id }, scores = matches.map { $0.score }
            if ids != row.expected.map(\.id) || scores != row.expected.map(\.score) {
                print("FAIL search \(row.id): ids=\(ids) scores=\(scores)"); failures += 1
            }
            checked += 1
        }
        for row in corpus.folds where row.locale == locale {
            let actual = SettingsSearchRanker.probeNormalized(row.text)
            if actual != row.expected { print("FAIL fold \(row.id): actual=\(actual.debugDescription) expected=\(row.expected.debugDescription)"); failures += 1 }
            checked += 1
        }
        print("3b settings search \(locale): \(checked) observations, \(failures) failures")
        if failures != 0 { exit(1) }
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, default=ROOT / "fixtures/parity/settings-search-3b-v1.json")
    parser.add_argument("--verify-source-only", action="store_true", help="Check committed pin/hash and corpus shape without Swift (also available on Windows)")
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding="utf-8-sig"))
    source = subprocess.check_output(["git", "-C", str(args.mac_repo), "show", REVISION + ":" + SOURCE])
    if corpus["version"] != 1 or corpus["macReference"] != REVISION or hashlib.sha256(source).hexdigest() != corpus["sourceSha256"]:
        raise SystemExit("Settings-search reference/hash changed")
    if not corpus["cases"] or not corpus["folds"]:
        raise SystemExit("Missing settings-search observations")
    if args.verify_source_only:
        print("Verified exact 3b settings-search source/hash; Swift behavior not executed")
        return
    with tempfile.TemporaryDirectory(prefix="lingrove-settings-search-") as temporary:
        directory = Path(temporary)
        policy = directory / "Policy.swift"
        policy.write_bytes(source + b'\nextension SettingsSearchRanker { static func probeNormalized(_ value: String) -> String { normalize(value) } }\n')
        driver = directory / "Main.swift"; driver.write_text(DRIVER, encoding="utf-8")
        executable = directory / "settings-search"
        subprocess.run(["swiftc", str(policy), str(driver), "-o", str(executable)], check=True, timeout=120)
        failed = False
        for locale in sorted({row["locale"] for row in corpus["cases"] + corpus["folds"]}):
            result = subprocess.run([str(executable), str(args.fixture.resolve()), locale, "-AppleLocale", locale], timeout=30)
            failed |= result.returncode != 0
        if failed:
            raise SystemExit("Original Mac settings-search observations failed")


if __name__ == "__main__":
    main()
