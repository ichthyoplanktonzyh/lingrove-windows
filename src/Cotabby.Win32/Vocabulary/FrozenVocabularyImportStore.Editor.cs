using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using FrozenEntry = Cotabby.Core.Vocabulary.Frozen.VocabularyEntry;

namespace Cotabby.Win32.Vocabulary;

internal sealed partial class FrozenVocabularyImportStore
{
    public Task<FrozenVocabularyImportSnapshot> UpdateEntryAsync(FrozenEntry edited, FrozenEntry baseline,
        IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null, CancellationToken cancellationToken = default)
    {
        var supplied = importedImages is null ? new Dictionary<string, VocabularyPreparedImage>() : new Dictionary<string, VocabularyPreparedImage>(importedImages, StringComparer.Ordinal);
        return ReviewTransaction(document => new VocabularyReviewDraft(VocabularyEntryDraftPolicy.Update(edited, baseline, document, _now()), true), cancellationToken, supplied);
    }
}
