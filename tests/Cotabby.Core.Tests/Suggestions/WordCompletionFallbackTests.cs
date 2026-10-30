using Cotabby.Core.Spelling;

namespace Cotabby.Core.Tests.Suggestions;

public class WordCompletionFallbackTests
{
    [Fact]
    public void UniqueReferenceWinsAndAmbiguousReferencesAbstain()
    {
        var candidates = new[] { new WordPrefixIndex.Candidate("schedule", 100) };
        Assert.Equal("uler", WordCompletionFallback.Suffix("sched", ["scheduler"], candidates));
        Assert.Null(WordCompletionFallback.Suffix("sched", ["schedule", "scheduler"], candidates));
        Assert.Equal("ULE", WordCompletionFallback.Suffix("SCHED", ["schedule"], candidates));
    }

    [Fact]
    public void DictionaryRequiresStrongFrequencyMarginAndBoundedPrefix()
    {
        var index = new WordPrefixIndex("schedule 100\nscheduled 20\nscheduler 3\ninvalid-word 999\n");
        Assert.Equal("ule", WordCompletionFallback.Suffix("sched", [], index.Candidates("sched")));
        Assert.Empty(index.Candidates("sc"));
        Assert.Null(WordCompletionFallback.Suffix("sched", [], [new("schedule", 100), new("scheduled", 26)]));
        var broad = new WordPrefixIndex(string.Join('\n', Enumerable.Range(0, 513).Select(i => $"abc{new string('a', i + 1)} 1")));
        Assert.Empty(broad.Candidates("abc"));
    }
}
