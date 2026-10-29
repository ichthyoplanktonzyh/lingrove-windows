using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningLexiconServiceTests
{
    [Fact]
    public async Task UnknownLanguageSearchesAllBundledLanguagesWithoutChangingKnownIndices()
    {
        var service = new LearningLexiconService(new Source(() => Resource([
            Entry("bank", [], "en"), Entry("bank", [], "de"), Entry("other", [], "fr") ])), new FixtureText());
        var all = Assert.IsType<LearningDictionaryResult.Entries>(await service.LookupUnknownAsync("bank", false));
        Assert.Equal(new[] { "en", "de" }, all.Values.Select(value => value.Language));
        var english = Assert.IsType<LearningDictionaryResult.Entries>(await service.LookupAsync("bank", LearningLanguage.English, false));
        Assert.Equal("en", Assert.Single(english.Values).Language);
    }

    [Fact]
    public async Task FormsRequireExpressionModeAndMissesAreValues()
    {
        var source = new Source(() => Resource([Entry("take care of", ["taking care of", "take"])]));
        var service = new LearningLexiconService(source, new FixtureText());
        Assert.Equal(0, source.Calls);
        var found = Assert.IsType<LearningDictionaryResult.Entries>(await service.LookupAsync("taking care of", LearningLanguage.English, true));
        Assert.Equal("source-id", Assert.Single(found.Values).Id);
        Assert.Equal(LearningDictionaryResult.NotFound, await service.LookupAsync("taking care of", LearningLanguage.English, false));
        Assert.Equal(LearningDictionaryResult.NotFound, await service.LookupAsync("take", LearningLanguage.English, false));
        Assert.Equal(LearningDictionaryResult.NotFound, await service.LookupAsync("unknown", LearningLanguage.English, true));
        Assert.IsType<LearningDictionaryResult.Entries>(await service.LookupAsync("TAKE CARE OF", LearningLanguage.English, false));
        Assert.Equal(1, source.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("word\tword")]
    [InlineData("word\u0000")]
    [InlineData("word\ufffd")]
    [InlineData("word\ud800")]
    public async Task InvalidQueriesNeverLoadTheResource(string query)
    {
        var source = new Source(() => Resource([]));
        var service = new LearningLexiconService(source, new FixtureText());
        Assert.Equal(LearningLexiconFailure.InvalidQuery,
            (await Assert.ThrowsAsync<LearningLexiconException>(() => service.LookupAsync(query, LearningLanguage.English, true))).Failure);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task QueryBudgetUsesUtf8BytesAndDoesNotTruncate()
    {
        var service = new LearningLexiconService(new Source(() => Resource([])), new FixtureText());
        Assert.Equal(LearningDictionaryResult.NotFound, await service.LookupAsync(new string('é', 128), LearningLanguage.English, true));
        Assert.Equal(LearningLexiconFailure.InvalidQuery,
            (await Assert.ThrowsAsync<LearningLexiconException>(() => service.LookupAsync(new string('é', 129), LearningLanguage.English, true))).Failure);
    }

    [Fact]
    public async Task ChineseScriptsShareEntriesButKeepTheirRequestedSegmentationLanguage()
    {
        var source = new Source(() => Resource([Entry("中国", [], "zh")]));
        var text = new FixtureText { Word = new LearningDictionaryWord("中国", new(0, 2)) };
        var service = new LearningLexiconService(source, text);
        foreach (var language in new[] { LearningLanguage.Chinese, LearningLanguage.TraditionalChinese })
        {
            Assert.Equal("source-id", Assert.Single(await service.RecognizeAsync("中国", language)).EntryId);
            Assert.IsType<LearningDictionaryResult.Entries>(await service.LookupAsync("中国", language, true));
        }
        Assert.Equal([LearningLanguage.Chinese, LearningLanguage.TraditionalChinese], text.Languages);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task ConcurrentCallersShareOneSuccessfulLazyLoadAndDoNotRunInUiContext()
    {
        SynchronizationContext? observed = new();
        var source = new Source(() => { observed = SynchronizationContext.Current; return Resource([]); });
        var service = new LearningLexiconService(source, new FixtureText());
        var previous = SynchronizationContext.Current;
        Task<LearningDictionaryResult>[] tasks;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            tasks = Enumerable.Range(0, 16).Select(_ => service.LookupAsync("word", LearningLanguage.English, true)).ToArray();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(observed);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task FailedLoadCanRetryAndDoesNotBecomeAnEmptyDictionary()
    {
        int attempt = 0;
        var source = new Source(() => ++attempt == 1
            ? throw new LearningLexiconException(LearningLexiconFailure.UnreadableResource) : Resource([Entry("word", [])]));
        var service = new LearningLexiconService(source, new FixtureText());
        await Assert.ThrowsAsync<LearningLexiconException>(() => service.LookupAsync("word", LearningLanguage.English, true));
        Assert.IsType<LearningDictionaryResult.Entries>(await service.LookupAsync("word", LearningLanguage.English, true));
        Assert.Equal(2, source.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringLoadMatchesMacRecognitionAndBundledLookupBoundaries(bool recognition)
    {
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var source = new Source(() =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return Resource([]);
        });
        var service = new LearningLexiconService(source, new FixtureText());
        using var cancellation = new CancellationTokenSource();
        Task operation = recognition ? service.RecognizeAsync("word", LearningLanguage.English, cancellation.Token)
            : service.LookupAsync("word", LearningLanguage.English, true, cancellation.Token);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            cancellation.Cancel();
            using var queued = new CancellationTokenSource();
            var waiting = service.LookupAsync("word", LearningLanguage.English, true, queued.Token);
            queued.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { release.Set(); }
        if (recognition) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10)));
        else await operation.WaitAsync(TimeSpan.FromSeconds(10)); // Mac has no post-lookup cancellation check.
        Assert.Equal(LearningDictionaryResult.NotFound,
            await service.LookupAsync("word", LearningLanguage.English, true).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, source.Calls);
    }

    private static LearningLexiconResource Resource(IReadOnlyList<LearningLexiconEntry> entries) => new("v", "attribution", "license", entries);
    private static LearningLexiconEntry Entry(string word, string[] forms, string language = "en") =>
        new("source-id", language, word, forms, [new("noun", ["source meaning"], [])], "source-url");
    private sealed class Source(Func<LearningLexiconResource> load) : ILearningLexiconResourceSource
    {
        public int Calls;
        public LearningLexiconResource Load() { Interlocked.Increment(ref Calls); return load(); }
    }
    // This fake supplies only ASCII fixture keys/scripted words. It is not the Mac Unicode oracle
    // or a production tokenizer; native dataset recognition is exercised separately on Windows.
    private sealed class FixtureText : ILearningLexicalText
    {
        public LearningDictionaryWord? Word;
        public List<LearningLanguage> Languages { get; } = [];
        public string Key(string value) => value.ToLowerInvariant();
        public bool IsPatternText(string value) => true;
        public bool FirstIsUppercase(string value) => false;
        public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language)
        { Languages.Add(language); return Word is { } word ? [word] : []; }
    }
}
