using Cotabby.Core.LanguageLearning;
using LiveExplanationRecord = Cotabby.Core.LanguageLearning.LearningExplanationRecord;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>An acknowledged revision and actual stable writing destination; Changed is false for
/// a missing removal, unmatched explanation or refused adoption, without a revision increment.</summary>
public sealed record CanonicalWritingReceipt(ulong Revision, bool Changed, VocabularyExplanationDestination? Destination = null);

/// <summary>One application-owned current-format notebook serves import, browsing and writing.
/// Card inputs are detached live values; this contract reads no older notebook or persistence format.</summary>
public interface ICanonicalVocabularyStore : IVocabularyEntryImportStore, ICanonicalVocabularyReviewStore, ICanonicalVocabularyMediaStore, ICanonicalVocabularyEditorStore, ICanonicalVocabularySenseStore, ICanonicalVocabularySemanticStore, IAsyncDisposable
{
    Task<CanonicalWritingReceipt> SaveCaptureAsync(VocabularyCapture capture, CancellationToken cancellationToken = default);
    Task<CanonicalWritingReceipt> RemoveCaptureAsync(VocabularyCapture capture, CancellationToken cancellationToken = default);
    Task<LiveExplanationRecord?> FindExplanationAsync(LearningExplanationInput input, LearningLanguage language, CancellationToken cancellationToken = default);
    Task<CanonicalWritingReceipt> SaveExplanationAsync(LiveExplanationRecord record, CancellationToken cancellationToken = default);
    Task<CanonicalWritingReceipt> AdoptExplanationAsync(LiveExplanationRecord record, VocabularyExplanationDestination destination, CancellationToken cancellationToken = default);
}
