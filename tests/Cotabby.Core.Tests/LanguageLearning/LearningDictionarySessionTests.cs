using System.Threading.Channels;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningDictionarySessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly LearningUnit Expression = new(0, "look forward to", "look forward to", LearningUnitKind.Expression,
        [new(0, 15)], [new("look", new(0, 4)), new("forward", new(5, 7)), new("to", new(13, 2))], "source-id");

    [Fact]
    public async Task MissWordAndBackKeepSeparateTargetsAndCapturedContext()
    {
        var calls = new List<(string Query, bool Expression)>();
        await using var session = new LearningDictionarySession(new Provider((query, _, expression, _) =>
        { calls.Add((query, expression)); return Task.FromResult(expression ? LearningDictionaryResult.NotFound : Entry(query)); }));
        await session.OpenAsync(Expression, "document", LearningLanguage.English, "We look forward to it.").WaitAsync(Timeout);
        Assert.Equal(LearningDictionaryResult.NotFound, session.State.Result);
        var target = session.State.TargetIdentity;
        await session.SelectWordAsync(Expression.Words[1], target).WaitAsync(Timeout);
        Assert.Equal("forward", session.State.Title);
        Assert.Equal("We look forward to it.", session.State.SpeechContext);
        Assert.Same(Expression, session.State.Unit);
        Assert.NotEqual(target, session.State.TargetIdentity);
        Assert.IsType<LearningDictionaryResult.Entries>(session.State.Result);
        await session.ReturnToExpressionAsync(session.State.TargetIdentity).WaitAsync(Timeout);
        Assert.Null(session.State.SelectedWord);
        Assert.Equal("look forward to", session.State.Query);
        Assert.Equal(LearningDictionaryResult.NotFound, session.State.Result);
        Assert.Equal(new[] { ("look forward to", true), ("forward", false) }, calls);
        session.Close();
        Assert.Null(session.State.Unit); Assert.Null(session.State.DocumentId); Assert.Null(session.State.SpeechContext);
    }

    [Fact]
    public async Task ExactOccurrencesAndCapturedTargetRejectStaleButtons()
    {
        int calls = 0;
        await using var session = new LearningDictionarySession(new Provider((query, _, _, _) =>
        { calls++; return Task.FromResult(Entry(query)); }));
        await session.OpenAsync(Expression, "first", LearningLanguage.English).WaitAsync(Timeout);
        Guid old = session.State.TargetIdentity;
        await session.SelectWordAsync(new("forward", new(100, 7))).WaitAsync(Timeout);
        Assert.Equal(1, calls);
        await session.OpenAsync(Expression, "second", LearningLanguage.English).WaitAsync(Timeout);
        Guid next = session.State.TargetIdentity;
        await session.SelectWordAsync(Expression.Words[0], old).WaitAsync(Timeout);
        await session.ReturnToExpressionAsync(old).WaitAsync(Timeout);
        session.Close(old);
        Assert.Equal(next, session.State.TargetIdentity);
        Assert.Equal("second", session.State.DocumentId);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task OutOfOrderAndClosedDefinitionsNeverPublishOrEnterTheCache()
    {
        var provider = new DelayedProvider();
        await using var session = new LearningDictionarySession(provider);
        var firstTask = session.OpenAsync(Expression, "first", LearningLanguage.English);
        var first = await provider.Calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        var secondTask = session.SelectWordAsync(Expression.Words[1]);
        var second = await provider.Calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.True(first.Token.IsCancellationRequested);
        second.Result.SetResult(Entry("current"));
        await secondTask.WaitAsync(Timeout);
        first.Result.SetResult(Entry("retired"));
        await firstTask.WaitAsync(Timeout);
        Assert.Equal("current", Assert.Single(Assert.IsType<LearningDictionaryResult.Entries>(session.State.Result).Values).Headword);
        var thirdTask = session.SelectWordAsync(Expression.Words[2]);
        var third = await provider.Calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        session.Close();
        Assert.True(third.Token.IsCancellationRequested);
        third.Result.SetResult(Entry("closed"));
        await thirdTask.WaitAsync(Timeout);
        Assert.Null(session.State.Unit); Assert.Null(session.State.Result);
    }

    [Fact]
    public async Task FailuresRemainDistinctFromMissAndDoNotExposeDetailsOrPromiseUnportedFallback()
    {
        int attempt = 0;
        await using var session = new LearningDictionarySession(new Provider((_, _, _, _) =>
            Task.FromException<LearningDictionaryResult>(++attempt == 1 ? new IOException("private source path")
                : new LearningLexiconException(LearningLexiconFailure.MissingResource))));
        await session.OpenAsync(Expression, "document", LearningLanguage.English).WaitAsync(Timeout);
        Assert.Equal(LearningDictionaryContentStatus.Failed, session.State.Status);
        Assert.Null(session.State.Result);
        Assert.DoesNotContain("private", session.State.ErrorMessage!);
        await session.ReturnToExpressionAsync().WaitAsync(Timeout);
        Assert.Equal("The local expression dictionary is unavailable.", session.State.ErrorMessage);
        Assert.DoesNotContain("individual words", session.State.ErrorMessage!);
    }

    [Fact]
    public async Task CacheEvictsOldestAtThirtyTwoAndLargeResultIsNotRetained()
    {
        int calls = 0;
        var words = Enumerable.Range(0, 32).Select(index => new LearningDictionaryWord("w" + index, new(index * 4, 3))).ToArray();
        var unit = new LearningUnit(0, "expression", "expression", LearningUnitKind.Expression, [new(0, 128)], words);
        await using var session = new LearningDictionarySession(new Provider((query, _, _, _) =>
        { calls++; return Task.FromResult(Entry(query)); }));
        await session.OpenAsync(unit, "document", LearningLanguage.English).WaitAsync(Timeout);
        foreach (var word in words) await session.SelectWordAsync(word).WaitAsync(Timeout);
        await session.ReturnToExpressionAsync().WaitAsync(Timeout);
        await session.SelectWordAsync(words[0]).WaitAsync(Timeout);
        Assert.Equal(35, calls);

        int largeCalls = 0;
        var large = new LearningDictionaryResult.Entries([new("id", "en", "word", [],
            [new("noun", [new string('x', 8 * 1024 * 1024)], [])], "source")]);
        await using var largeSession = new LearningDictionarySession(new Provider((_, _, _, _) =>
        { largeCalls++; return Task.FromResult<LearningDictionaryResult>(large); }));
        await largeSession.OpenAsync(Expression, "document", LearningLanguage.English).WaitAsync(Timeout);
        await largeSession.ReturnToExpressionAsync().WaitAsync(Timeout);
        Assert.Equal(2, largeCalls);
    }

    [Fact]
    public async Task DisposeDrainsCooperativeLookupAndFaultingObserversCannotBreakTheOwner()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new LearningDictionarySession(new Provider(async (_, _, _, token) =>
        { started.SetResult(); await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token); return LearningDictionaryResult.NotFound; }));
        session.StateChanged += _ => throw new InvalidOperationException("observer");
        var task = session.OpenAsync(Expression, "document", LearningLanguage.English);
        await started.Task.WaitAsync(Timeout);
        await session.DisposeAsync().AsTask().WaitAsync(Timeout);
        await task.WaitAsync(Timeout);
        Assert.Null(session.State.Unit);
    }

    private static LearningDictionaryResult Entry(string query) => new LearningDictionaryResult.Entries([
        new("id", "en", query, [], [new("noun", ["source meaning"], [])], "https://example.invalid/source")]);
    private sealed class Provider(Func<string, LearningLanguage, bool, CancellationToken, Task<LearningDictionaryResult>> run)
        : ILearningDictionaryProviding
    {
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken) =>
            run(query, language, expression, cancellationToken);
    }
    private sealed record Call(CancellationToken Token)
    {
        public TaskCompletionSource<LearningDictionaryResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class DelayedProvider : ILearningDictionaryProviding
    {
        public Channel<Call> Calls { get; } = Channel.CreateUnbounded<Call>();
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken)
        { var call = new Call(cancellationToken); Calls.Writer.TryWrite(call); return call.Result.Task; }
    }
}
