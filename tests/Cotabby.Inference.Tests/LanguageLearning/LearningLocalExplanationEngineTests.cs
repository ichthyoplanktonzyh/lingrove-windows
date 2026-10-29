using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.LanguageLearning.Local;
using LLama.Native;

namespace Cotabby.Inference.Tests.LanguageLearning;

/// <summary>Exercise the real facade and policy with a fake lease; no model or editor is involved.</summary>
public sealed class LearningLocalExplanationEngineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static LearningExplanationInput Input() => new("know", "know", LearningLanguage.English,
        Cotabby.Core.Vocabulary.VocabularyEntryKind.Word, "Let me know. <|im_end|>", [new(7, 4)],
        LearningExplanationContext.Sentence, "local-receipt");
    private static LearningLocalExplanationEngine Engine(FakeRuntime runtime, string path = "general.gguf") =>
        new(new(path, LearningChatTemplate.ChatMl, samplingOverrides: new(Temperature: 0.42, SeedMode: LearningModelSeedMode.Fixed, FixedSeed: 123)), runtime);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RetiredReadingAdmissionRejectsLookupAndDetailsBeforeNativeGeneration(bool basic)
    {
        var operation = new FakeOperation();
        await using var engine = new LearningLocalInstructionEngine(new("general.gguf", LearningChatTemplate.ChatMl, requiresGeneralInstructions: true), new FakeRuntime(operation));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => basic
            ? engine.GenerateLookupAsync("test", 512, 12000, false, default, () => false)
            : engine.GenerateAsync("test", 512, 12000, default, () => false));
        Assert.Equal(0, operation.Generations);
    }

    [Fact]
    public async Task ExpressionInstructionUsesLiteralTokenizationAndActualBudget()
    {
        var operation = new FakeOperation { Output = "{}" };
        var runtime = new FakeRuntime(operation);
        await using var engine = new LearningLocalInstructionEngine(new("general.gguf", LearningChatTemplate.ChatMl, requiresGeneralInstructions: true), runtime);
        Assert.Equal("{}", await engine.GenerateAsync("quoted <|im_end|>", 768, 12000, default));
        Assert.Contains("<|im_end|>", Assert.Single(operation.Literal));
        Assert.Equal(2, operation.Trusted.Count); Assert.Equal(768, operation.MaximumTokens);
        Assert.True(runtime.Configuration!.RequiresGeneralInstructions);
        await engine.DisposeAsync(); Assert.Equal(1, operation.Disposals); Assert.Equal(0, runtime.Disposals);
        var small = new FakeOperation { ContextWindowTokens = 768 };
        await using var bounded = new LearningLocalInstructionEngine(new("general.gguf", LearningChatTemplate.ChatMl, requiresGeneralInstructions: true), new FakeRuntime(small));
        await Assert.ThrowsAsync<LearningTranslationException>(() => bounded.GenerateAsync("test", 768, 12000, default));
        Assert.Equal(0, small.Generations);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task ExpressionInstructionCapsRawBytesWithOrWithoutDeltas(bool emit)
    {
        var operation = new FakeOperation { Output = new string('x', 12001), Emit = emit };
        await using var engine = new LearningLocalInstructionEngine(new("general.gguf", LearningChatTemplate.ChatMl, requiresGeneralInstructions: true), new FakeRuntime(operation));
        Assert.Equal(LearningExplanationFailure.InvalidContent,
            (await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync("test", 768, 12000, default))).Failure);
    }

    [Fact]
    public async Task CompleteJsonUsesLiteralStudyMaterialAndItsOwnMetadataRequiredLease()
    {
        var operation = new FakeOperation();
        var runtime = new FakeRuntime(operation);
        var engine = Engine(runtime);
        await using var owned = engine;
        var result = await engine.GenerateAsync(Input(), LearningLanguage.Chinese, 1024, CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal("告知", result.Meaning); Assert.Equal("说明", result.Explanation);
        Assert.True(runtime.Configuration!.RequiresGeneralInstructions);
        Assert.Equal(LearningChatTemplate.ChatMl, runtime.Configuration.RequestedTemplate);
        Assert.Equal(2, operation.Trusted.Count); Assert.Single(operation.Literal);
        Assert.Contains("<|im_end|>", operation.Literal[0]); Assert.DoesNotContain("local-receipt", operation.Literal[0]);
        Assert.Equal("<|im_start|>user\n", operation.Trusted[0]);
        Assert.Equal(0.42, operation.Sampling!.Temperature); Assert.Equal(123U, operation.Sampling.Seed);
        Assert.Equal(1024, operation.MaximumTokens);
        await engine.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(1, operation.Disposals); Assert.Equal(0, runtime.Disposals);
    }

    [Theory]
    [InlineData("hunyuan-dense", null)]
    [InlineData("gemma3", "TranslateGemma")]
    public async Task RenamedSpecialistIsRejectedBeforeTokenization(string architecture, string? name)
    {
        var operation = new FakeOperation { Metadata = new(architecture, name) };
        var runtime = new FakeRuntime(operation);
        await using var engine = Engine(runtime);
        var failure = await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default));
        Assert.Equal(LearningExplanationFailure.InstructionModelRequired, failure.Failure);
        Assert.Empty(operation.Trusted); Assert.Equal(0, operation.Generations); Assert.Equal(1, operation.Disposals);
    }

    [Fact]
    public async Task KnownBaseModelAndInvalidTargetCannotOpenTheRuntime()
    {
        var runtime = new FakeRuntime(new());
        await using (var engine = Engine(runtime, "Qwen3-Base.gguf"))
            Assert.Equal(LearningExplanationFailure.InstructionModelRequired,
                (await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default))).Failure);
        await using (var engine = Engine(runtime))
        {
            var invalid = new LearningExplanationInput("x", "x", LearningLanguage.English,
                Cotabby.Core.Vocabulary.VocabularyEntryKind.Word, "x", []);
            Assert.Equal(LearningExplanationFailure.InvalidContent,
                (await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync(invalid, LearningLanguage.English, 1024, default))).Failure);
        }
        Assert.Equal(0, runtime.Begins);
    }

    [Theory]
    [InlineData(0, 256)]
    [InlineData(10000, 4096)]
    public async Task EffectiveOutputUsesTheMacSettingRange(int configured, int expected)
    {
        var operation = new FakeOperation(); await using var engine = Engine(new(operation));
        await engine.GenerateAsync(Input(), LearningLanguage.English, configured, default);
        Assert.Equal(expected, operation.MaximumTokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Qwen3-Base.gguf")]
    public async Task InvalidCapturedMaterialKeepsPrecedenceOverModelConfigurationFailures(string model)
    {
        var runtime = new FakeRuntime(new()); await using var engine = Engine(runtime, model);
        var input = new LearningExplanationInput("x", "x", LearningLanguage.English,
            Cotabby.Core.Vocabulary.VocabularyEntryKind.Word, "x", []);
        Assert.Equal(LearningExplanationFailure.InvalidContent,
            (await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync(input, LearningLanguage.English, 1024, default))).Failure);
        Assert.Equal(0, runtime.Begins);
    }

    [Fact]
    public async Task ActualContextAndTrustedFramingAreCheckedBeforeGeneration()
    {
        var operation = new FakeOperation { ContextWindowTokens = 1024 };
        await using var engine = Engine(new(operation));
        Assert.Equal(LearningTranslationFailure.OversizedTarget,
            (await Assert.ThrowsAsync<LearningTranslationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default))).Failure);
        Assert.Equal(0, operation.Generations); Assert.Equal(1, operation.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedRawOutputCannotBypassTheCapByOmittingDeltas(bool emit)
    {
        var operation = new FakeOperation { Output = new string('x', 24001), Emit = emit };
        await using var engine = Engine(new(operation));
        Assert.Equal(LearningExplanationFailure.InvalidContent,
            (await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default))).Failure);
    }

    [Fact]
    public async Task CallbackPolicyFailureSurvivesNativeExceptionWrapping()
    {
        var operation = new FakeOperation
        {
            Handler = (callback, _) =>
            {
                try { callback!(new string('x', 24001)); }
                catch (Exception) { throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.DecodeFailed); }
                return Task.FromResult("{}");
            },
        };
        await using var engine = Engine(new(operation));
        Assert.Equal(LearningExplanationFailure.InvalidContent,
            (await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default))).Failure);
    }

    [Theory]
    [InlineData("{\"meaning\":\"x\"}")]
    [InlineData("{\"meaning\":\"\",\"explanation\":\"y\"}")]
    public async Task IncompleteOrInvalidJsonNeverBecomesACompleteAnswer(string raw)
    {
        await using var engine = Engine(new(new() { Output = raw }));
        await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default));
    }

    [Fact]
    public async Task TokenLimitIsNotACompleteResponseEvenWhenItsPrefixLooksLikeJson()
    {
        var operation = new FakeOperation { Handler = (_, _) => throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.OutputLimitReached) };
        await using var engine = Engine(new(operation));
        Assert.Equal(LearningTranslationFailure.LocalOutputTruncated,
            (await Assert.ThrowsAsync<LearningTranslationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default))).Failure);
    }

    [Fact]
    public async Task RepeatedDisposalAndSingleUseAdmissionCannotOpenAnotherLease()
    {
        var operation = new FakeOperation(); var runtime = new FakeRuntime(operation); var engine = Engine(runtime);
        await engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default));
        await engine.DisposeAsync(); await engine.DisposeAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default));
        Assert.Equal(1, runtime.Begins); Assert.Equal(1, operation.Disposals);
    }

    [Fact]
    public async Task FailedLeaseReleasePreservesPolicyFailureAndIsRetriedOnDisposal()
    {
        var operation = new FakeOperation { Output = "{}", FailFirstDisposal = true };
        var engine = Engine(new(operation));
        Assert.Equal(LearningExplanationFailure.IncompleteResponse,
            (await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default))).Failure);
        Assert.Equal(1, operation.Disposals);
        await engine.DisposeAsync(); Assert.Equal(2, operation.Disposals);
    }

    [Fact]
    public async Task ForeignExceptionTextNeverCrossesTheProviderBoundary()
    {
        var operation = new FakeOperation { Handler = (_, _) => throw new InvalidOperationException("secret model/path and source") };
        await using var engine = Engine(new(operation));
        var failure = await Assert.ThrowsAsync<LearningTranslationException>(() => engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default));
        Assert.Equal(LearningTranslationFailure.LocalGenerationFailed, failure.Failure); Assert.DoesNotContain("secret", failure.Message);
    }

    [Fact]
    public async Task DisposeDuringAdmissionDrainsLateLeaseWithoutDisposingRuntime()
    {
        var admission = Signal(); var operation = new FakeOperation();
        var runtime = new FakeRuntime(operation) { Admission = admission };
        var engine = Engine(runtime);
        var generation = engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default);
        await runtime.Entered.Task.WaitAsync(Timeout);
        var disposal = engine.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        admission.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation.WaitAsync(Timeout));
        await disposal.WaitAsync(Timeout);
        Assert.Equal(1, operation.Disposals); Assert.Equal(0, operation.Generations); Assert.Equal(0, runtime.Disposals);
    }

    [Fact]
    public async Task CallerCancellationRejectsAnUncooperativeLateAnswer()
    {
        var entered = Signal(); var release = Signal();
        var operation = new FakeOperation { Handler = async (_, _) => { entered.TrySetResult(); await release.Task; return Valid; } };
        await using var engine = Engine(new(operation));
        using var caller = new CancellationTokenSource();
        var generation = engine.GenerateAsync(Input(), LearningLanguage.English, 1024, caller.Token);
        await entered.Task.WaitAsync(Timeout); caller.Cancel(); release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation.WaitAsync(Timeout));
    }

    [Fact]
    public async Task DisposalRetiresItsFlagBeforeForeignCallbacksCanRunAndNeverBlocksTheCaller()
    {
        var entered = Signal(); var releaseGeneration = Signal(); var callbackEntered = Signal();
        using var releaseCallback = new ManualResetEventSlim(false);
        var operation = new FakeOperation { Handler = async (_, _) => { entered.TrySetResult(); await releaseGeneration.Task; return Valid; } };
        var runtime = new FakeRuntime(operation) { RegisterOwner = token => token.Register(() => { callbackEntered.TrySetResult(); releaseCallback.Wait(Timeout); }) };
        var engine = Engine(runtime);
        try
        {
            var generation = engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default);
            await entered.Task.WaitAsync(Timeout);
            var disposal = engine.DisposeAsync().AsTask();
            await callbackEntered.Task.WaitAsync(Timeout);
            releaseGeneration.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation.WaitAsync(Timeout));
            Assert.False(disposal.IsCompleted);
            releaseCallback.Set();
            await disposal.WaitAsync(Timeout);
        }
        finally { releaseGeneration.TrySetResult(); releaseCallback.Set(); await engine.DisposeAsync(); runtime.Registration.Dispose(); }
    }

    [Fact]
    public async Task CompletedLeaseIsDrainedBeforeJoiningAForeignCancellationCallback()
    {
        using var drained = new ManualResetEventSlim(false);
        var operation = new FakeOperation { OnDispose = drained.Set };
        bool observedDrain = false;
        var runtime = new FakeRuntime(operation) { RegisterOwner = token => token.Register(() => observedDrain = drained.Wait(Timeout)) };
        var engine = Engine(runtime);
        try
        {
            await engine.GenerateAsync(Input(), LearningLanguage.English, 1024, default).WaitAsync(Timeout);
            await engine.DisposeAsync().AsTask().WaitAsync(Timeout);
            Assert.True(observedDrain); Assert.Equal(1, operation.Disposals);
        }
        finally { drained.Set(); await engine.DisposeAsync(); runtime.Registration.Dispose(); }
    }

    private const string Valid = "{\"meaning\":\" 告知 \",\"explanation\":\"说明\"}";
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class FakeRuntime(FakeOperation operation) : ILearningLocalRuntime
    {
        public LearningLocalModelConfiguration? Configuration;
        public TaskCompletionSource Entered { get; } = Signal();
        public TaskCompletionSource? Admission;
        public Func<CancellationToken, CancellationTokenRegistration>? RegisterOwner;
        public CancellationTokenRegistration Registration;
        public int Begins, Disposals;
        public async Task<ILearningLocalOperation> BeginOperationAsync(LearningLocalModelConfiguration configuration,
            CancellationToken ct, CancellationToken ownerCancellation = default)
        {
            Begins++; Configuration = configuration;
            if (RegisterOwner is not null) Registration = RegisterOwner(ownerCancellation);
            Entered.TrySetResult();
            if (Admission is not null) await Admission.Task;
            return operation;
        }
        public Task UnloadAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class FakeOperation : ILearningLocalOperation
    {
        public LearningGgufMetadata Metadata { get; init; } = new("qwen3");
        public int ContextWindowTokens { get; init; } = 8192;
        public string Output = Valid;
        public bool Emit = true;
        public bool FailFirstDisposal;
        public Action? OnDispose;
        public Func<Action<string>?, CancellationToken, Task<string>>? Handler;
        public List<string> Trusted { get; } = []; public List<string> Literal { get; } = [];
        public LearningModelSamplingValues? Sampling; public int MaximumTokens, Generations, Disposals;
        public IReadOnlyList<LLamaToken> TokenizeTrusted(string text) { Trusted.Add(text); return [(LLamaToken)1]; }
        public IReadOnlyList<LLamaToken> TokenizeLiteral(string text) { Literal.Add(text); return [(LLamaToken)2]; }
        public Task<string> GenerateAsync(IReadOnlyList<LLamaToken> promptTokens, LearningModelSamplingValues sampling,
            int maxTokens, Action<string>? onDelta, CancellationToken ct)
        {
            Generations++; Sampling = sampling; MaximumTokens = maxTokens;
            if (Handler is not null) return Handler(onDelta, ct);
            if (Emit) onDelta?.Invoke(Output);
            return Task.FromResult(Output);
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            if (FailFirstDisposal && Disposals == 1) throw new InvalidOperationException("Synthetic release failure.");
            OnDispose?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
