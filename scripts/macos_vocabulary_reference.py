"""Read-only pinned Swift declaration inputs shared by vocabulary parity executables.

This module supplies source text only, never an implementation or expected result. Each driver
compiles these actual declarations in a temporary binary and chooses its own observable scope.
"""
import subprocess

BASELINE = "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f"


def source(repo, path):
    return subprocess.check_output(["git", "-C", str(repo), "show", f"{BASELINE}:{path}"], text=True)


def before(value, marker):
    if value.count(marker) != 1: raise RuntimeError("Pinned declaration marker must be unique")
    return value[:value.index(marker)]


def between(value, start, end):
    if value.count(start) != 1 or value.count(end) != 1:
        raise RuntimeError("Pinned declaration boundaries must be unique")
    return value[value.index(start):value.index(end)]


def model_sources(repo):
    paths = ["Cotabby/Models/Context/LanguageCatalog.swift", "Cotabby/Models/LanguageLearning/LearningLanguage.swift",
        "Cotabby/Models/Vocabulary/VocabularyModels.swift", "Cotabby/Models/Vocabulary/VocabularyContentModels.swift",
        "Cotabby/Models/Review/ReviewModels.swift", "Cotabby/Models/Review/ReviewLayout.swift"]
    values = [source(repo, path) for path in paths]
    explanation = source(repo, "Cotabby/Models/LanguageLearning/LearningExplanationModels.swift")
    values.append("import Foundation\n" + between(explanation,
        "nonisolated enum LearningExplanationContext:", "/// UI sessions borrow this app-owned boundary."))
    values.append(before(source(repo, "Cotabby/Models/Review/ReviewAudioModels.swift"),
        "/// A preparation sheet retains only its explicitly generated audio."))
    merge = source(repo, "Cotabby/Support/Review/AnkiReviewPolicy.swift")
    values.append("import Foundation\nnonisolated enum AnkiReviewPolicy {\n" +
        between(merge, "    static func merged(", "\n}\n") + "\n}\n")
    return values


def sense_policy_sources(repo):
    paths = ["Cotabby/Support/Vocabulary/VocabularyPolicy.swift", "Cotabby/Support/Vocabulary/VocabularySensePolicy.swift",
        "Cotabby/Support/Vocabulary/VocabularyContentPolicy.swift", "Cotabby/Support/Prompting/TokenCountEstimator.swift",
        "Cotabby/Support/Prompting/ModelContextBudget.swift", "Cotabby/Support/LanguageLearning/LearningFailure.swift",
        "Cotabby/Support/Suggestion/Acceptance/InsertionSafetyGate.swift"]
    values = model_sources(repo) + [source(repo, path) for path in paths]
    explanation = source(repo, "Cotabby/Support/LanguageLearning/LearningExplanationPolicy.swift")
    values.append("import Foundation\nnonisolated enum LearningExplanationPolicy {\n" +
        between(explanation, "    static func input(_ capture:", "    static func input(_ presentation:") +
        between(explanation, "    static func isValid(_ input:", "    static func prompt(") +
        between(explanation, "    static func isValid(_ result:", "    static func parse(") + "}\n")
    review = source(repo, "Cotabby/Support/Review/ReviewCardPolicy.swift")
    values.append("import Foundation\nnonisolated enum ReviewCardPolicy {\n" +
        between(review, "    static func html(_ text:", "    static func sentenceHTML(") + "}\n")
    return values


def archive_validation_sources(repo):
    """Original validator method bodies in a value wrapper; no actor/storage implementation."""
    values = sense_policy_sources(repo)
    archive = source(repo, "Cotabby/Services/Vocabulary/VocabularyArchive.swift")
    cards = source(repo, "Cotabby/Services/Vocabulary/VocabularyArchive+ReviewCards.swift")
    media = source(repo, "Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift")
    values.append("import Foundation\nnonisolated struct VocabularyArchive {\n" +
        between(archive, "    func validate(_ document:", "    private static func encoded(") +
        between(cards, "    func validateCards(", "    func removeReview(") +
        between(cards, "    func validateTemplates(", "\n}\n") + "\n" +
        between(media, "    func validateBlockIdentities(", "    static let maximumBackupBytes") + "}\n")
    layout = source(repo, "Cotabby/Support/Review/ReviewLayoutPolicy.swift")
    endpoint = source(repo, "Cotabby/Support/Review/AnkiReviewPolicy.swift")
    speech = source(repo, "Cotabby/Support/Speech/SpeechExportPolicy.swift")
    values += ["import Foundation\nnonisolated enum ReviewLayoutPolicy {\n" +
        between(layout, "    static func validate(", "    static func portable(") + "}\n",
        "import Foundation\nextension AnkiReviewPolicy {\n" +
        between(endpoint, "    static func endpoint(", "    static func fields(") + "}\n",
        "import Foundation\nnonisolated enum SpeechExportPolicy {\n" +
        between(speech, "    static func validFilename(", "    static func wav(") + "}\n"]
    return values


