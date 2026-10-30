using System.Windows.Threading;
using Cotabby.App.UI;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningShowcaseStoreTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task IndependentSamplesUseRealCanonicalTransactionsAndDrain() => dispatcher.RunAsync(async () =>
    {
        await using var first = new LearningShowcaseStore(Dispatcher.CurrentDispatcher);
        await using var second = new LearningShowcaseStore(Dispatcher.CurrentDispatcher);
        await first.PrepareAsync(); await second.PrepareAsync();
        var capture = new VocabularyCapture("sample-only", "sample-only", LearningLanguage.English, VocabularyEntryKind.Word, "example", LearningLanguage.English);
        Assert.True(await first.Writing.SaveAsync(capture));
        Assert.True(first.Writing.Contains(capture)); Assert.False(second.Writing.Contains(capture));
        var firstState = await first.Backend.ReadAsync(); var secondState = await second.Backend.ReadAsync();
        var bank = Assert.Single(firstState.Document.CollectedEntries, entry => entry.Expression == "bank");
        Assert.Equal(2, bank.Senses.Count);
        Assert.Contains(firstState.Document.CollectedEntries, entry => entry.Expression == "sample-only");
        Assert.DoesNotContain(secondState.Document.CollectedEntries, entry => entry.Expression == "sample-only");
        await first.DisposeAsync(); await Assert.ThrowsAsync<ObjectDisposedException>(() => first.PrepareAsync());
    });
}
