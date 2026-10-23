#!/usr/bin/env python3
"""Execute pinned Mac sense-content values/validation/HTML against shared data-only fixtures.

No archive, image decoder, model, editor or Anki connection is involved. The driver projects
fixture text specs into actual Mac content values; exercised algorithms come only from Git.
"""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

BASELINE = "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f"
CONTENT = "Cotabby/Support/Vocabulary/VocabularyContentPolicy.swift"
TESTS = "CotabbyTests/Services/Vocabulary/VocabularyContentTests.swift"

DRIVER = r'''
import Foundation
import CryptoKit

// Decode native Swift strings to preserve literal BOM/decomposed scalars, including in captions.
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
struct Block: Decodable {
    let id: UUID
    let kind: String
    let text: TextSpec?
    let style: VocabularyTextContent.Style?
    let bold: Bool?
    let italic: Bool?
    let digest: String?
    let width: Int?
    let height: Int?
    let byteCount: Int?
    let caption: TextSpec?
    func value() -> VocabularyContentBlock {
        let content: VocabularyContentBlock.Content
        if kind == "text" {
            var value = VocabularyTextContent()
            if let text { value.text = text.value }
            if let style { value.style = style }
            if let bold { value.bold = bold }
            if let italic { value.italic = italic }
            content = .text(value)
        } else {
            precondition(kind == "image")
            content = .image(.init(digest: digest!, width: width!, height: height!, byteCount: byteCount!), caption: caption?.value ?? "")
        }
        return .init(id: id, content: content)
    }
}
struct Row: Decodable { let id: String; let blocks: [Block] }
struct Corpus: Decodable { let cases: [Row] }
struct Observation: Encodable {
    let valid: Bool
    let meaningful: Bool
    let images: [String]
    let html: String?
    let htmlBytes: Int?
    let htmlSHA256: String?
    enum CodingKeys: String, CodingKey { case valid, meaningful, images, html, htmlBytes, htmlSHA256 }
    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(valid, forKey: .valid); try c.encode(meaningful, forKey: .meaningful); try c.encode(images, forKey: .images)
        try c.encode(html, forKey: .html); try c.encode(htmlBytes, forKey: .htmlBytes); try c.encode(htmlSHA256, forKey: .htmlSHA256)
    }
}
struct Result: Encodable { let id: String; let expected: Observation }
let corpus = try JSONDecoder().decode(Corpus.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
let results = corpus.cases.map { row in
    let blocks = row.blocks.map { $0.value() }
    let valid: Bool
    do { try VocabularyContentPolicy.validate(blocks); valid = true } catch { valid = false }
    // Mac html expects validation at its callers. Never render a rejected image filename here.
    let html = valid ? VocabularyContentPolicy.html(blocks) : nil
    let bytes = html.map { Data($0.utf8) }
    return Result(id: row.id, expected: Observation(valid: valid, meaningful: VocabularyContentPolicy.meaningful(blocks),
        images: VocabularyContentPolicy.images(blocks).map(\.filename),
        html: html.flatMap { $0.utf8.count <= 2_048 ? $0 : nil }, htmlBytes: bytes?.count,
        htmlSHA256: bytes.map { SHA256.hash(data: $0).map { String(format: "%02x", $0) }.joined() }))
}
let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]
FileHandle.standardOutput.write(try encoder.encode(results))
'''


def source(repo, path):
    return subprocess.check_output(["git", "-C", str(repo), "show", f"{BASELINE}:{path}"], text=True)


def before(value, marker):
    if value.count(marker) != 1:
        raise RuntimeError("Pinned declaration marker must be unique")
    return value[:value.index(marker)]


def between(value, start, end):
    if value.count(start) != 1 or value.count(end) != 1:
        raise RuntimeError("Pinned declaration boundaries must be unique")
    return value[value.index(start):value.index(end)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, default=Path(__file__).resolve().parents[1] / "fixtures/parity/vocabulary-content-v1.json")
    parser.add_argument("--capture-output", type=Path, help="Author actual observations into a NEW file; normal checks never update expectations")
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding="utf-8"))
    rows = corpus["cases"]
    ids = [row.get("id") for row in rows]
    if (corpus.get("version") != 1 or corpus.get("macReference") != BASELINE or not rows
            or any(not value for value in ids) or len(set(ids)) != len(ids)):
        parser.error("Content cases require pinned version and unique IDs")
    # Tests are provenance only. All behavior comes from the production declarations below.
    tests = source(args.mac_repo, TESTS)
    for row in rows:
        if (row.get("macSource") != CONTENT or row.get("provenanceKind") not in ("regression-adaptation", "source-policy-extension")
                or (row["provenanceKind"] == "regression-adaptation" and (row.get("macTest") != TESTS
                    or "func " + row.get("macTestMethod", "?") + "(" not in tests))):
            parser.error("Content cases require explicit source/test provenance")
    models = before(source(args.mac_repo, "Cotabby/Models/Vocabulary/VocabularyContentModels.swift"), "/// A portable, versioned single-file backup")
    error = source(args.mac_repo, "Cotabby/Support/Vocabulary/VocabularyPolicy.swift")
    error = "import Foundation\n" + error[error.index("nonisolated enum VocabularyError:"):]
    review = source(args.mac_repo, "Cotabby/Support/Review/ReviewCardPolicy.swift")
    escaping = "import Foundation\nnonisolated enum ReviewCardPolicy {\n" + between(review, "    static func html(_ text:", "    static func sentenceHTML(") + "}\n"
    # Complete content declarations/policy + exact original escaping and error declarations.
    # Backup, entry/sense/review graphs are excluded, never replaced with fake implementations.
    with tempfile.TemporaryDirectory(prefix="lingrove-content-parity-") as temporary:
        root = Path(temporary); paths = []
        for index, value in enumerate([models, source(args.mac_repo, CONTENT), escaping, error]):
            path = root / f"source{index}.swift"; path.write_text(value, encoding="utf-8"); paths.append(str(path))
        driver = root / "main.swift"; driver.write_text(DRIVER, encoding="utf-8")
        binary = root / "content-parity"
        subprocess.run(["swiftc", "-swift-version", "6", "-O", *paths, str(driver), "-o", str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open("x", encoding="utf-8") as output:
            json.dump(observations, output, ensure_ascii=False, indent=2); output.write("\n")
        print(f"Captured {len(observations)} actual Mac sense-content observations")
        return
    if len(observations) != len(rows):
        raise SystemExit("Content observation count changed")
    for row, observation in zip(rows, observations):
        if observation != {"id": row["id"], "expected": row["expected"]}:
            raise SystemExit(f"Mac content mismatch: {row['id']}")
    print(f"Verified {len(observations)} sense-content cases against actual pinned Mac values/policy/HTML")


if __name__ == "__main__":
    main()