def archive_edit_sources(repo):
    """Run original private merge helpers and commit's exact pruning loop, without storage fakes."""
    archive = source(repo, "Cotabby/Services/Vocabulary/VocabularyArchive.swift")
    wrapper = "import Foundation\nnonisolated struct VocabularyArchiveEditReference {\n" + between(archive,
        "    private func mergeSenses(", "    func remove(id:")
    wrapper += "\n    func mergeProbe(_ baseline: [VocabularySense], _ current: [VocabularySense], _ edited: [VocabularySense]) throws -> [VocabularySense] {\n"
    wrapper += "        try mergeSenses(baseline: baseline, current: current, edited: edited)\n    }\n"
    wrapper += "    func pruneProbe(_ document: VocabularyDocument) -> VocabularyDocument {\n        var next = document\n"
    wrapper += between(archive, "        for index in next.entries.indices {", "        try validate(next)")
    wrapper += "        return next\n    }\n}\n"
    return sense_policy_sources(repo) + [wrapper]


def database_projection_sources(repo):
    return sense_policy_sources(repo) + [source(repo, path) for path in (
        "Cotabby/Models/Vocabulary/VocabularyDatabaseModels.swift",
        "Cotabby/Support/Vocabulary/VocabularyDatabaseProjection.swift")]


def database_sqlite_sources(repo):
    """Complete original SQLite implementation, removing only two logging calls/import."""
    connection = source(repo, "Cotabby/Services/Vocabulary/VocabularySQLiteConnection.swift")
    if connection.count('import Logging\n') != 1 or connection.count('CotabbyLogger.learning.error(') != 2:
        raise RuntimeError('Pinned SQLite logging boundary changed')
    connection = '\n'.join(line for line in connection.split('\n')
        if line != 'import Logging' and 'CotabbyLogger.learning.error(' not in line)
    return database_projection_sources(repo) + [connection] + [source(repo, path) for path in (
        "Cotabby/Models/Vocabulary/VocabularyCLIReceipt.swift",
        "Cotabby/Services/Vocabulary/VocabularySQLiteFileLock.swift",
        "Cotabby/Services/Vocabulary/VocabularySQLiteDatabase.swift")]


def database_archive_sources(repo):
    """Complete real archive/media classes; the three validation dependencies retain original bodies."""
    return database_sqlite_sources(repo) + archive_validation_sources(repo)[-3:] + [source(repo, path) for path in (
        "Cotabby/Services/Vocabulary/VocabularyImageStorage.swift",
        "Cotabby/Services/Vocabulary/VocabularyArchive.swift",
        "Cotabby/Services/Vocabulary/VocabularyArchive+ReviewCards.swift",
        "Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift")]


def vocabulary_store_sources(repo):
    """Original UI-store slice around the complete real archive; no replacement mutation bodies."""
    store = source(repo, "Cotabby/Services/Vocabulary/VocabularyStore.swift")
    # CLI/media/review/export presentation is not in this Windows slice. Retain every exercised
    # declaration/body exactly; omit only diagnostic lines whose module is app-specific.
    wrapper = 'import Foundation\nimport Combine\n@MainActor final class VocabularyStore {\n'
    wrapper += between(store, '    @Published private(set) var reviewTemplates:', '    /// The mailbox borrows')
    wrapper += between(store, '    func loadIfNeeded()', '    func mergeSenses(')
    wrapper += between(store, '    func mergeSenses(', '    func saveReview(')
    wrapper += between(store, '    func remove(_ capture:', '    func prepareImage(_ data:')
    wrapper += between(store, '    func remove(id:', '    func prepareExport(')
    wrapper += store[store.index('    private func change('):]
    if wrapper.count('CotabbyLogger.learning.') != 3: raise RuntimeError('Pinned store logging boundary changed')
    wrapper = '\n'.join(line for line in wrapper.split('\n') if 'CotabbyLogger.learning.' not in line)
    wrapper += '''
extension VocabularyStore {
    func applyProbe(_ snapshot: VocabularySnapshot) { apply(snapshot) }
    func pendingCountProbe(_ capture: VocabularyCapture) -> Int { pendingCounts[VocabularyPolicy.captureKey(capture)] ?? 0 }
    func controlledChangeProbe(_ capture: VocabularyCapture, gate: StoreProbeGate) async {
        await change(capture) { await gate.wait(); return try await self.archive.save(capture) }
    }
}
'''
    return database_archive_sources(repo) + [wrapper]


