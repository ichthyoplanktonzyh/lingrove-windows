using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>Borrowed writing-card surface. It owns no editor, Store or persistence lifetime.</summary>
internal interface IWritingVocabularyStore
{
    event Action? Changed;
    Action<VocabularySelection?>? OpenSelection { get; }
    string? ErrorMessage { get; }
    bool IsLoaded { get; }
    Task LoadAsync();
    bool Contains(VocabularyCapture capture);
    bool IsPending(VocabularyCapture capture);
    Task<bool> SaveAsync(VocabularyCapture capture);
    Task<bool> RemoveAsync(VocabularyCapture capture);
    void Show(VocabularyCapture capture);
}
