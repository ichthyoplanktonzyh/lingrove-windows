"""Actual pinned notebook and child-session sources; providers remain the driver's external seams."""
from macos_vocabulary_reference import between, sense_policy_sources, source


def notebook_session_sources(repo):
    values = sense_policy_sources(repo)
    declaration = 'import Foundation\nnonisolated enum LearningExplanationContext:'
    indexes = [index for index, value in enumerate(values) if value.startswith(declaration)]
    if len(indexes) != 1: raise RuntimeError('Original explanation value boundary changed')
    # This executable needs the complete real generation protocol as well as its values.
    values[indexes[0]] = source(repo, 'Cotabby/Models/LanguageLearning/LearningExplanationModels.swift')
    paths = ['Cotabby/Models/LanguageLearning/LearningUnitModels.swift',
        'Cotabby/Models/LanguageLearning/LearningDictionaryArticle.swift',
        'Cotabby/Models/LanguageLearning/DictionaryLanguagePair.swift',
        'Cotabby/Models/LanguageLearning/LearningDictionarySources.swift',
        'Cotabby/Models/LanguageLearning/LearningDictionarySettings.swift',
        'Cotabby/Support/LanguageLearning/LearningUnitPolicy.swift',
        'Cotabby/Support/LanguageLearning/DictionaryContentPolicy.swift',
        'Cotabby/Support/LanguageLearning/DictionaryWebsitePolicy.swift',
        'Cotabby/Support/Logging/RequestID.swift',
        'Cotabby/Services/LanguageLearning/LearningExplanationSession.swift',
        'Cotabby/Services/Vocabulary/VocabularyNotebookSession.swift']
    for path in paths:
        value = source(repo, path)
        if path == 'Cotabby/Services/LanguageLearning/LearningExplanationSession.swift':
            # Expose the real private generation task for deterministic awaiting, including after
            # cancel removes its handle. No timing sleep or substitute session state is needed.
            value += '\nextension LearningExplanationSession { func generationProbe() -> Task<Void, Never>? { work } }\n'
        values.append(value)
    words = source(repo, 'Cotabby/Models/LanguageLearning/LearningDictionaryWord.swift')
    values.append('import Foundation\n' + between(words, 'nonisolated struct LearningDictionaryWord:', '/// View-local geometry exists'))
    factory = source(repo, 'Cotabby/Support/Vocabulary/VocabularyCaptureFactory.swift')
    values.append('import Foundation\nnonisolated enum VocabularyCaptureFactory {\n' + between(factory, '    static func component(', '\n}\n') + '\n}\n')
    dictionary = source(repo, 'Cotabby/Services/LanguageLearning/LearningDictionarySession.swift')
    if dictionary.count('import Logging\n') != 1 or dictionary.count('CotabbyLogger.learning.') != 1:
        raise RuntimeError('Pinned dictionary diagnostics boundary changed')
    logging = between(dictionary, '        CotabbyLogger.learning.debug("Dictionary lookup", metadata:', '        let result = try await provider.lookup')
    dictionary = dictionary.replace('import Logging\n', '').replace(logging, '')
    values.append(dictionary)
    return values
