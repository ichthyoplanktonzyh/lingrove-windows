using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Vocabulary;

// This adapter preserves existing library/view tests. Production composition selects the
// canonical owner; this interface alone neither constructs nor opens an older archive.
internal sealed partial class VocabularyStore : IWritingVocabularyStore
{
    event Action? IWritingVocabularyStore.Changed { add => Changed += value; remove => Changed -= value; }
    Action<VocabularySelection?>? IWritingVocabularyStore.OpenSelection => OpenSelection;
    string? IWritingVocabularyStore.ErrorMessage => ErrorMessage;
    bool IWritingVocabularyStore.IsLoaded => IsLoaded;
    Task IWritingVocabularyStore.LoadAsync() => LoadAsync();
    bool IWritingVocabularyStore.Contains(VocabularyCapture capture) => Contains(capture);
    bool IWritingVocabularyStore.IsPending(VocabularyCapture capture) => IsPending(capture);
    Task<bool> IWritingVocabularyStore.SaveAsync(VocabularyCapture capture) => SaveAsync(capture);
    Task<bool> IWritingVocabularyStore.RemoveAsync(VocabularyCapture capture) => RemoveAsync(capture);
    void IWritingVocabularyStore.Show(VocabularyCapture capture) => Show(capture);
}
