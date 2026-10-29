using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>One Store captures exact offered senses and media in one span; a package owns its bytes.</summary>
public interface ICanonicalVocabularyExportStore
{
    Task<VocabularyExportPackage> PrepareExportAsync(IReadOnlyList<VocabularySelection> selections, VocabularyExportConfiguration configuration, CancellationToken cancellationToken = default);
    Task WriteExportAsync(VocabularyExportPackage package, string destination, CancellationToken cancellationToken = default);
}
