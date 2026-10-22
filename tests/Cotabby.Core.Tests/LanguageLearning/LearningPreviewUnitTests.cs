using System.Threading.Channels;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningPreviewUnitTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly LearningTranslationOptions Options = new() { ModelName = "fixture" };

    [Fact]
    public async Task AnnotationIsIndependentOfTranslationCompletionAndNeverRunsOnPrefixes()
    {
        var recognizer = new ControlledRecognizer();
        await using var owner = new LearningPreviewCoordinator(unitRecognizer: recognizer);
        var states = Channel.CreateUnbounded<LearningPreviewState>();
        owner.StateChanged += state => states.Writer.TryWrite(state);
        var engine = new StreamingEngine();
        var translation = owner.PreviewAsync("source", Options, engine);
        await engine.Started.Task.WaitAsync(Timeout);
        engine.Delta!("provisional");
        Assert.Empty(owner.State.Units);
        Assert.Equal(0, recognizer.Count);
        engine.Result.SetResult("complete");
        await translation.WaitAsync(Timeout);
        var call = await recognizer.Calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal("complete", call.Text);
        Assert.Equal(LearningLanguage.English, call.Language);
        Assert.False(call.Token.IsCancellationRequested);
        Assert.Equal(LearningPreviewStatus.Ready, owner.State.Status);
        Assert.Equal(LearningUnitStage.Pending, owner.State.UnitStage);
        call.Result.SetResult(Units("complete"));
        var state = await Until(states.Reader, state => state.UnitStage == LearningUnitStage.Ready);
        Assert.Equal("complete", Assert.Single(state.Units).Surface);
        Assert.Equal("complete", state.Result);
    }

    [Fact]
    public async Task NewPreviewWinsWhenOldRecognitionIgnoresCancellation()
    {
        var recognizer = new ControlledRecognizer();
        await using var owner = new LearningPreviewCoordinator(unitRecognizer: recognizer);
        var states = Channel.CreateUnbounded<LearningPreviewState>();
        owner.StateChanged += state => states.Writer.TryWrite(state);
        await owner.PreviewAsync("first", Options, new Engine("old")).WaitAsync(Timeout);
        var old = await recognizer.Calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        await owner.PreviewAsync("second", Options, new Engine("new")).WaitAsync(Timeout);
        var next = await recognizer.Calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.True(old.Token.IsCancellationRequested);
        string id = owner.State.RequestId;
        old.Result.SetResult(Units("old"));
        next.Result.SetResult(Units("new"));
        var ready = await Until(states.Reader, state => state.RequestId == id && state.UnitStage == LearningUnitStage.Ready);
        Assert.Equal("new", ready.Result);
        Assert.Equal("new", Assert.Single(ready.Units).Surface);
        owner.Cancel();
        Assert.Empty(owner.State.Units);
        Assert.Equal(LearningPreviewStatus.Cancelled, owner.State.Status);
    }

    [Fact]
    public async Task CancelRevokesIndependentWorkAfterTranslationHasFinished()
    {
        var recognizer = new ControlledRecognizer();
        await using var owner = new LearningPreviewCoordinator(unitRecognizer: recognizer);
        await owner.PreviewAsync("source", Options, new Engine("translation")).WaitAsync(Timeout);
        var call = await recognizer.Calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        owner.Cancel();
        Assert.True(call.Token.IsCancellationRequested);
        call.Result.SetResult(Units("translation"));
        await owner.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(LearningPreviewStatus.Cancelled, owner.State.Status);
        Assert.Empty(owner.State.Units);
    }

    [Fact]
    public async Task FailedRecognitionDoesNotFailOrEraseAValidatedTranslationAndCanRetry()
    {
        int calls = 0;
        var recognizer = new DelegateRecognizer((text, _, _) => ++calls == 1
            ? Task.FromException<IReadOnlyList<LearningUnit>>(new IOException("private dictionary path"))
            : Task.FromResult(Units(text)));
        await using var owner = new LearningPreviewCoordinator(unitRecognizer: recognizer);
        var states = Channel.CreateUnbounded<LearningPreviewState>();
        owner.StateChanged += state => states.Writer.TryWrite(state);
        await owner.PreviewAsync("source", Options, new Engine("translation"), "scope").WaitAsync(Timeout);
        var failed = await Until(states.Reader, state => state.UnitStage == LearningUnitStage.Failed);
        Assert.Equal(LearningPreviewStatus.Ready, failed.Status);
        Assert.Equal("translation", failed.Result);
        Assert.Null(failed.ErrorMessage);
        await owner.PreviewAsync("source", Options, new Engine("must use translation cache"), "scope").WaitAsync(Timeout);
        var id = owner.State.RequestId;
        Assert.Equal("translation", (await Until(states.Reader,
            state => state.RequestId == id && state.UnitStage == LearningUnitStage.Ready)).Result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UnitCacheUsesTargetLanguageAndExactTranslationAndFlushesAtSixtyFour()
    {
        int calls = 0;
        await using var owner = new LearningPreviewCoordinator(unitRecognizer: new DelegateRecognizer((text, _, _) =>
        { Interlocked.Increment(ref calls); return Task.FromResult(Units(text)); }));
        var states = Channel.CreateUnbounded<LearningPreviewState>();
        owner.StateChanged += state => states.Writer.TryWrite(state);
        async Task Complete(string result, LearningTranslationOptions? options = null)
        {
            await owner.PreviewAsync("source", options ?? Options, new Engine(result)).WaitAsync(Timeout);
            var id = owner.State.RequestId;
            await Until(states.Reader, state => state.RequestId == id && state.UnitStage == LearningUnitStage.Ready);
        }
        await Complete("same"); await Complete("same");
        Assert.Equal(1, calls);
        await Complete("same", Options with { TargetLanguage = LearningLanguage.Spanish });
        Assert.Equal(2, calls);
        for (int index = 0; index < 63; index++) await Complete("different" + index);
        Assert.Equal(65, calls);
        await Complete("same");
        Assert.Equal(66, calls);
    }

    [Fact]
    public async Task ClearCachePreventsOldTranslationAndAnnotationFromRepopulatingIt()
    {
        int calls = 0;
        await using var owner = new LearningPreviewCoordinator(unitRecognizer: new DelegateRecognizer((text, _, _) =>
        { Interlocked.Increment(ref calls); return Task.FromResult(Units(text)); }));
        var states = Channel.CreateUnbounded<LearningPreviewState>();
        owner.StateChanged += state => states.Writer.TryWrite(state);
        var engine = new StreamingEngine();
        var request = owner.PreviewAsync("source", Options, engine);
        await engine.Started.Task.WaitAsync(Timeout);
        owner.ClearCache();
        engine.Result.SetResult("translation");
        await request.WaitAsync(Timeout);
        var id = owner.State.RequestId;
        await Until(states.Reader, state => state.RequestId == id && state.UnitStage == LearningUnitStage.Ready);
        await owner.PreviewAsync("source", Options, new Engine("translation")).WaitAsync(Timeout);
        id = owner.State.RequestId;
        await Until(states.Reader, state => state.RequestId == id && state.UnitStage == LearningUnitStage.Ready);
        Assert.Equal(2, calls);
    }

    private static IReadOnlyList<LearningUnit> Units(string text) => [LearningUnit.Word(new(text, new(0, text.Length)))];
    private static async Task<LearningPreviewState> Until(ChannelReader<LearningPreviewState> reader,
        Func<LearningPreviewState, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (true) { var state = await reader.ReadAsync(timeout.Token); if (predicate(state)) return state; }
    }
    private sealed class Engine(string output) : ILearningTranslationEngine
    {
        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken cancellationToken) => Task.FromResult(output);
    }
    private sealed class StreamingEngine : ILearningTranslationEngine
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<string>? Delta;
        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken cancellationToken) => Result.Task;
        public Task<string> TranslateAsync(LearningTranslationRequest request, Action<string>? delta, CancellationToken cancellationToken)
        { Delta = delta; Started.SetResult(); return Result.Task; }
    }
    private sealed record Call(string Text, LearningLanguage Language, CancellationToken Token)
    {
        public TaskCompletionSource<IReadOnlyList<LearningUnit>> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class ControlledRecognizer : ILearningUnitRecognizing
    {
        public Channel<Call> Calls { get; } = Channel.CreateUnbounded<Call>();
        public int Count;
        public Task<IReadOnlyList<LearningUnit>> RecognizeAsync(string translation, LearningLanguage language, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Count);
            var call = new Call(translation, language, cancellationToken);
            Calls.Writer.TryWrite(call);
            return call.Result.Task;
        }
    }
    private sealed class DelegateRecognizer(Func<string, LearningLanguage, CancellationToken, Task<IReadOnlyList<LearningUnit>>> run)
        : ILearningUnitRecognizing
    {
        public Task<IReadOnlyList<LearningUnit>> RecognizeAsync(string translation, LearningLanguage language, CancellationToken cancellationToken) =>
            run(translation, language, cancellationToken);
    }
}
