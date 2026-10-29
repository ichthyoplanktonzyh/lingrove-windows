using System.Threading.Channels;
using Cotabby.App.Learning;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Suggestions;
using Cotabby.Core.Support.Runtime;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Core.Vocabulary;
using Cotabby.Inference.LanguageLearning.Local;
using LLama.Native;

namespace Cotabby.App.Tests.Learning;

/// <summary>Actual non-WPF app owner with deterministic timers and effects; never loads a model.</summary>
public sealed class LearningExplanationControllerTests
{
    private sealed class EndpointInstruction(Func<CancellationToken, Task<string>> generate) : ILearningInstructionEngine
    {
        public Task<string> GenerateAsync(string prompt, int output, int maximumBytes, CancellationToken token, Func<bool>? admission = null)
        { token.ThrowIfCancellationRequested(); if (admission?.Invoke() == false) throw new OperationCanceledException(); return generate(token); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static AppSettings EndpointSettings() => new() { LearningExplanationBackend = LearningModelBackend.Endpoint,
        LearningExplanationEndpointBaseUrl = "https://owned.invalid/v1", LearningExplanationEndpointModel = "general" };

    [Fact]
    public async Task EndpointExplanationAndLookupNeverBorrowTheLocalRuntime()
    {
        var runtime = new FakeRuntime(); var settings = EndpointSettings();
        await using var owner = new LearningExplanationController(runtime, settings, Path.GetTempPath(),
            endpointFactory: _ => new Engine((_, _, _, _) => Task.FromResult(Answer)),
            endpointInstructionFactory: _ => new EndpointInstruction(_ => Task.FromResult("lookup")));
        var record = await owner.Service.GenerateAsync(Input, "owned"); Assert.Equal("general", record.Model);
        var input = new ReadingLookupInput("bank", LearningLanguage.English, LearningLanguage.Chinese, true, null) { Admission = () => true };
        Assert.Equal("lookup", await owner.GenerateReadingLookupAsync(input, null, raw => raw, default));
        Assert.Equal(0, runtime.Begins); Assert.Equal(LocalRuntimeResidency.Released, owner.Residency);
    }

    [Fact]
    public async Task EndpointEditorInstructionsRequireAnExplicitFinalPermit()
    {
        var runtime = new FakeRuntime(); int facades = 0;
        await using var owner = new LearningExplanationController(runtime, EndpointSettings(), Path.GetTempPath(),
            endpointFactory: _ => new Engine((_, _, _, _) => Task.FromResult(Answer)),
            endpointInstructionFactory: _ => { facades++; return new EndpointInstruction(_ => Task.FromResult("unused")); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.GenerateExpressionAsync(Expression, value => value, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.GenerateReadingLookupAsync(
            new("bank", LearningLanguage.English, LearningLanguage.Chinese, true, null), null, raw => raw, default));
        Assert.Equal(0, facades); Assert.Equal(0, runtime.Begins);
    }

    [Fact]
    public async Task LatestInstructionWaitsForThePreviousExplanationFacadeToDrain()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int instructions = 0;
        var owner = new LearningExplanationController(new FakeRuntime(), EndpointSettings(), Path.GetTempPath(),
            endpointFactory: _ => new Engine(async (_, _, _, _) => { entered.TrySetResult(); await release.Task; return Answer; },
                async () => { disposing.TrySetResult(); await disposed.Task; }),
            endpointInstructionFactory: _ => { Interlocked.Increment(ref instructions); return new EndpointInstruction(_ => Task.FromResult("new")); });
        try
        {
            var old = owner.Service.GenerateAsync(Input, "old"); await entered.Task.WaitAsync(TimeoutLimit);
            var input = new ReadingLookupInput("bank", null, LearningLanguage.Chinese, true, null) { Admission = () => true };
            var next = owner.GenerateReadingLookupAsync(input, null, raw => raw, default);
            Assert.Equal(0, Volatile.Read(ref instructions)); release.TrySetResult();
            await disposing.Task.WaitAsync(TimeoutLimit); Assert.Equal(0, Volatile.Read(ref instructions));
            disposed.TrySetResult(); Assert.Equal("new", await next.WaitAsync(TimeoutLimit));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old); Assert.Equal(1, instructions);
        }
        finally { release.TrySetResult(); disposed.TrySetResult(); await owner.DisposeAsync(); }
    }

    [Fact]
    public async Task UnknownReadingLookupUsesConstrainedSharedOwnerAndRetiresOnLanguageChange()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime { Generate = ct => { started.TrySetResult(ct); return pending.Task; } };
        var settings = Settings(); var owner = Owner(runtime, settings, new Clock());
        var input = new ReadingLookupInput("bank", null, LearningLanguage.Chinese, true, null);
        var work = owner.GenerateReadingLookupAsync(input, null, raw => ReadingLookupPolicy.Parse(raw, input, x => x.ToLowerInvariant()), default);
        try
        {
            var token = await started.Task.WaitAsync(TimeoutLimit);
            Assert.False(runtime.LastOperation.LookupContext); Assert.Equal(512, runtime.LastOperation.Output);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => cancelled.TrySetResult());
            settings.LearningExplanationLanguage = LearningLanguage.French; owner.ApplySettings(settings);
            await cancelled.Task.WaitAsync(TimeoutLimit);
            var shutdown = owner.DisposeAsync().AsTask(); Assert.False(shutdown.IsCompleted);
            pending.SetResult("{\"meaning\":\"bank\",\"sentence_translation\":\"\"}");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeoutLimit));
            await shutdown.WaitAsync(TimeoutLimit); Assert.Equal(1, runtime.Disposals);
        }
        finally { pending.TrySetResult("{}"); await owner.DisposeAsync(); }
    }
    private static readonly TimeSpan TimeoutLimit = TimeSpan.FromSeconds(10);
    private static readonly LearningExplanationResult Answer = new("Meaning", "Explanation");
    private static LearningExplanationInput Input => new("know", "know", LearningLanguage.English, VocabularyEntryKind.Word,
        "Let me know.", [new(7, 4)]);
    private static ExpressionAssistRequest Expression => new("排除", "We can ", ".", "", "en", "zh-Hans");
    [Fact]
    public async Task UnconfiguredExpressionModelReturnsAnActionableFailureWithoutRuntimeWork()
    {
        var runtime = new FakeRuntime();
        await using var owner = Owner(runtime, new AppSettings(), new Clock());
        var failure = await Assert.ThrowsAsync<LearningTranslationException>(() => owner.GenerateExpressionAsync(Expression, value => value, default));
        Assert.Equal(LearningTranslationFailure.LocalModelNotConfigured, failure.Failure);
    }
    private static AppSettings Settings()
    {
        var settings = new AppSettings { LearningExplanationLocalModel = "tutor.gguf", LearningExplanationLocalTemplate = LearningChatTemplate.Qwen3 };
        settings.NormalizeModelAssignmentsAndRegisterLegacy();
        return settings;
    }
    private static LearningExplanationController Owner(FakeRuntime runtime, AppSettings settings, Clock clock,
        Func<LearningLocalModelConfiguration, ILearningExplanationEngine>? factory = null, Func<bool>? battery = null) =>
        new(runtime, settings, Path.Combine(Path.GetTempPath(), "lingrove-owner-test"), battery, clock,
            engineFactory: factory ?? (_ => new Engine((_, _, _, _) => Task.FromResult(Answer))));

    [Fact]
    public async Task ShutdownRetiresExpressionWithoutDeltasAndWaitsForNativeDrain()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime { Generate = ct => { started.SetResult(ct); return pending.Task; } };
        var owner = Owner(runtime, Settings(), new Clock());
        var work = owner.GenerateExpressionAsync(Expression, value => value.ToLowerInvariant(), default);
        try
        {
            var token = await started.Task.WaitAsync(TimeoutLimit);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => cancelled.TrySetResult());
            var shutdown = owner.DisposeAsync().AsTask();
            await cancelled.Task.WaitAsync(TimeoutLimit);
            Assert.False(shutdown.IsCompleted); Assert.Equal(0, runtime.Disposals);
            pending.SetResult("{}");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeoutLimit));
            await shutdown.WaitAsync(TimeoutLimit); Assert.Equal(1, runtime.Disposals);
        }
        finally { pending.TrySetResult("{}"); await owner.DisposeAsync(); }
    }

    [Fact]
    public async Task ShutdownSnapshotsConcurrentCompletedInstructionsUnderAdmissionLock()
    {
        for (int pass = 0; pass < 20; pass++)
        {
            var runtime = new FakeRuntime { Generate = _ => Task.FromResult("{\"candidates\":[],\"clarification\":\"?\"}") };
            var owner = Owner(runtime, Settings(), new Clock());
            var jobs = Enumerable.Range(0, 30).Select(_ => owner.GenerateExpressionAsync(Expression, value => value.ToLowerInvariant(), default)).ToArray();
            var shutdown = owner.DisposeAsync().AsTask();
            try { await Task.WhenAll(jobs).WaitAsync(TimeoutLimit); } catch (OperationCanceledException) { }
            await shutdown.WaitAsync(TimeoutLimit);
            Assert.Equal(1, runtime.Disposals);
        }
    }

    [Fact]
    public async Task ExpressionAndExplanationRetireEachOtherAndShutdownDrainsRuntime()
    {
        var runtime = new FakeRuntime(); var settings = Settings();
        var firstStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResult = new TaskCompletionSource<LearningExplanationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expressionStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expressionResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Generate = ct => { expressionStarted.TrySetResult(ct); return expressionResult.Task; };
        int calls = 0;
        var owner = Owner(runtime, settings, new Clock(), _ => new Engine((_, _, _, ct) =>
        { if (Interlocked.Increment(ref calls) == 1) { firstStarted.SetResult(ct); return firstResult.Task; } return Task.FromResult(Answer); }));
        try
        {
            var first = owner.Service.GenerateAsync(Input, "first", refresh: true);
            var firstToken = await firstStarted.Task.WaitAsync(TimeoutLimit);
            var expression = owner.GenerateExpressionAsync(Expression, value => value.ToLowerInvariant(), CancellationToken.None);
            // The 3b controller waits for the old generation AND facade drain before the
            // next instruction starts. Keep both fakes noncooperative to prove that ordering.
            Assert.True(firstToken.IsCancellationRequested); Assert.False(expressionStarted.Task.IsCompleted);
            firstResult.SetResult(Answer);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeoutLimit));
            var expressionToken = await expressionStarted.Task.WaitAsync(TimeoutLimit);
            var expressionRetired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var expressionRegistration = expressionToken.Register(() => expressionRetired.TrySetResult());
            var second = owner.Service.GenerateAsync(Input, "second", refresh: true);
            await expressionRetired.Task.WaitAsync(TimeoutLimit);
            Assert.False(second.IsCompleted);
            expressionResult.SetResult("{\"candidates\":[{\"text\":\"rule out\",\"hint\":\"排除\"}]}");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => expression.WaitAsync(TimeoutLimit));
            await second.WaitAsync(TimeoutLimit);
            runtime.Generate = ct => { finalStarted.TrySetResult(ct); return finalResult.Task; };
            var final = owner.GenerateExpressionAsync(Expression, value => value.ToLowerInvariant(), default);
            var finalToken = await finalStarted.Task.WaitAsync(TimeoutLimit);
            var shutdown = owner.DisposeAsync().AsTask();
            Assert.True(finalToken.IsCancellationRequested); Assert.False(shutdown.IsCompleted); Assert.Equal(0, runtime.Disposals);
            finalResult.SetResult("{}");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => final.WaitAsync(TimeoutLimit));
            await shutdown.WaitAsync(TimeoutLimit); Assert.Equal(1, runtime.Disposals);
        }
        finally { firstResult.TrySetResult(Answer); expressionResult.TrySetResult("{}"); finalResult.TrySetResult("{}"); await owner.DisposeAsync(); }
    }

    [Fact]
    public async Task DisablingRetiresExpressionAndRejectsLateResult()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime { Generate = ct => { started.SetResult(ct); return pending.Task; } };
        var settings = Settings(); await using var owner = Owner(runtime, settings, new Clock());
        var work = owner.GenerateExpressionAsync(Expression, value => value.ToLowerInvariant(), default);
        try
        {
            var token = await started.Task.WaitAsync(TimeoutLimit);
            var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => retired.TrySetResult());
            settings.Enabled = false; owner.ApplySettings(settings);
            await retired.Task.WaitAsync(TimeoutLimit);
            pending.SetResult("{\"candidates\":[{\"text\":\"x\",\"hint\":\"y\"}]}");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeoutLimit));
        }
        finally { pending.TrySetResult("{}"); }
    }

    [Fact]
    public async Task ExpressionUsesSameLocalOwnerAndDefaultOutputAndLeavesEndpointUnavailable()
    {
        var runtime = new FakeRuntime { Generate = _ => Task.FromResult("{\"candidates\":[{\"text\":\"rule out\",\"hint\":\"排除\"}]}") };
        var settings = Settings(); var clock = new Clock();
        await using var owner = Owner(runtime, settings, clock);
        var observations = new List<PerformanceRequestObservation>(); owner.RequestObserved += observations.Add;
        var result = await owner.GenerateExpressionAsync(Expression, value => value.ToLowerInvariant(), CancellationToken.None).WaitAsync(TimeoutLimit);
        Assert.Equal("rule out", Assert.Single(result.Candidates).Text);
        Assert.Equal(768, runtime.LastOperation.Output); Assert.True(runtime.Configuration!.RequiresGeneralInstructions);
        Assert.Equal(1, runtime.LastOperation.Disposals); Assert.Equal(0, runtime.Disposals);
        var observation = Assert.Single(observations); Assert.Equal(ModelRequestSource.ExpressionAssist, observation.Source);
        Assert.Equal(ModelRequestOutcome.Completed, observation.Outcome); Assert.NotNull(observation.UsableResultMs);
        Assert.Equal(TimeSpan.FromSeconds(60), (await clock.NextTimer()).Due);
    }

    [Fact]
    public async Task StartupAndUnrelatedTranslationChangesNeverLoadOrReleaseExplanation()
    {
        var runtime = new FakeRuntime(); var clock = new Clock(); var settings = Settings();
        await using var owner = Owner(runtime, settings, clock);
        Assert.Equal(LocalRuntimeResidency.OnDemand, owner.Residency);
        Assert.Equal(0, runtime.Begins); Assert.Equal(0, runtime.Unloads); Assert.Empty(clock.Timers);
        settings.LearningTranslationEnabled = false; settings.LearningBackend = LearningTranslationBackend.Endpoint;
        settings.LearningLocalModel = "translator.gguf"; settings.ActiveModelId = "completion";
        owner.ApplySettings(settings);
        Assert.Equal(LocalRuntimeResidency.OnDemand, owner.Residency);
        Assert.Equal(0, runtime.Begins); Assert.Equal(0, runtime.Unloads); Assert.Empty(clock.Timers);
    }

    [Fact]
    public async Task CompleteFacadeSchedulesSixtySecondReleaseAndNextRequestCanReload()
    {
        var runtime = new FakeRuntime(); var clock = new Clock(); int calls = 0;
        await using var owner = Owner(runtime, Settings(), clock, _ => new Engine((_, _, _, _) =>
        { calls++; return Task.FromResult(Answer); }));
        await owner.Service.GenerateAsync(Input, "req_first").WaitAsync(TimeoutLimit);
        var first = await clock.NextTimer(); Assert.Equal(TimeSpan.FromSeconds(60), first.Due);
        Assert.Equal(0, runtime.Unloads); first.Fire(); Assert.Equal(1, runtime.Unloads);
        await owner.Service.GenerateAsync(Input, "req_reload", refresh: true).WaitAsync(TimeoutLimit);
        var second = await clock.NextTimer(); Assert.Equal(2, calls);
        first.Fire(); Assert.Equal(1, runtime.Unloads); // A queued disposed callback must be inert.
        second.Fire(); Assert.Equal(2, runtime.Unloads);
    }

    [Fact]
    public async Task RequestParameterEditRestartsIdleDeadlineWithoutUnloadingWeights()
    {
        var runtime = new FakeRuntime(); var clock = new Clock(); var settings = Settings();
        await using var owner = Owner(runtime, settings, clock);
        await owner.Service.GenerateAsync(Input, "req_first").WaitAsync(TimeoutLimit);
        var before = await clock.NextTimer();
        settings.LearningExplanationLocalGenerationProfiles["tutor.gguf"] = new() { MaximumOutputTokens = 768, Sampling = new(Temperature: 0.4) };
        owner.ApplySettings(settings);
        var after = await clock.NextTimer();
        Assert.True(before.Disposed); before.Fire(); Assert.Equal(0, runtime.Unloads);
        Assert.Equal(TimeSpan.FromSeconds(60), after.Due); after.Fire(); Assert.Equal(1, runtime.Unloads);
    }

    [Fact]
    public async Task SamplerEditCancelsActiveWorkButRetainsTheAllocation()
    {
        var runtime = new FakeRuntime(); var clock = new Clock(); var settings = Settings();
        var pending = new TaskCompletionSource<LearningExplanationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var owner = Owner(runtime, settings, clock, _ => new Engine((_, _, _, token) =>
        { started.TrySetResult(token); return pending.Task; }));
        var work = owner.Service.GenerateAsync(Input, "req_parameter");
        try
        {
            var token = await started.Task.WaitAsync(TimeoutLimit);
            settings.LearningExplanationLocalGenerationProfiles["tutor.gguf"] = new() { Sampling = new(Temperature: 0.3) };
            owner.ApplySettings(settings);
            Assert.True(token.IsCancellationRequested); Assert.Equal(0, runtime.Unloads);
            pending.SetResult(Answer);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeoutLimit));
            Assert.Null(owner.Service.Cached(Input)); Assert.Equal(0, runtime.Unloads);
            (await clock.NextTimer()).Fire(); Assert.Equal(1, runtime.Unloads);
        }
        finally { pending.TrySetResult(Answer); }
    }

    [Theory]
    [InlineData("model")]
    [InlineData("context")]
    [InlineData("template")]
    public async Task AllocationEditsRetireOnlyThisOwnerAndInvalidateItsOldTimer(string change)
    {
        var runtime = new FakeRuntime(); var other = new FakeRuntime(); var clock = new Clock(); var settings = Settings();
        await using var owner = Owner(runtime, settings, clock);
        await using var independent = Owner(other, Settings(), new Clock());
        await owner.Service.GenerateAsync(Input, "req_first").WaitAsync(TimeoutLimit);
        var old = await clock.NextTimer();
        if (change == "model") { settings.LearningExplanationLocalModel = "second.gguf"; settings.RecordLocalModelTask("second.gguf", Cotabby.Core.Models.LocalModelTask.Explanation); }
        else if (change == "context") settings.LearningExplanationLocalContextTokensByModel["tutor.gguf"] = 16384;
        else settings.LearningExplanationLocalTemplate = LearningChatTemplate.ChatMl;
        owner.ApplySettings(settings);
        Assert.Equal(1, runtime.Unloads); old.Fire(); Assert.Equal(1, runtime.Unloads);
        Assert.Equal(0, other.Unloads); Assert.Equal(0, other.Begins);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("pause")]
    [InlineData("battery")]
    public async Task GlobalEligibilityRetiresUncooperativeWorkAndResumingCanGenerate(string reason)
    {
        var runtime = new FakeRuntime(); var clock = new Clock(); var settings = Settings(); bool battery = false;
        var pending = new TaskCompletionSource<LearningExplanationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        await using var owner = Owner(runtime, settings, clock, _ => new Engine((_, _, _, token) =>
        { if (++calls != 1) return Task.FromResult(Answer); started.SetResult(token); return pending.Task; }), () => battery);
        var work = owner.Service.GenerateAsync(Input, "req_pending");
        try
        {
            var token = await started.Task.WaitAsync(TimeoutLimit);
            if (reason == "disabled") settings.Enabled = false;
            else if (reason == "pause") settings.Pause = new((DateTimeOffset?)null);
            else battery = true;
            owner.ApplySettings(settings);
            Assert.Equal(LocalRuntimeResidency.Released, owner.Residency); Assert.True(token.IsCancellationRequested);
            Assert.Equal(1, runtime.Unloads); pending.SetResult(Answer);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeoutLimit));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.Service.GenerateAsync(Input, "req_blocked").WaitAsync(TimeoutLimit));
            Assert.Equal(1, calls); Assert.Null(owner.Service.Cached(Input));
            settings.Enabled = true; settings.Pause = null; battery = false; owner.ApplySettings(settings);
            Assert.Equal(LocalRuntimeResidency.OnDemand, owner.Residency);
            Assert.Equal(Answer, (await owner.Service.GenerateAsync(Input, "req_resumed").WaitAsync(TimeoutLimit)).Result);
            Assert.Equal(2, calls);
        }
        finally { pending.TrySetResult(Answer); }
    }

    [Fact]
    public async Task LiveBatteryCheckRejectsOutputEvenBeforeThePowerEventArrives()
    {
        var runtime = new FakeRuntime(); var clock = new Clock(); bool battery = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<LearningExplanationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var owner = Owner(runtime, Settings(), clock, _ => new Engine((_, _, _, _) =>
        { started.SetResult(); return pending.Task; }), () => battery);
        var work = owner.Service.GenerateAsync(Input, "req_power");
        try
        {
            await started.Task.WaitAsync(TimeoutLimit); battery = true; pending.SetResult(Answer);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeoutLimit));
            Assert.Null(owner.Service.Cached(Input)); Assert.Equal(1, runtime.Unloads);
        }
        finally { pending.TrySetResult(Answer); }
    }

    [Fact]
    public async Task ExpiredPauseDoesNotNeedAPowerEventToAllowTheNextExplicitRequest()
    {
        var runtime = new FakeRuntime(); var clock = new Clock(); var settings = Settings();
        settings.Pause = new(clock.GetUtcNow().AddHours(1));
        await using var owner = Owner(runtime, settings, clock);
        Assert.Equal(LocalRuntimeResidency.Released, owner.Residency);
        clock.Now = clock.Now.AddHours(2);
        Assert.Equal(Answer, (await owner.Service.GenerateAsync(Input, "req_expired").WaitAsync(TimeoutLimit)).Result);
        Assert.Equal(LocalRuntimeResidency.OnDemand, owner.Residency);
    }

    [Fact]
    public async Task FailedLoadStillSchedulesReleaseForPossibleNativeMemory()
    {
        var runtime = new FakeRuntime(); var clock = new Clock();
        await using var owner = Owner(runtime, Settings(), clock,
            _ => new Engine((_, _, _, _) => Task.FromException<LearningExplanationResult>(new IOException("private model path"))));
        var error = await Assert.ThrowsAsync<LearningExplanationException>(() => owner.Service.GenerateAsync(Input, "req_failed").WaitAsync(TimeoutLimit));
        Assert.DoesNotContain("private", error.Message);
        (await clock.NextTimer()).Fire(); Assert.Equal(1, runtime.Unloads); Assert.Null(owner.Service.Cached(Input));
    }

    [Theory]
    [InlineData("../outside.gguf")]
    [InlineData("folder\\outside.gguf")]
    [InlineData("C:outside.gguf")]
    [InlineData("not-a-model.txt")]
    public async Task InstalledSelectionCannotBecomeAnArbitraryModelPath(string name)
    {
        var runtime = new FakeRuntime(); var clock = new Clock(); var settings = Settings(); settings.LearningExplanationLocalModel = name;
        int calls = 0;
        await using var owner = Owner(runtime, settings, clock, _ => { calls++; throw new InvalidOperationException(); });
        var failure = await Assert.ThrowsAsync<LearningTranslationException>(() => owner.Service.GenerateAsync(Input, "req_path").WaitAsync(TimeoutLimit));
        Assert.Equal(LearningTranslationFailure.LocalModelNotConfigured, failure.Failure); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task UnassignedOrIncompatibleSelectionCannotOpenTheExplanationRuntime()
    {
        var runtime = new FakeRuntime(); var settings = Settings(); settings.LearningExplanationLocalModel = "other.gguf";
        int calls = 0;
        await using var owner = Owner(runtime, settings, new Clock(), _ => { calls++; return new Engine((_, _, _, _) => Task.FromResult(Answer)); });
        await Assert.ThrowsAsync<LearningTranslationException>(() => owner.Service.GenerateAsync(Input, "req_unassigned", refresh: true).WaitAsync(TimeoutLimit));
        Assert.Equal(0, calls);
        settings.RecordLocalModelTask("other.gguf", Cotabby.Core.Models.LocalModelTask.Explanation); owner.ApplySettings(settings);
        Assert.Equal(Answer, (await owner.Service.GenerateAsync(Input, "req_assigned", refresh: true).WaitAsync(TimeoutLimit)).Result);
        Assert.Equal(1, calls);
        settings.LearningExplanationLocalModel = "Hy-MT2-1.8B-Q4_K_M.gguf"; owner.ApplySettings(settings);
        await Assert.ThrowsAsync<LearningExplanationException>(() => owner.Service.GenerateAsync(Input, "req_incompatible", refresh: true).WaitAsync(TimeoutLimit));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ProductionFacadeReceivesTheIndependentConfigurationAndMetadataRequirement()
    {
        var runtime = new FakeRuntime(); var clock = new Clock(); var settings = Settings();
        settings.LearningExplanationLocalContextTokensByModel["tutor.gguf"] = 4096;
        settings.LearningExplanationLocalGenerationProfiles["tutor.gguf"] = new() { MaximumOutputTokens = 768 };
        await using var owner = new LearningExplanationController(runtime, settings, Path.GetTempPath(), clock: clock);
        var record = await owner.Service.GenerateAsync(Input, "req_real_facade").WaitAsync(TimeoutLimit);
        Assert.Equal(Answer, record.Result); Assert.Equal(1, runtime.Begins);
        Assert.NotNull(runtime.Configuration); Assert.True(runtime.Configuration.RequiresGeneralInstructions);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "tutor.gguf"), runtime.Configuration.ModelPath);
        Assert.Equal(4096, runtime.Configuration.ContextWindowTokens); Assert.Equal(768, runtime.LastOperation.Output);
        Assert.Equal(1, runtime.LastOperation.Disposals); Assert.Equal(0, runtime.Disposals);
    }

    [Fact]
    public async Task ShutdownClosesSavedAndGenerationAdmissionThenDrainsFacadeBeforeRuntime()
    {
        var runtime = new FakeRuntime(); var clock = new Clock();
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = Owner(runtime, Settings(), clock, _ => new Engine((_, _, _, _) => Task.FromResult(Answer), async () =>
        { disposing.SetResult(); await drain.Task; }));
        var work = owner.Service.GenerateAsync(Input, "req_shutdown");
        try
        {
            await disposing.Task.WaitAsync(TimeoutLimit);
            var shutdown = owner.DisposeAsync().AsTask();
            Assert.False(shutdown.IsCompleted); Assert.Equal(0, runtime.Disposals);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.Service.GenerateAsync(Input, "req_closed"));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.Service.LoadSavedAsync(Input));
            Assert.Throws<ObjectDisposedException>(() => owner.Service.SavedRecord(Input));
            drain.SetResult(); await shutdown.WaitAsync(TimeoutLimit);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeoutLimit));
            await owner.DisposeAsync(); Assert.Equal(1, runtime.Disposals); Assert.Null(owner.Service.Cached(Input));
        }
        finally { drain.TrySetResult(); await owner.DisposeAsync().AsTask().WaitAsync(TimeoutLimit); }
    }

    private sealed class Engine(Func<LearningExplanationInput, LearningLanguage, int, CancellationToken, Task<LearningExplanationResult>> generate,
        Func<Task>? dispose = null) : ILearningExplanationEngine
    {
        public Task<LearningExplanationResult> GenerateAsync(LearningExplanationInput input, LearningLanguage responseLanguage, int maximumOutputTokens, CancellationToken ct) =>
            generate(input, responseLanguage, maximumOutputTokens, ct);
        public ValueTask DisposeAsync() => new(dispose?.Invoke() ?? Task.CompletedTask);
    }

    private sealed class FakeRuntime : ILearningLocalRuntime
    {
        public Func<CancellationToken, Task<string>>? Generate;
        public int Begins, Unloads, Disposals;
        public LearningLocalModelConfiguration? Configuration;
        public Operation LastOperation { get; private set; } = new();
        public Task UnloadAsync() { Interlocked.Increment(ref Unloads); return Task.CompletedTask; }
        public Task<ILearningLocalOperation> BeginOperationAsync(LearningLocalModelConfiguration configuration, CancellationToken ct, CancellationToken ownerCancellation = default)
        {
            ct.ThrowIfCancellationRequested(); ownerCancellation.ThrowIfCancellationRequested();
            Configuration = configuration; Interlocked.Increment(ref Begins); LastOperation = new() { Generate = Generate };
            return Task.FromResult<ILearningLocalOperation>(LastOperation);
        }
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }
    private sealed class Operation : ILearningLocalOperation
    {
        public Func<CancellationToken, Task<string>>? Generate;
        public int Output, Disposals;
        public bool? LookupContext;
        public LearningGgufMetadata Metadata => new("qwen3");
        public int ContextWindowTokens => 4096;
        public IReadOnlyList<LLamaToken> TokenizeTrusted(string text) => text.Length == 0 ? [] : new LLamaToken[1];
        public IReadOnlyList<LLamaToken> TokenizeLiteral(string text) => text.Length == 0 ? [] : new LLamaToken[1];
        public Task<string> GenerateAsync(IReadOnlyList<LLamaToken> promptTokens, LearningModelSamplingValues sampling, int maxTokens, Action<string>? onDelta, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Output = maxTokens;
            if (Generate is not null) return Generate(ct);
            const string raw = "{\"meaning\":\"Meaning\",\"explanation\":\"Explanation\"}"; onDelta?.Invoke(raw); return Task.FromResult(raw);
        }
        public Task<string> GenerateLookupAsync(IReadOnlyList<LLamaToken> promptTokens, LearningModelSamplingValues sampling, int maxTokens, Action<string>? onDelta, bool hasContext, CancellationToken ct)
        { LookupContext = hasContext; return GenerateAsync(promptTokens, sampling, maxTokens, onDelta, ct); }
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }
    private sealed class Clock : TimeProvider
    {
        private readonly Channel<ManualTimer> _created = Channel.CreateUnbounded<ManualTimer>();
        public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        public List<ManualTimer> Timers { get; } = [];
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime); lock (Timers) Timers.Add(timer);
            _created.Writer.TryWrite(timer); return timer;
        }
        public Task<ManualTimer> NextTimer() => _created.Reader.ReadAsync().AsTask().WaitAsync(TimeoutLimit);
    }
    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        public TimeSpan Due { get; } = due;
        public bool Disposed { get; private set; }
        // Fire deliberately ignores disposal: real timers can have a callback already queued.
        public void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
