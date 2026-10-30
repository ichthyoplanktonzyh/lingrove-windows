using System.Windows.Threading;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI;

/// <summary>Mounted demo owns a private in-memory canonical Store; it never borrows AppHost's
/// notebook or opens a user's file. Disposal drains the view lease before its database owner.</summary>
internal sealed class LearningShowcaseStore : IAsyncDisposable
{
    private readonly ICanonicalVocabularyStore _backend;
    internal CanonicalWritingViewStore Writing { get; }
    internal ICanonicalVocabularyStore Backend => _backend;
    private readonly object _gate = new();
    private Task? _preparation;
    private Task? _disposal;

    internal LearningShowcaseStore(Dispatcher dispatcher)
    {
        _backend = FrozenVocabularyStoreFactory.CreateInMemory();
        Writing = new CanonicalWritingViewStore(_backend, new DispatcherSynchronizationContext(dispatcher));
    }
    internal Task PrepareAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            return _preparation ??= PrepareCoreAsync();
        }
    }
    private async Task PrepareCoreAsync()
    {
        await Writing.LoadAsync();
        foreach (var capture in new[]
        {
            Seed("bank", "She deposited money at the bank.", "银行，提供存款等金融服务的机构"),
            Seed("bank", "They sat on the bank of the river.", "河岸，河流边的陆地"),
            Seed("look forward to", "I look forward to seeing you.", "期待某事或做某事", VocabularyEntryKind.Expression),
            Seed("take a break", "Let's take a break after this meeting.", "暂时休息一下", VocabularyEntryKind.Expression),
        }) await Writing.SaveAsync(capture);
    }
    private static VocabularyCapture Seed(string word, string sentence, string meaning, VocabularyEntryKind kind = VocabularyEntryKind.Word)
    {
        int location = sentence.IndexOf(word, StringComparison.Ordinal);
        var example = new Cotabby.Core.Vocabulary.VocabularyExample(Guid.NewGuid(), sentence, "", [new(location, word.Length)],
            VocabularyExampleCompleteness.Sentence, meaning, DateTimeOffset.UnixEpoch, meaningLanguage: LearningLanguage.Chinese);
        return new(word, word, LearningLanguage.English, kind, meaning, LearningLanguage.Chinese, example);
    }
    public ValueTask DisposeAsync() { lock (_gate) return new(_disposal ??= Task.Run(DrainAsync)); }
    private async Task DrainAsync()
    {
        Task? preparation; lock (_gate) preparation = _preparation;
        if (preparation is not null) try { await preparation.ConfigureAwait(false); } catch { }
        try { await Writing.DisposeAsync(); } finally { await _backend.DisposeAsync(); }
    }
}
