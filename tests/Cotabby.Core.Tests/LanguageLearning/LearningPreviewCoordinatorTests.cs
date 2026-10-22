using System.Collections.Concurrent;
using System.Threading.Channels;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningPreviewCoordinatorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task PreviewPublishesTheCapturedRequestAndValidatedResult()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new GateEngine();
        var snapshots = new ConcurrentQueue<LearningPreviewState>();
        coordinator.StateChanged += snapshots.Enqueue;
        var options = new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.Chinese,
            TargetLanguage = LearningLanguage.French,
            Style = new LearningTranslationStyle(LearningTranslationRegister.Formal),
        };

        var preview = coordinator.PreviewAsync(" 你好。 ", options, engine);
        var call = await engine.NextCallAsync();

        Assert.Equal(" 你好。 ", call.Request.Source);
        Assert.Equal(LearningLanguage.Chinese, call.Request.Options.SourceLanguage);
        Assert.Equal(LearningLanguage.French, call.Request.Options.TargetLanguage);
        Assert.Equal(LearningTranslationRegister.Formal, call.Request.Options.Style.Register);
        Assert.Equal(call.Request.RequestId, coordinator.State.RequestId);
        Assert.Equal(options, coordinator.State.Options);
        Assert.Equal(LearningPreviewStatus.Loading, coordinator.State.Status);

        call.Complete("  Bonjour.\n ");
        await preview.WaitAsync(TestTimeout);

        Assert.Equal(LearningPreviewStatus.Ready, coordinator.State.Status);
        Assert.Equal("Bonjour.", coordinator.State.Result);
        Assert.Null(coordinator.State.ErrorMessage);
        Assert.Equal(
            new[] { LearningPreviewStatus.Loading, LearningPreviewStatus.Ready },
            snapshots.Select(snapshot => snapshot.Status));
    }

    [Fact]
    public async Task ANewSourceAndConfigurationWinWhenOlderEngineIgnoresCancellation()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new GateEngine();
        var firstOptions = new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.Chinese,
            TargetLanguage = LearningLanguage.English,
            ModelName = "first-model",
        };
        var secondOptions = new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.English,
            TargetLanguage = LearningLanguage.Japanese,
            ModelName = "second-model",
        };

        var firstTask = coordinator.PreviewAsync("First sentence.", firstOptions, engine);
        var firstCall = await engine.NextCallAsync();
        var firstId = firstCall.Request.RequestId;

        var secondTask = coordinator.PreviewAsync("Second sentence.", secondOptions, engine);
        var secondCall = await engine.NextCallAsync();
        Assert.True(firstCall.CancellationToken.IsCancellationRequested);
        Assert.NotEqual(firstId, secondCall.Request.RequestId);
        Assert.Equal("first-model", firstCall.Request.Options.ModelName);
        Assert.Equal("second-model", secondCall.Request.Options.ModelName);

        secondCall.Complete("二番目の文。");
        await secondTask.WaitAsync(TestTimeout);
        firstCall.Complete("obsolete first result");
        await firstTask.WaitAsync(TestTimeout);

        Assert.Equal(LearningPreviewStatus.Ready, coordinator.State.Status);
        Assert.Equal(secondCall.Request.RequestId, coordinator.State.RequestId);
        Assert.Equal("Second sentence.", coordinator.State.Source);
        Assert.Equal(LearningLanguage.Japanese, coordinator.State.Options?.TargetLanguage);
        Assert.Equal("二番目の文。", coordinator.State.Result);
    }

    [Fact]
    public async Task CancelInvalidatesTheTaskEvenWhenTheEngineReturnsLate()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new GateEngine();
        var statuses = new ConcurrentQueue<LearningPreviewStatus>();
        coordinator.StateChanged += state => statuses.Enqueue(state.Status);

        var preview = coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine);
        var call = await engine.NextCallAsync();
        coordinator.Cancel();

        Assert.True(call.CancellationToken.IsCancellationRequested);
        Assert.Equal(LearningPreviewStatus.Cancelled, coordinator.State.Status);
        call.Complete("late answer");
        await preview.WaitAsync(TestTimeout);

        Assert.Equal(LearningPreviewStatus.Cancelled, coordinator.State.Status);
        Assert.Null(coordinator.State.Result);
        Assert.DoesNotContain(LearningPreviewStatus.Ready, statuses);
    }

    [Fact]
    public async Task RetryUsesANewRequestAfterFixedSafeFailure()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new RetryEngine();
        const string secretDetails = "source and endpoint secret";

        await coordinator.PreviewAsync("Try again.", new LearningTranslationOptions(), engine).WaitAsync(TestTimeout);
        var failed = coordinator.State;
        Assert.Equal(LearningPreviewStatus.Failed, failed.Status);
        Assert.Equal("Translation failed. Check the translation settings and try again.", failed.ErrorMessage);
        Assert.DoesNotContain(secretDetails, failed.ErrorMessage, StringComparison.Ordinal);

        await coordinator.PreviewAsync("Try again.", new LearningTranslationOptions(), engine).WaitAsync(TestTimeout);
        var ready = coordinator.State;
        Assert.Equal(LearningPreviewStatus.Ready, ready.Status);
        Assert.NotEqual(failed.RequestId, ready.RequestId);
        Assert.Equal("It worked.", ready.Result);
    }

    [Fact]
    public async Task MissingEngineProducesAFixedUnavailableMessage()
    {
        await using var coordinator = new LearningPreviewCoordinator();

        await coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine: null!).WaitAsync(TestTimeout);

        Assert.Equal(LearningPreviewStatus.Failed, coordinator.State.Status);
        Assert.Equal("The translation engine is not ready. Check the translation settings.", coordinator.State.ErrorMessage);
    }

    [Theory]
    [InlineData(" \t\n ")]
    [InlineData("unsafe\0output")]
    [InlineData("replacement\uFFFDcharacter")]
    public async Task UnsafeOrEmptyOutputIsRejected(string result)
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new ImmediateEngine((_, _) => Task.FromResult(result));

        await coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine).WaitAsync(TestTimeout);

        Assert.Equal(LearningPreviewStatus.Failed, coordinator.State.Status);
        Assert.Null(coordinator.State.Result);
        Assert.Equal(new LearningTranslationException(LearningTranslationFailure.InvalidOutput).Message,
            coordinator.State.ErrorMessage);
    }

    [Fact]
    public async Task OversizedOutputIsRejectedWithoutTruncation()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var oversized = new string('x', LearningExpressionOutputPolicy.MaximumOutputBytes + 1);
        var engine = new ImmediateEngine((_, _) => Task.FromResult(oversized));

        await coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine).WaitAsync(TestTimeout);

        Assert.Equal(LearningPreviewStatus.Failed, coordinator.State.Status);
        Assert.Null(coordinator.State.Result);
        Assert.Equal(new LearningTranslationException(LearningTranslationFailure.InvalidOutput).Message,
            coordinator.State.ErrorMessage);
    }

    [Fact]
    public async Task NotificationsAreSequencedOutsideTheLockAndCallbackFailuresStayContained()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new CancellationAwareEngine();
        var statuses = new ConcurrentQueue<LearningPreviewStatus>();
        var callbackCouldReadState = false;

        coordinator.StateChanged += state =>
        {
            statuses.Enqueue(state.Status);
            if (state.Status != LearningPreviewStatus.Loading) return;

            var readState = Task.Run(() => coordinator.State.Status);
            callbackCouldReadState = readState.Wait(TestTimeout);
            coordinator.Cancel();
        };
        coordinator.StateChanged += _ => throw new InvalidOperationException("callback has private details");
        coordinator.StateChanged += state => statuses.Enqueue(state.Status);

        var preview = coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine);
        await preview.WaitAsync(TestTimeout);

        Assert.True(callbackCouldReadState);
        Assert.Equal(
            new[] { LearningPreviewStatus.Loading, LearningPreviewStatus.Loading,
                LearningPreviewStatus.Cancelled, LearningPreviewStatus.Cancelled },
            statuses);
        Assert.Equal(LearningPreviewStatus.Cancelled, coordinator.State.Status);
    }

    [Fact]
    public async Task DisposeCancelsAndDrainsAnEngineThatHonorsCancellation()
    {
        var coordinator = new LearningPreviewCoordinator();
        var engine = new CancellationAwareEngine();
        var preview = coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine);
        await engine.Started.Task.WaitAsync(TestTimeout);

        await coordinator.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        await preview.WaitAsync(TestTimeout);

        Assert.True(engine.CancellationObserved);
        Assert.Equal(LearningPreviewStatus.Cancelled, coordinator.State.Status);
    }

    [Fact]
    public async Task PreviewDispatchesPromptAndEngineWorkAwayFromTheCallingSynchronizationContext()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new ContextRecordingEngine();
        var uiContext = new PumpSynchronizationContext();
        var priorContext = SynchronizationContext.Current;
        Task preview;
        try
        {
            SynchronizationContext.SetSynchronizationContext(uiContext);
            preview = coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(priorContext);
        }

        // The pump would run a continuation captured by the caller. With explicit pool dispatch,
        // no request-policy or engine continuation is posted back to this UI context.
        uiContext.PumpPending();
        var engineContext = await engine.CapturedContext.Task.WaitAsync(TestTimeout);
        Assert.NotSame(uiContext, engineContext);
        Assert.False(string.IsNullOrWhiteSpace(engine.Request?.Prompt.Prompt));

        engine.Complete("A translation.");
        await preview.WaitAsync(TestTimeout);
        Assert.Equal(LearningPreviewStatus.Ready, coordinator.State.Status);
    }

    private sealed class GateEngine : ILearningTranslationEngine
    {
        private readonly Channel<EngineCall> _calls = Channel.CreateUnbounded<EngineCall>();

        public async Task<EngineCall> NextCallAsync() =>
            await _calls.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);

        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct)
        {
            var call = new EngineCall(request, ct);
            if (!_calls.Writer.TryWrite(call)) throw new InvalidOperationException("Test call queue closed.");
            // This fake deliberately ignores cancellation to prove the coordinator's identity guard.
            return call.Completion.Task;
        }
    }

    private sealed class EngineCall(LearningTranslationRequest request, CancellationToken cancellationToken)
    {
        public LearningTranslationRequest Request { get; } = request;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(string result) => Completion.TrySetResult(result);
    }

    private sealed class ImmediateEngine(Func<LearningTranslationRequest, CancellationToken, Task<string>> translate)
        : ILearningTranslationEngine
    {
        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct) => translate(request, ct);
    }

    private sealed class RetryEngine : ILearningTranslationEngine
    {
        private int _callCount;

        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct) =>
            Interlocked.Increment(ref _callCount) == 1
                ? Task.FromException<string>(new InvalidOperationException("source and endpoint secret"))
                : Task.FromResult("It worked.");
    }

    private sealed class CancellationAwareEngine : ILearningTranslationEngine
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                return "unreachable";
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class ContextRecordingEngine : ILearningTranslationEngine
    {
        private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<SynchronizationContext?> CapturedContext { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LearningTranslationRequest? Request { get; private set; }

        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct)
        {
            Request = request;
            CapturedContext.TrySetResult(SynchronizationContext.Current);
            return _completion.Task;
        }

        public void Complete(string result) => _completion.TrySetResult(result);
    }

    private sealed class PumpSynchronizationContext : SynchronizationContext
    {
        private const int MaximumCallbacksPerPump = 64;
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public override void Post(SendOrPostCallback callback, object? state) => _posted.Enqueue((callback, state));

        public void PumpPending()
        {
            var processed = 0;
            while (processed < MaximumCallbacksPerPump && _posted.TryDequeue(out var work))
            {
                processed++;
                var prior = Current;
                SetSynchronizationContext(this);
                try { work.Callback(work.State); }
                finally { SetSynchronizationContext(prior); }
            }

            if (!_posted.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"The synchronization-context pump exceeded {MaximumCallbacksPerPump} callbacks.");
            }
        }
    }
}
