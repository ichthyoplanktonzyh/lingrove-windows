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
