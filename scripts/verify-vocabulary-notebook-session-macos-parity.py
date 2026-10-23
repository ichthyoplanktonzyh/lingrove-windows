#!/usr/bin/env python3
"""Execute actual original notebook, explanation and dictionary sessions with provider-only probes."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE
from macos_notebook_session_reference import notebook_session_sources

OPERATIONS = ['first', 'missing', 'alias', 'second', 'same', 'note', 'context', 'remove', 'standalone', 'no-standalone', 'nil',
    'saved', 'wrong-language', 'activate', 'cancel', 'dictionary-open', 'dictionary-repeat', 'dictionary-hide', 'dictionary-resume',
    'dictionary-form', 'dictionary-language', 'dictionary-empty-form', 'dictionary-pattern', 'dictionary-canonical']
_spec = importlib.util.spec_from_file_location('projection_fixture', Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
_builder = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_builder)
if _builder.DRIVER.count('func describe(') != 1: raise RuntimeError('Fixed graph boundary changed')
DRIVER = _builder.DRIVER[:_builder.DRIVER.index('func describe(')] + r'''
import Combine
actor DictionaryProbe: LearningDictionaryProviding {
    struct Request: Codable, Sendable { let query: String; let language: LearningLanguage; let expression: Bool }
    private(set) var calls = 0
    private(set) var requests: [Request] = []
    func lookup(_ query: String, language: LearningLanguage, expression: Bool, source: LearningDictionarySource,
                configuration: LearningDictionaryConfiguration) async throws -> LearningDictionaryResult {
        calls += 1
        requests.append(.init(query: query, language: language, expression: expression))
        return .entries([.init(id: "fixture", language: language.rawValue, headword: query, forms: [], senses: [], sourceURL: "")])
    }
}
@MainActor final class ExplanationProbe: LearningExplanationGenerating {
    let changed = PassthroughSubject<Void, Never>()
    var changes: AnyPublisher<Void, Never> { changed.eraseToAnyPublisher() }
    var language: LearningLanguage = .english
    var isConfigured = true
    private(set) var calls = 0
    private(set) var displays = 0
    private var pending: [(LearningExplanationInput, CheckedContinuation<LearningExplanationRecord, Never>)] = []
    func generate(_ input: LearningExplanationInput, requestID: String, refresh: Bool) async throws -> LearningExplanationRecord {
        calls += 1
        return await withCheckedContinuation { pending.append((input, $0)) }
    }
    func recordDisplay(requestID: String) { displays += 1 }
    func release() {
        let work = pending; pending = []
        for (input, continuation) in work {
            continuation.resume(returning: .init(id: id(200), input: input, language: language,
                result: .init(meaning: "generated", explanation: "prose"), model: "fixture", createdAt: now))
        }
    }
}
@MainActor func settle(_ condition: () async -> Bool) async {
    let deadline = ContinuousClock.now + .seconds(5)
    while !(await condition()) {
        if ContinuousClock.now >= deadline { fatalError("Original session did not settle") }
        await Task.yield()
    }
}
func object<T: Encodable>(_ value: T) throws -> Any { try JSONSerialization.jsonObject(with: JSONEncoder().encode(value)) }
func copied(_ entry: VocabularyEntry, language: LearningLanguage? = nil, kind: VocabularyEntry.Kind? = nil) -> VocabularyEntry {
    .init(id: entry.id, expression: entry.expression, dictionaryForm: entry.dictionaryForm, language: language ?? entry.language,
        kind: kind ?? entry.kind, senses: entry.senses, createdAt: entry.createdAt, updatedAt: entry.updatedAt)
}
@MainActor func observe(_ mode: String) async throws -> [String: Any] {
    var entry = document().entries[0]
    entry.senses[0].mergedFrom = [id(99)]
    var other = entry.senses[0].examples[0]
    other = VocabularyExample(id: id(6), text: "My bank.", translation: "", ranges: [.init(NSRange(location: 3, length: 4))], completeness: .sentence, meaning: "", createdAt: now)
    entry.senses.append(.init(id: id(4), meaning: "other", examples: [other], createdAt: now, updatedAt: now))
    if mode == "no-standalone" { entry.senses[0].isSavedWithoutContext = false }
    if mode == "standalone" { entry.senses[0].examples = [] }
    if mode == "dictionary-empty-form" { entry.dictionaryForm = "" }
    if mode == "dictionary-pattern" { entry = copied(entry, kind: .pattern); entry.expression = "keep me posted"; entry.dictionaryForm = "keep someone posted"; entry.senses[0].examples = [] }
    if mode == "dictionary-canonical" { entry.dictionaryForm = "café" }
    let provider = DictionaryProbe(), service = ExplanationProbe()
    let learning = VocabularyNotebookSession(service: service, provider: provider)
    var facts: [[String: Any]] = []
    func show(_ value: VocabularyEntry) {
        let query = value.dictionaryForm.isEmpty ? value.expression : value.dictionaryForm
        facts.append(["query": query, "language": value.language.rawValue,
            "words": LearningUnitPolicy.words(in: query, language: value.language).map { ["text": $0.text, "location": $0.range.location, "length": $0.range.length] }])
        learning.showDictionary(value)
    }
    var reused: Bool?; var priorStopped: Bool?
    if mode == "saved" || mode == "wrong-language" {
        let input = LearningExplanationPolicy.input(LearningExplanationPolicy.capture(entry, sense: entry.senses[0], example: entry.senses[0].examples[0]))
        let english = LearningExplanationRecord(id: id(201), input: input, language: .english, result: .init(meaning: "saved", explanation: "saved prose"), model: "fixture", createdAt: now)
        let wrong = LearningExplanationRecord(id: id(202), input: input, language: .chinese, result: .init(meaning: "wrong", explanation: "wrong prose"), model: "fixture", createdAt: now)
        entry.senses[0].examples[0].explanations = mode == "saved" ? [english, wrong] : [wrong]
    }
    learning.select(entry, senseID: mode == "missing" ? id(999) : mode == "alias" ? id(99) : id(2))
    let first = learning.explanations[id(3).uuidString]
    var earlier: Task<Void, Never>?
    if ["second", "remove", "context", "note"].contains(mode) {
        first!.explain(); await settle { service.calls == 1 }; earlier = first!.generationProbe()
    }
    switch mode {
    case "second": learning.select(entry, senseID: id(4)); priorStopped = first?.isGenerating == false
    case "same": learning.select(entry, senseID: id(2)); reused = first === learning.explanations[id(3).uuidString]
    case "note": entry.senses[0].note = "changed"; learning.select(entry, senseID: id(2)); reused = first === learning.explanations[id(3).uuidString]
    case "context": entry.senses[0].examples[0].translation = "changed annotation"; entry.senses[0].examples[0].text = "The bank opens."; learning.select(entry, senseID: id(2)); reused = first === learning.explanations[id(3).uuidString]
    case "remove": entry.senses[0].examples = []; learning.select(entry, senseID: id(2)); priorStopped = first?.isGenerating == false
    case "nil": learning.select(nil)
    case "activate":
        first!.explain(); await settle { service.calls == 1 }
        let retired = first!.generationProbe()
        learning.activateExplanation("standalone"); priorStopped = !first!.isGenerating
        learning.explanations["standalone"]!.explain(); await settle { service.calls == 2 }
        let active = learning.explanations["standalone"]!.generationProbe()
        service.release(); await retired?.value; await active?.value
    case "cancel":
        first!.explain(); await settle { service.calls == 1 }; let retired = first!.generationProbe()
        learning.cancel(); service.release(); await retired?.value
        priorStopped = !first!.isGenerating
    default: break
    }
    if ["second", "remove", "context"].contains(mode) { service.release(); await earlier?.value }
    if mode.hasPrefix("dictionary-") {
        show(entry); await settle { learning.dictionary.content != .loading }
        switch mode {
        case "dictionary-repeat": show(entry)
        case "dictionary-hide": learning.hideDictionary()
        case "dictionary-resume": learning.cancel(); learning.resume(); await settle { learning.dictionary.content != .loading }
        case "dictionary-form": entry.dictionaryForm = "riverbank"; learning.select(entry, senseID: id(2)); show(entry); await settle { learning.dictionary.content != .loading }
        case "dictionary-language": entry = copied(entry, language: .french); learning.select(entry, senseID: id(2))
        case "dictionary-canonical": entry.dictionaryForm = "cafe\u{301}"; learning.select(entry, senseID: id(2)); show(entry)
        default: break
        }
    }
    let rows = try learning.explanations.keys.sorted().map { key -> [String: Any] in
        let session = learning.explanations[key]!
        return ["key": key, "input": try session.input.map(object) as Any? ?? NSNull(),
            "meaning": session.record?.result.meaning as Any? ?? NSNull(), "generating": session.isGenerating]
    }
    var dictionary: [String: Any] = ["open": learning.dictionary.unit != nil, "query": learning.dictionary.query]
    if let unit = learning.dictionary.unit {
        dictionary["unit"] = ["id": unit.id, "surface": unit.surface, "query": unit.query, "kind": unit.kind.rawValue,
            "ranges": unit.ranges.map { [$0.location, $0.length] }, "words": unit.words.map { ["text": $0.text, "location": $0.range.location, "length": $0.range.length] }]
    } else { dictionary["unit"] = NSNull() }
    let output: [String: Any] = ["entry": learning.entryID?.uuidString.lowercased() as Any? ?? NSNull(),
        "sense": learning.senseID?.uuidString.lowercased() as Any? ?? NSNull(), "rows": rows, "dictionary": dictionary,
        "dictionaryCalls": await provider.calls, "dictionaryRequests": try (await provider.requests).map(object), "generations": service.calls, "displays": service.displays,
        "reused": reused as Any? ?? NSNull(), "priorStopped": priorStopped as Any? ?? NSNull(), "_facts": facts]
    learning.cancel(); service.release(); await earlier?.value
    return output
}
@main struct Main {
    @MainActor static func main() async throws {
        let corpus = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))) as! [String: Any]
        var results: [[String: Any]] = []
        for row in corpus["cases"] as! [[String: Any]] {
            var observed = try await observe(row["operation"] as! String); let facts = observed.removeValue(forKey: "_facts")!
            results.append(["id": row["id"]!, "expected": observed, "facts": facts])
        }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: results, options: [.sortedKeys]))
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-notebook-session-v1.json')
    parser.add_argument('--capture-output', type=Path)
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text()); rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or [r.get('id') for r in rows] != OPERATIONS or [r.get('operation') for r in rows] != OPERATIONS:
        parser.error('All 24 pinned original-session probes are required')
    if any(r.get('macSource') != 'Cotabby/Services/Vocabulary/VocabularyNotebookSession.swift' or r.get('provenanceKind') != 'source-policy-extension'
        or r.get('macChildSources') != ['Cotabby/Services/LanguageLearning/LearningExplanationSession.swift', 'Cotabby/Services/LanguageLearning/LearningDictionarySession.swift'] for r in rows):
        parser.error('Original notebook session provenance is required')
    with tempfile.TemporaryDirectory(prefix='lingrove-notebook-session-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(notebook_session_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER); binary = root / 'notebook-session'
        subprocess.run(['swiftc', '-O', '-parse-as-library', *inputs, str(driver), '-o', str(binary)], check=True)
        observed = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x') as output: json.dump(observed, output, indent=2, ensure_ascii=False); output.write('\n')
        print(f'Captured {len(observed)} original Mac notebook/child session observations'); return
    if observed != [{'id': r['id'], 'expected': r['expected'], 'facts': r['facts']} for r in rows]: raise SystemExit('Original notebook session mismatch')
    print(f'Verified {len(observed)} cases against actual original Mac notebook/child sessions')


if __name__ == '__main__': main()
