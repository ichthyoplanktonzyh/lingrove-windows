using Cotabby.Core.Vocabulary.EntryInput;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Detached revisioned view published by the single archive owner after acknowledgement.</summary>
public sealed record FrozenVocabularyImportSnapshot(ulong Revision, VocabularyDocument Document);
public sealed record FrozenVocabularyImportPreview(IReadOnlyList<VocabularyEntryInput> Inputs, VocabularyEntryImportResult Result, ulong Revision)
{
    private IReadOnlyList<VocabularyEntryInput> _inputs = Cotabby.Core.Support.CollectionValues.List(Inputs);
    public IReadOnlyList<VocabularyEntryInput> Inputs { get => _inputs; init => _inputs = Cotabby.Core.Support.CollectionValues.List(value); }
}
/// <summary>Import surface borrowed from the eventual app-owned Store; file reading is separate.
/// Notifications are revisioned snapshots and consumers marshal them to their presentation thread.</summary>
public interface IVocabularyEntryImportStore
{
    Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration configuration, CancellationToken cancellationToken = default);
    Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken cancellationToken = default);
    IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed);
}
