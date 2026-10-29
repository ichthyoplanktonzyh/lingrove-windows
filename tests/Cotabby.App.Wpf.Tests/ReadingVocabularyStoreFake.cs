using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

internal sealed class ReadingVocabularyStoreFake : IWritingVocabularyStore
{
    public event Action? Changed { add { } remove { } }
    public Action<VocabularySelection?>? OpenSelection => null;
    public string? ErrorMessage => null;
    public bool IsLoaded => true;
    public VocabularyCapture? Saved;
    public LearningExplanationRecord? Explanation;
    public TaskCompletionSource<bool>? PendingSave;
    public Task LoadAsync() => Task.CompletedTask;
    public bool Contains(VocabularyCapture capture) => Saved == capture;
    public bool IsPending(VocabularyCapture capture) => false;
    public Task<bool> SaveAsync(VocabularyCapture capture) { Saved = capture; return PendingSave?.Task ?? Task.FromResult(true); }
    public Task<bool> RemoveAsync(VocabularyCapture capture) { Saved = null; return Task.FromResult(true); }
    public Task<bool> SaveExplanationAsync(LearningExplanationRecord record) { Explanation = record; return Task.FromResult(true); }
    public void Show(VocabularyCapture capture) { }
}
