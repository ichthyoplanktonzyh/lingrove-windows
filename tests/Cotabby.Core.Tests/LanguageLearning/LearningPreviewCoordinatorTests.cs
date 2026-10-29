using System.Collections.Concurrent;
using System.Threading.Channels;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Runtime.Metrics;

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

        var preview = coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine, "test-profile");
        var call = await engine.NextCallAsync();
        call.Emit("provisional");
        Assert.Equal("provisional", coordinator.State.PartialResult);
        coordinator.Cancel();

        Assert.True(call.CancellationToken.IsCancellationRequested);
        Assert.Equal(LearningPreviewStatus.Cancelled, coordinator.State.Status);
        Assert.Null(coordinator.State.PartialResult);
        call.Complete("late answer");
        await preview.WaitAsync(TestTimeout);

        Assert.Equal(LearningPreviewStatus.Cancelled, coordinator.State.Status);
        Assert.Null(coordinator.State.Result);
        Assert.DoesNotContain(LearningPreviewStatus.Ready, statuses);

        var retry = coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine, "test-profile");
        var retryCall = await engine.NextCallAsync();
        Assert.Equal(2, engine.CallCount);
        retryCall.Complete("fresh answer");
        await retry.WaitAsync(TestTimeout);
        Assert.Equal("fresh answer", coordinator.State.Result);
    }

    [Fact]
    public async Task RetryUsesANewRequestAfterFixedSafeFailure()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new RetryEngine();
        const string secretDetails = "source and endpoint secret";

        await coordinator.PreviewAsync("Try again.", new LearningTranslationOptions(), engine, "test-profile").WaitAsync(TestTimeout);
        var failed = coordinator.State;
        Assert.Equal(LearningPreviewStatus.Failed, failed.Status);
        Assert.Null(failed.PartialResult);
        Assert.Equal("Translation failed. Check the translation settings and try again.", failed.ErrorMessage);
        Assert.DoesNotContain(secretDetails, failed.ErrorMessage, StringComparison.Ordinal);

        await coordinator.PreviewAsync("Try again.", new LearningTranslationOptions(), engine, "test-profile").WaitAsync(TestTimeout);
        var ready = coordinator.State;
        Assert.Equal(LearningPreviewStatus.Ready, ready.Status);
        Assert.NotEqual(failed.RequestId, ready.RequestId);
        Assert.Equal("It worked.", ready.Result);
        Assert.Equal(2, engine.CallCount);

        await coordinator.PreviewAsync("Try again.", new LearningTranslationOptions(), engine, "test-profile").WaitAsync(TestTimeout);
        Assert.Equal(2, engine.CallCount);
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

    [Fact]
    public async Task OptionalPlannerRunsOffTheCallingContextAndKeepsCapturedOptionsInState()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var options = new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.English,
            TargetLanguage = LearningLanguage.French,
            Style = new LearningTranslationStyle(LearningTranslationRegister.Formal),
        };
        var engine = new CountingPlannerEngine((_, _) => Task.FromResult("Planned translation."));
        var uiContext = new PumpSynchronizationContext();
        var priorContext = SynchronizationContext.Current;
        Task preview;
        try
        {
            SynchronizationContext.SetSynchronizationContext(uiContext);
            preview = coordinator.PreviewAsync("A sentence.", options, engine);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(priorContext);
        }

        uiContext.PumpPending();
        var plannerContext = await engine.PlannerContext.Task.WaitAsync(TestTimeout);
        await preview.WaitAsync(TestTimeout);

        Assert.NotSame(uiContext, plannerContext);
        Assert.Equal(options, engine.PlannedOptions);
        Assert.Equal(options, engine.LastRequest?.Options);
        Assert.Equal(options, coordinator.State.Options);
        Assert.Equal("Planned translation.", coordinator.State.Result);
        Assert.Equal(1, engine.PlanCount);
        Assert.Equal(1, engine.GenerationCount);
    }

    [Fact]
    public async Task ACancelledLatePlanDoesNotStartGenerationOrPublishAResult()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new GatePlannerEngine();
        var options = new LearningTranslationOptions();

        var preview = coordinator.PreviewAsync("A sentence.", options, engine, "profile");
        var plan = await engine.NextPlanAsync();
        coordinator.Cancel();
        Assert.True(plan.CancellationToken.IsCancellationRequested);
        plan.Complete(LearningTranslationRequestFactory.Create(plan.RequestId, plan.Source, plan.Options));
        await preview.WaitAsync(TestTimeout);

        Assert.Equal(0, engine.GenerationCount);
        Assert.Equal(LearningPreviewStatus.Cancelled, coordinator.State.Status);
        Assert.Null(coordinator.State.Result);
        Assert.Null(coordinator.State.PartialResult);

        var retry = coordinator.PreviewAsync("A sentence.", options, engine, "profile");
        var retryPlan = await engine.NextPlanAsync();
        retryPlan.Complete(LearningTranslationRequestFactory.Create(retryPlan.RequestId, retryPlan.Source, retryPlan.Options));
        await retry.WaitAsync(TestTimeout);
        Assert.Equal(1, engine.GenerationCount);
    }

    [Fact]
    public async Task APlanSupersededWhileWaitingCannotGenerateOrReplaceTheNewerResult()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new GatePlannerEngine();
        var options = new LearningTranslationOptions();

        var oldTask = coordinator.PreviewAsync("Old sentence.", options, engine, "profile");
        var oldPlan = await engine.NextPlanAsync();
        var currentTask = coordinator.PreviewAsync("Current sentence.", options, engine, "profile");
        var currentPlan = await engine.NextPlanAsync();
        Assert.True(oldPlan.CancellationToken.IsCancellationRequested);

        currentPlan.Complete(LearningTranslationRequestFactory.Create(
            currentPlan.RequestId, currentPlan.Source, currentPlan.Options));
        await currentTask.WaitAsync(TestTimeout);
        Assert.Equal("Generated for Current sentence.", coordinator.State.Result);

        oldPlan.Complete(LearningTranslationRequestFactory.Create(oldPlan.RequestId, oldPlan.Source, oldPlan.Options));
        await oldTask.WaitAsync(TestTimeout);

        Assert.Equal(1, engine.GenerationCount);
        Assert.Equal("Current sentence.", coordinator.State.Source);
        Assert.Equal("Generated for Current sentence.", coordinator.State.Result);

        // The stale plan never completed generation, so it cannot have created an old-source cache entry.
        var oldRetry = coordinator.PreviewAsync("Old sentence.", options, engine, "profile");
        var oldRetryPlan = await engine.NextPlanAsync();
        Assert.Equal(1, engine.GenerationCount);
        oldRetryPlan.Complete(LearningTranslationRequestFactory.Create(
            oldRetryPlan.RequestId, oldRetryPlan.Source, oldRetryPlan.Options));
        await oldRetry.WaitAsync(TestTimeout);
        Assert.Equal(2, engine.GenerationCount);
    }

    [Fact]
    public async Task PlannerFailuresAreRedactedAndCannotReachGeneration()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new FailingPlannerEngine(new InvalidOperationException("private source and model path"));

        await coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine).WaitAsync(TestTimeout);

        Assert.Equal(LearningPreviewStatus.Failed, coordinator.State.Status);
        Assert.Null(coordinator.State.Result);
        Assert.DoesNotContain("private source and model path", coordinator.State.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("Translation failed. Check the translation settings and try again.", coordinator.State.ErrorMessage);
        Assert.Equal(0, engine.GenerationCount);
    }

    [Fact]
    public async Task ScopedCacheHitStillPlansButSkipsTranslationGeneration()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var observations = new ConcurrentQueue<PerformanceRequestObservation>();
        coordinator.RequestObserved += observations.Enqueue;
        var engine = new CountingPlannerEngine((_, _) => Task.FromResult("Cached planned result."));
        var options = new LearningTranslationOptions { ModelName = "fixture-model" };

        await coordinator.PreviewAsync("A sentence.", options, engine, "profile").WaitAsync(TestTimeout);
        await coordinator.PreviewAsync("A sentence.", options, engine, "profile").WaitAsync(TestTimeout);

        Assert.Equal(2, engine.PlanCount);
        Assert.Equal(1, engine.GenerationCount);
        Assert.Equal("Cached planned result.", coordinator.State.Result);
        var observation = Assert.Single(observations);
        Assert.Equal("fixture-model", observation.ModelName);
        Assert.Equal(ModelRequestSource.Translation, observation.Source);
        Assert.Equal(ModelRequestOutcome.Completed, observation.Outcome);
        Assert.NotNull(observation.UsableResultMs);
    }

    [Fact]
    public async Task PlannerCannotRelabelTheCapturedOptionsInStateOrCache()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var options = new LearningTranslationOptions { TargetLanguage = LearningLanguage.French };
        var engine = new CountingPlannerEngine(
            (request, _) => Task.FromResult("Should not generate."),
            plannedOptions: options with { TargetLanguage = LearningLanguage.Japanese });

        await coordinator.PreviewAsync("A sentence.", options, engine, "profile").WaitAsync(TestTimeout);

        Assert.Equal(options, coordinator.State.Options);
        Assert.Equal(LearningPreviewStatus.Failed, coordinator.State.Status);
        Assert.Null(coordinator.State.Result);
        Assert.Equal(new LearningTranslationException(LearningTranslationFailure.InvalidOptions).Message,
            coordinator.State.ErrorMessage);
        Assert.Equal(0, engine.GenerationCount);
    }

    [Fact]
    public async Task StreamPublishesTheFirstPrefixImmediatelyThenCoalescesUntilFinal()
    {
        var clock = new ManualTimeProvider();
        await using var coordinator = new LearningPreviewCoordinator(clock);
        var engine = new GateEngine();
        var snapshots = new ConcurrentQueue<LearningPreviewState>();
        coordinator.StateChanged += snapshots.Enqueue;

        var preview = coordinator.PreviewAsync("A sentence.", new LearningTranslationOptions(), engine);
        var call = await engine.NextCallAsync();

        call.Emit(" \n");
        Assert.Null(coordinator.State.PartialResult);
        call.Emit("Hello");
        Assert.Equal("Hello", coordinator.State.PartialResult);
        Assert.Null(coordinator.State.Result);
        Assert.Equal(LearningPreviewStatus.Loading, coordinator.State.Status);

        call.Emit(",");
        Assert.Equal("Hello", coordinator.State.PartialResult);
        clock.Advance(TimeSpan.FromMilliseconds(34));
        call.Emit(" world");
        Assert.Equal("Hello, world", coordinator.State.PartialResult);

        call.Complete("Hello, world!");
        await preview.WaitAsync(TestTimeout);

        Assert.Null(coordinator.State.PartialResult);
        Assert.Equal("Hello, world!", coordinator.State.Result);
        Assert.Equal(LearningPreviewStatus.Ready, coordinator.State.Status);
        Assert.Equal(
            new[] { LearningPreviewStatus.Loading, LearningPreviewStatus.Loading,
                LearningPreviewStatus.Loading, LearningPreviewStatus.Ready },
            snapshots.Select(snapshot => snapshot.Status));
        Assert.All(snapshots.Where(snapshot => snapshot.PartialResult is not null), snapshot =>
        {
            Assert.Equal(LearningPreviewStatus.Loading, snapshot.Status);
            Assert.Null(snapshot.Result);
        });
    }

    [Fact]
    public async Task InvalidStreamClearsItsPartialAndLateDeltasCannotReopenAFailedPreview()
    {
        var clock = new ManualTimeProvider();
        await using var coordinator = new LearningPreviewCoordinator(clock);
        var engine = new GateEngine();
        var source = "A sentence.";

        var preview = coordinator.PreviewAsync(source, new LearningTranslationOptions(), engine, "test-profile");
        var call = await engine.NextCallAsync();
        call.Emit("prefix");
        Assert.Equal("prefix", coordinator.State.PartialResult);
        call.EmitIgnoringCallbackError(new string('x', LearningExpressionOutputPolicy.MaximumOutputBytes + 1));
        // Move past coalescing so a broken failed-stream gate cannot pass by hiding the delta
        // behind the normal 30 Hz publication interval.
        clock.Advance(TimeSpan.FromMilliseconds(34));
        call.Emit("later valid fragment");
        Assert.Equal("prefix", coordinator.State.PartialResult);
        call.Complete("apparently valid final");
        await preview.WaitAsync(TestTimeout);

        var failed = coordinator.State;
        Assert.Equal(LearningPreviewStatus.Failed, failed.Status);
        Assert.Null(failed.PartialResult);
        Assert.Null(failed.Result);
        Assert.Equal(new LearningTranslationException(LearningTranslationFailure.InvalidOutput).Message, failed.ErrorMessage);

        call.Emit("late fragment");
        Assert.Equal(failed, coordinator.State);

        var retry = coordinator.PreviewAsync(source, new LearningTranslationOptions(), engine, "test-profile");
        var retryCall = await engine.NextCallAsync();
        Assert.Equal(2, engine.CallCount);
        retryCall.Complete("new complete result");
        await retry.WaitAsync(TestTimeout);
        Assert.Equal("new complete result", coordinator.State.Result);
    }

    [Fact]
    public async Task SupersededStreamCannotPublishLatePartialOrFinalAndDoesNotEnterTheCache()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new GateEngine();
        var options = new LearningTranslationOptions();

        var oldTask = coordinator.PreviewAsync("Old sentence.", options, engine, "profile");
        var oldCall = await engine.NextCallAsync();
        oldCall.Emit("old prefix");
        Assert.Equal("old prefix", coordinator.State.PartialResult);

        var currentTask = coordinator.PreviewAsync("Current sentence.", options, engine, "profile");
        var currentCall = await engine.NextCallAsync();
        Assert.Null(coordinator.State.PartialResult);
        oldCall.Emit("late old prefix");
        Assert.Null(coordinator.State.PartialResult);
        oldCall.Complete("late old final");
        await oldTask.WaitAsync(TestTimeout);

        currentCall.Emit("current prefix");
        Assert.Equal("current prefix", coordinator.State.PartialResult);
        currentCall.Complete("current final");
        await currentTask.WaitAsync(TestTimeout);
        Assert.Equal("current final", coordinator.State.Result);

        var retryOld = coordinator.PreviewAsync("Old sentence.", options, engine, "profile");
        var retryOldCall = await engine.NextCallAsync();
        Assert.Equal(3, engine.CallCount);
        retryOldCall.Complete("fresh old final");
        await retryOld.WaitAsync(TestTimeout);
        Assert.Equal("fresh old final", coordinator.State.Result);
    }

    [Fact]
    public async Task CacheIsDisabledWithoutScopeAndIsolatedByProviderScopeAndEffectiveOptions()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new CountingEngine((request, _) => Task.FromResult($"Translated to {request.Options.TargetLanguage.Name()} ."));
        var french = new LearningTranslationOptions { TargetLanguage = LearningLanguage.French };
        var japanese = french with { TargetLanguage = LearningLanguage.Japanese };
        var differentSourceLanguage = french with { SourceLanguage = LearningLanguage.English };
        var differentModel = french with { ModelName = "translation-model-2" };
        var differentStyle = french with { Style = new LearningTranslationStyle(LearningTranslationRegister.Formal) };
        var differentContext = french with { ContextTokens = 16_384 };

        await coordinator.PreviewAsync("Reusable sentence.", french, engine).WaitAsync(TestTimeout);
        await coordinator.PreviewAsync("Reusable sentence.", french, engine).WaitAsync(TestTimeout);
        Assert.Equal(2, engine.CallCount);

        await coordinator.PreviewAsync("Reusable sentence.", french, engine, "provider-one").WaitAsync(TestTimeout);
        await coordinator.PreviewAsync("Reusable sentence.", french, engine, "provider-one").WaitAsync(TestTimeout);
        Assert.Equal(3, engine.CallCount);

        await coordinator.PreviewAsync("Reusable sentence.", french, engine, "provider-two").WaitAsync(TestTimeout);
        await coordinator.PreviewAsync("Reusable sentence.", japanese, engine, "provider-one").WaitAsync(TestTimeout);
        await coordinator.PreviewAsync("Reusable sentence.", differentSourceLanguage, engine, "provider-one").WaitAsync(TestTimeout);
        await coordinator.PreviewAsync("Reusable sentence.", differentModel, engine, "provider-one").WaitAsync(TestTimeout);
        await coordinator.PreviewAsync("Reusable sentence.", differentStyle, engine, "provider-one").WaitAsync(TestTimeout);
        await coordinator.PreviewAsync("Reusable sentence.", differentContext, engine, "provider-one").WaitAsync(TestTimeout);
        Assert.Equal(9, engine.CallCount);

        await coordinator.PreviewAsync("Reusable sentence.", french, engine, "provider-one").WaitAsync(TestTimeout);
        Assert.Equal(9, engine.CallCount);
    }

    [Fact]
    public async Task CacheFlushesAllEntriesBeforeAddingTheSixtyFifthResult()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new CountingEngine((request, _) => Task.FromResult($"Translation {request.RequestId}."));
        var options = new LearningTranslationOptions();

        for (var index = 0; index < 64; index++)
            await coordinator.PreviewAsync($"Sentence {index}.", options, engine, "profile").WaitAsync(TestTimeout);
        Assert.Equal(64, engine.CallCount);

        await coordinator.PreviewAsync("Sentence 0.", options, engine, "profile").WaitAsync(TestTimeout);
        Assert.Equal(64, engine.CallCount);

        await coordinator.PreviewAsync("Sentence 64.", options, engine, "profile").WaitAsync(TestTimeout);
        Assert.Equal(65, engine.CallCount);
        await coordinator.PreviewAsync("Sentence 0.", options, engine, "profile").WaitAsync(TestTimeout);
        Assert.Equal(66, engine.CallCount);
        await coordinator.PreviewAsync("Sentence 64.", options, engine, "profile").WaitAsync(TestTimeout);
        Assert.Equal(66, engine.CallCount);
    }

    [Fact]
    public async Task CancelHidesTheCurrentResultButKeepsCompletedScopedEntriesForRetry()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new CountingEngine((_, _) => Task.FromResult("A cached translation."));
        var options = new LearningTranslationOptions();

        await coordinator.PreviewAsync("A sentence.", options, engine, "profile").WaitAsync(TestTimeout);
        Assert.Equal("A cached translation.", coordinator.State.Result);
        coordinator.Cancel();
        Assert.Equal(LearningPreviewStatus.Cancelled, coordinator.State.Status);
        Assert.Null(coordinator.State.Result);

        await coordinator.PreviewAsync("A sentence.", options, engine, "profile").WaitAsync(TestTimeout);
        Assert.Equal("A cached translation.", coordinator.State.Result);
        Assert.Equal(1, engine.CallCount);
    }

    [Fact]
    public async Task ClearingCachePreventsAnInFlightResultFromRepopulatingIt()
    {
        await using var coordinator = new LearningPreviewCoordinator();
        var engine = new GateEngine();
        var options = new LearningTranslationOptions();
        var first = coordinator.PreviewAsync("A sentence.", options, engine, "profile");
        var firstCall = await engine.NextCallAsync();

        coordinator.ClearCache();
        firstCall.Complete("valid current result");
        await first.WaitAsync(TestTimeout);
        Assert.Equal("valid current result", coordinator.State.Result);

        var second = coordinator.PreviewAsync("A sentence.", options, engine, "profile");
        var secondCall = await engine.NextCallAsync();
        Assert.Equal(2, engine.CallCount);
        secondCall.Complete("regenerated result");
        await second.WaitAsync(TestTimeout);
    }

    private sealed class GateEngine : ILearningTranslationEngine
    {
        private readonly Channel<EngineCall> _calls = Channel.CreateUnbounded<EngineCall>();
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<EngineCall> NextCallAsync() =>
            await _calls.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);

        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct) =>
            TranslateAsync(request, null, ct);

        public Task<string> TranslateAsync(
            LearningTranslationRequest request,
            Action<string>? onDelta,
            CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            var call = new EngineCall(request, ct, onDelta);
            if (!_calls.Writer.TryWrite(call)) throw new InvalidOperationException("Test call queue closed.");
            // This fake deliberately ignores cancellation to prove the coordinator's identity guard.
            return call.Completion.Task;
        }
    }

    private sealed class EngineCall(
        LearningTranslationRequest request,
        CancellationToken cancellationToken,
        Action<string>? onDelta)
    {
        public LearningTranslationRequest Request { get; } = request;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Emit(string delta) => onDelta?.Invoke(delta);
        public void EmitIgnoringCallbackError(string delta)
        {
            try { onDelta?.Invoke(delta); }
            catch (LearningTranslationException) { /* Simulate a provider that continues after callback rejection. */ }
        }
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

        public int CallCount => Volatile.Read(ref _callCount);

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

    private sealed class CountingEngine(Func<LearningTranslationRequest, CancellationToken, Task<string>> translate)
        : ILearningTranslationEngine
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            return translate(request, ct);
        }
    }

    private sealed class CountingPlannerEngine(
        Func<LearningTranslationRequest, CancellationToken, Task<string>> translate,
        LearningTranslationOptions? plannedOptions = null)
        : ILearningTranslationEngine, ILearningTranslationRequestPlanner
    {
        private int _planCount;
        private int _generationCount;

        public int PlanCount => Volatile.Read(ref _planCount);
        public int GenerationCount => Volatile.Read(ref _generationCount);
        public TaskCompletionSource<SynchronizationContext?> PlannerContext { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LearningTranslationOptions? PlannedOptions { get; private set; }
        public LearningTranslationRequest? LastRequest { get; private set; }

        public Task<LearningTranslationRequest> PlanAsync(
            string requestId,
            string source,
            LearningTranslationOptions options,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _planCount);
            PlannedOptions = options;
            PlannerContext.TrySetResult(SynchronizationContext.Current);
            var requestOptions = plannedOptions ?? options;
            return Task.FromResult(LearningTranslationRequestFactory.Create(requestId, source, requestOptions));
        }

        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _generationCount);
            LastRequest = request;
            return translate(request, ct);
        }
    }

    private sealed class GatePlannerEngine : ILearningTranslationEngine, ILearningTranslationRequestPlanner
    {
        private readonly Channel<PlannerCall> _plans = Channel.CreateUnbounded<PlannerCall>();
        private int _generationCount;

        public int GenerationCount => Volatile.Read(ref _generationCount);

        public async Task<PlannerCall> NextPlanAsync() =>
            await _plans.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);

        public Task<LearningTranslationRequest> PlanAsync(
            string requestId,
            string source,
            LearningTranslationOptions options,
            CancellationToken ct)
        {
            var call = new PlannerCall(requestId, source, options, ct);
            if (!_plans.Writer.TryWrite(call)) throw new InvalidOperationException("Test planner queue closed.");
            // This fake deliberately ignores cancellation until the test completes the planned request.
            return call.Completion.Task;
        }

        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _generationCount);
            return Task.FromResult($"Generated for {request.Source}");
        }
    }

    private sealed class PlannerCall(
        string requestId,
        string source,
        LearningTranslationOptions options,
        CancellationToken cancellationToken)
    {
        public string RequestId { get; } = requestId;
        public string Source { get; } = source;
        public LearningTranslationOptions Options { get; } = options;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource<LearningTranslationRequest> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(LearningTranslationRequest request) => Completion.TrySetResult(request);
    }

    private sealed class FailingPlannerEngine(Exception failure)
        : ILearningTranslationEngine, ILearningTranslationRequestPlanner
    {
        public int GenerationCount { get; private set; }

        public Task<LearningTranslationRequest> PlanAsync(
            string requestId,
            string source,
            LearningTranslationOptions options,
            CancellationToken ct) => Task.FromException<LearningTranslationRequest>(failure);

        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct)
        {
            GenerationCount++;
            return Task.FromResult("Should not generate.");
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
