#!/usr/bin/env python3
"""Execute pinned Mac notebook browse projections, search and sense-title policy."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, notebook_browse_sources

_spec = importlib.util.spec_from_file_location('projection_fixture', Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
_builder = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_builder)
if _builder.DRIVER.count('func describe(') != 1: raise RuntimeError('Fixed graph boundary changed')
DRIVER = _builder.DRIVER[:_builder.DRIVER.index('func describe(')] + r'''
func observe(_ row: [String: Any]) -> [String: Any] {
    var doc = document(); let mode = row["operation"] as! String
    if mode == "highlight" {
        let text = row["text"] as? String ?? String(repeating: "x", count: 12_000) + "bank"
        let ranges = (row["ranges"] as! [[Int]]).map { VocabularyTextRange(NSRange(location: $0[0], length: $0[1])) }
        let example = VocabularyExample(id: id(3), text: text, translation: "", ranges: ranges, completeness: .sentence, meaning: "", createdAt: now)
        return ["runs": NotebookHighlightReference().probe(example)]
    }
    if mode == "matches" {
        let value = row["value"] as? String ?? "Needle"
        switch row["field"] as! String {
        case "expression": doc.entries[0].expression = value
        case "dictionaryForm": doc.entries[0].dictionaryForm = value
        case "meaning": doc.entries[0].senses[0].meaning = value
        case "note": doc.entries[0].senses[0].note = value
        case "tag": doc.entries[0].senses[0].tags = [value]
        case "standalone": doc.entries[0].senses[0].standaloneMeanings = [.init(text: value, language: nil)]
        case "alternate": doc.entries[0].senses[0].alternateMeanings = [.init(text: value, language: nil)]
        case "text": doc.entries[0].senses[0].content = [.init(content: .text(.init(text: value)))]
        case "caption": doc.entries[0].senses[0].content = [.init(content: .image(.init(digest: String(repeating: "a", count: 64), width: 1, height: 1, byteCount: 1), caption: value))]
        case "usageText": doc.entries[0].senses[0].examples[0].text = value
        case "translation": doc.entries[0].senses[0].examples[0].translation = value
        case "usageMeaning": doc.entries[0].senses[0].examples[0].meaning = value
        case "variant": doc.entries[0].senses[0].examples[0].translationVariants = [.init(text: value, language: nil)]
        case "explanation":
            let record = doc.entries[0].senses[0].explanations[0]
            doc.entries[0].senses[0].explanations = [.init(id: record.id, input: record.input, language: record.language,
                result: .init(meaning: value, explanation: value), model: record.model, createdAt: record.createdAt)]
        default: fatalError("Unknown field")
        }
        let query = row["query"] as? String ?? "Needle"
        return ["matches": VocabularyPolicy.matches(doc.entries[0], query: query), "_facts": notebookSearchFacts(doc.entries[0], query: query)]
    }
    if mode == "title" {
        doc.entries[0].senses[0].meaning = ""
        doc.entries[0].senses[0].examples[0].meaning = ""
        let shape = row["shape"] as! String
        switch shape {
        case "meaning": doc.entries[0].senses[0].meaning = "  literal  "
        case "text": doc.entries[0].senses[0].content = [.init(content: .text(.init(text: "  literal  ")))]
        case "usage": doc.entries[0].senses[0].content = []; doc.entries[0].senses[0].examples[0].meaning = "usage"
        case "image": doc.entries[0].senses[0].content = [.init(content: .image(.init(digest: String(repeating: "a", count: 64), width: 1, height: 1, byteCount: 1), caption: ""))]
        case "empty": doc.entries[0].senses[0].content = [.init(content: .text(.init(text: " \n\t")))]
        default: fatalError("Unknown title")
        }
        return ["title": doc.entries[0].senses[0].displayTitle]
    }
    doc.entries[0].senses[0].mergedFrom = [id(99)]
    doc.entries[0].senses[0].tags = [row["firstTag"] as? String ?? "Work"]
    doc.entries[0].senses.append(.init(id: id(4), meaning: "other", tags: ["Home"], createdAt: now, updatedAt: now))
    let second = VocabularySense(id: id(12), meaning: "coffee", tags: ["Work"], createdAt: now, updatedAt: now)
    doc.entries.append(.init(id: id(10), expression: "café", dictionaryForm: "café", language: .french, kind: .word,
        senses: [second], createdAt: now, updatedAt: now.addingTimeInterval(10)))
    var reference = NotebookBrowseReference(store: doc)
    if mode == "selection" {
        if let value = row["sense"] as? Int { reference.selection = id(value) }
        let selected = reference.selectionProbe()
        return ["entry": selected.map { $0.entryID.uuidString.lowercased() } as Any? ?? NSNull(), "sense": selected.map { $0.senseID.uuidString.lowercased() } as Any? ?? NSNull()]
    }
    reference.query = row["query"] as? String ?? ""
    reference.selectedTag = row["tag"] as? String ?? ""
    reference.newestFirst = row["newestFirst"] as? Bool ?? true
    if let language = row["language"] as? String { reference.language = LearningLanguage(rawValue: language)! }
    let visible = reference.visibleProbe()
    return ["entries": visible.map { $0.id.uuidString.lowercased() }, "senses": visible.map { $0.senses.map { $0.id.uuidString.lowercased() } },
        "originalSenses": doc.entries[0].senses.count, "_facts": doc.entries.flatMap { notebookSearchFacts($0, query: reference.query) }]
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let observed = (corpus["cases"] as! [[String: Any]]).map { row -> [String: Any] in
    var result = observe(row); let facts = result.removeValue(forKey: "_facts") ?? []
    return ["id": row["id"]!, "expected": result, "facts": facts]
}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: observed, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-notebook-v1.json')
    parser.add_argument('--capture-output', type=Path)
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text()); rows = corpus['cases']; ids = [r.get('id') for r in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or len(set(ids)) != len(ids) or any(not i for i in ids): parser.error('Notebook cases require pinned unique schema')
    if any(r.get('macSource') != 'Cotabby/UI/Vocabulary/VocabularyNotebookView.swift'
        or r.get('macSecondarySource') != 'Cotabby/Support/Vocabulary/VocabularyPolicy.swift'
        or r.get('macSenseSource') != 'Cotabby/Support/Vocabulary/VocabularySensePolicy.swift'
        or r.get('provenanceKind') != 'source-policy-extension' for r in rows): parser.error('Notebook source provenance is required')
    native = [r for r in rows if r.get('operation') == 'matches']
    if len(native) != 29 or any(type(r.get('windowsTextParity')) is not bool for r in native) or {r['id'] for r in native if not r['windowsTextParity']} != {'zeroInside', 'zeroQuery', 'ligature'}:
        parser.error('The 26 native comparison cases and three documented provider gaps must remain explicit')
    with tempfile.TemporaryDirectory(prefix='lingrove-notebook-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(notebook_browse_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER); binary = root / 'notebook'
        subprocess.run(['swiftc', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observed = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x') as output: json.dump(observed, output, indent=2, ensure_ascii=False); output.write('\n')
        print(f'Captured {len(observed)} actual Mac notebook projections'); return
    if observed != [{'id': r['id'], 'expected': r['expected'], 'facts': r['facts']} for r in rows]: raise SystemExit('Original notebook observation mismatch')
    print(f'Verified {len(observed)} cases against original Mac notebook projections')


if __name__ == '__main__': main()