def notebook_browse_sources(repo):
    """Original notebook computed properties in an immutable document input wrapper."""
    view = source(repo, 'Cotabby/UI/Vocabulary/VocabularyNotebookView.swift')
    wrapper = '''import Foundation
struct NotebookBrowseReference {
    var store: VocabularyDocument
    var query = ""
    var language: LearningLanguage?
    var selectedTag = ""
    var newestFirst = true
    var selection: UUID?
'''
    wrapper += between(view, '    private var visibleEntries:', '    private var graphSelection:')
    wrapper += between(view, '    private var selectedEntry:', '    private func selections(')
    wrapper += '''
    func visibleProbe() -> [VocabularyEntry] { visibleEntries }
    func selectionProbe() -> VocabularySelection? {
        guard let entry = selectedEntry, let sense = selectedSense else { return nil }
        return .init(entryID: entry.id, senseID: sense.id)
    }
}
'''
    policy = source(repo, 'Cotabby/Support/Vocabulary/VocabularyPolicy.swift')
    fields = between(policy, '        let terms = query.split(whereSeparator:', '        return terms.allSatisfy')
    wrapper += "\nfunc notebookSearchFacts(_ entry: VocabularyEntry, query: String) -> [[String: Any]] {\n" + fields
    wrapper += '        return terms.flatMap { term in fields.map { ["field": $0, "term": String(term), "contains": $0.localizedStandardContains(term)] } }\n}\n'
    wrapper += "\nimport SwiftUI\nstruct NotebookHighlightReference {\n" + between(view,
        '    private func highlighted(_ example:', '\n}\n\nextension VocabularyEntry.Kind')
    wrapper += "\n    func probe(_ example: VocabularyExample) -> [[String: Any]] {\n        let value = highlighted(example)\n"
    wrapper += '        return value.runs.map { ["text": String(value[$0.range].characters), "target": $0.font != nil] }\n    }\n}\n'
    return sense_policy_sources(repo) + [wrapper]


def export_sources(repo):
    """Complete original export declarations, with real renderer dependency and no archive IO."""
    return sense_policy_sources(repo) + [source(repo, path) for path in (
        "Cotabby/Models/Vocabulary/VocabularyExportModels.swift",
        "Cotabby/Support/Vocabulary/VocabularyExportPolicy.swift",
        "Cotabby/Support/Vocabulary/VocabularyExportFormatter.swift")]


def archive_export_sources(repo):
    """Complete real archive plus original export/ZIP declarations; no replacement IO bodies."""
    return database_archive_sources(repo) + [source(repo, path) for path in (
        "Cotabby/Models/Vocabulary/VocabularyExportModels.swift",
        "Cotabby/Support/Vocabulary/VocabularyExportPolicy.swift",
        "Cotabby/Support/Vocabulary/VocabularyExportFormatter.swift",
        "Cotabby/Support/Vocabulary/VocabularyExportZIP.swift",
        "Cotabby/Services/Vocabulary/VocabularyArchive+Export.swift")]


def export_session_sources(repo):
    """Actual export session + existing Store ownership/delegates around the complete archive."""
    store = vocabulary_store_sources(repo)[-1]
    marker = '\nextension VocabularyStore {\n    func applyProbe('
    if store.count(marker) != 1: raise RuntimeError('Original Store test extension boundary changed')
    store = store[:store.index(marker)]
    original = source(repo, 'Cotabby/Services/Vocabulary/VocabularyStore.swift')
    delegates = between(original, '    func prepareExport(', '    func export(to url:')
    logging = '''        CotabbyLogger.learning.info("Vocabulary exported", metadata: ["format": .string(package.configuration.format.rawValue),
            "senses": .stringConvertible(package.senseCount), "with_images": .stringConvertible(package.configuration.includesImages)])
'''
    if delegates.count(logging) != 1: raise RuntimeError('Pinned export diagnostic boundary changed')
    store += '\nextension VocabularyStore {\n' + delegates.replace(logging, '') + '}\n'
    session = source(repo, 'Cotabby/Services/Vocabulary/VocabularyExportSession.swift')
    session += '\nextension VocabularyExportSession { var generationProbe: UUID { generation } }\n'
    return archive_export_sources(repo) + [store, session]
