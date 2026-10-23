#!/usr/bin/env python3
"""Execute original Mac entry-editor closures and content movement on isolated value drafts."""
import argparse
from collections import Counter
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, sense_policy_sources, source


def editor_sources(repo):
    editor = source(repo, 'Cotabby/UI/Vocabulary/VocabularyEntryEditor.swift')
    content = source(repo, 'Cotabby/UI/Vocabulary/VocabularyContentEditor.swift')
    change = between(editor, '                                    example.ranges = []', '                                }\n                            TextField')
    tags = between(editor, '                    value.senses[sense].tags = ', '                    Task {')
    first_disabled = between(editor, '                    .disabled(saving || importingImage', '                    .disabled(draft.senses')
    second_disabled = between(editor, '                    .disabled(draft.senses', '            }.padding()')
    def disabled_expression(value):
        value = value.strip()
        if not value.startswith('.disabled(') or not value.endswith(')'): raise RuntimeError('Pinned Save disable expression changed')
        return value[len('.disabled('):-1]
    first = disabled_expression(first_disabled)
    second = disabled_expression(second_disabled)
    move = between(content, '    private func move(_ id:', '    private func chooseImage()')
    # SwiftUI State permits mutation in a nonmutating view method. A plain value wrapper needs
    # only this effect qualifier; all indexed swap/guard statements remain the original source.
    move = move.replace('private func move(', 'private mutating func move(')
    wrapper = '''import Foundation
struct EditorDraftReference {
    var draft: VocabularyEntry
    var senseID: UUID
    var saving = false
    var importingImage = false
    var blocks: [VocabularyContentBlock] = []
    func parse(_ tags: String) -> [String] {
        var value = draft; let sense = 0
''' + tags + '''
        return value.senses[0].tags
    }
    func canSave() -> Bool { !(''' + first + ''') && !(''' + second + ''') }
    func change(_ original: VocabularyExample, to text: String) -> VocabularyExample {
        var example = original; example.text = text
        // SwiftUI onChange uses Swift value equality; this scheduling guard is a harness input,
        // not a replacement for the original callback's annotation-clearing statements below.
        if original.text != text {
''' + change + '''
        }
        return example
    }
''' + move + '''
    mutating func moveProbe(_ id: UUID, offset: Int) { move(id, by: offset) }
}
'''
    return sense_policy_sources(repo) + [wrapper]


_spec = importlib.util.spec_from_file_location('projection_fixture', Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
_builder = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_builder)
if _builder.DRIVER.count('func describe(') != 1: raise RuntimeError('Fixed graph boundary changed')
DRIVER = _builder.DRIVER[:_builder.DRIVER.index('func describe(')] + r'''
func observe(_ row: [String: Any]) throws -> [String: Any] {
    var entry = document().entries[0]; let operation = row["operation"] as! String
    var reference = EditorDraftReference(draft: entry, senseID: id(2))
    if operation == "tags" { return ["tags": reference.parse(row["text"] as! String)] }
    if operation == "change" {
        var example = entry.senses[0].examples[0]
        example.text = row["before"] as? String ?? example.text
        example.translationLanguage = .chinese; example.translationVariants = [.init(text: "variant", language: .french)]
        example.meaningLanguage = .english; example.meaningOrigin = .user; example.appBundleIdentifier = "source.application"
        let changed = reference.change(example, to: row["text"] as! String)
        let object = try JSONSerialization.jsonObject(with: JSONEncoder().encode(changed))
        return ["example": object]
    }
    if operation == "save" {
        entry.expression = row["expression"] as? String ?? entry.expression
        if row["emptyForm"] as? Bool == true { entry.dictionaryForm = "" }
        if row["emptyContent"] as? Bool == true { entry.senses[0].content = [.init(content: .text(.init()))] }
        if row["noExamples"] as? Bool == true { entry.senses[0].examples = [] }
        else if let text = row["text"] as? String { entry.senses[0].examples[0].text = text }
        reference.draft = entry; reference.saving = row["saving"] as? Bool ?? false; reference.importingImage = row["importing"] as? Bool ?? false
        if row["missingSense"] as? Bool == true { reference.senseID = id(999) }
        return ["enabled": reference.canSave()]
    }
    if operation == "move" {
        reference.blocks = [block(20), .init(id: id(21), content: .image(.init(digest: String(repeating: "a", count: 64), width: 1, height: 1, byteCount: 1), caption: "retain")), block(22)]
        reference.moveProbe(id(row["block"] as! Int), offset: row["offset"] as! Int)
        return ["ids": reference.blocks.map { $0.id.uuidString.lowercased() }, "imageRetained": reference.blocks.contains { if case .image(_, let caption) = $0.content { return caption == "retain" }; return false }]
    }
    fatalError("Unknown editor operation")
}
let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
let observed = try (corpus["cases"] as! [[String: Any]]).map { ["id": $0["id"]!, "expected": try observe($0)] }
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: observed, options: [.sortedKeys]))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-editor-v1.json')
    parser.add_argument('--capture-output', type=Path)
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text()); rows = corpus['cases']; ids = [r.get('id') for r in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or len(set(ids)) != len(ids) or any(not i for i in ids): parser.error('Pinned editor schema is required')
    if Counter(r.get('operation') for r in rows) != {'tags': 8, 'change': 5, 'save': 11, 'move': 5}: parser.error('All 29 original editor observations are required')
    if any(r.get('macSource') != 'Cotabby/UI/Vocabulary/VocabularyEntryEditor.swift' or r.get('macContentSource') != 'Cotabby/UI/Vocabulary/VocabularyContentEditor.swift'
        or r.get('provenanceKind') != 'source-policy-extension' for r in rows): parser.error('Exact editor source provenance is required')
    with tempfile.TemporaryDirectory(prefix='lingrove-editor-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(editor_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER); binary = root / 'editor'
        subprocess.run(['swiftc', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observed = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x') as output: json.dump(observed, output, indent=2, ensure_ascii=False); output.write('\n')
        print(f'Captured {len(observed)} actual Mac editor observations'); return
    if observed != [{'id': r['id'], 'expected': r['expected']} for r in rows]: raise SystemExit('Original editor observation mismatch')
    print(f'Verified {len(observed)} cases against original Mac editor closures')


if __name__ == '__main__': main()
