using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.LanguageLearning.Local;
using LLama.Native;

namespace Cotabby.Inference.Tests.LanguageLearning;

/// <summary>
/// Drives the production learning token loop with fake native sessions. These tests cover
/// cancellation and ownership boundaries without downloading or loading a GGUF model.
/// </summary>
public sealed class LearningLocalRuntimeTests
{
    private static readonly LearningLocalModelConfiguration Configuration =
        new("test-model.gguf", contextWindowTokens: 4_096);

    private static readonly LearningModelSamplingValues Sampling =
        new(Temperature: 0.7, TopP: 0.6, TopK: 20, RepetitionPenalty: 1.05, Seed: 123);

    [Fact]
    public async Task TrustedAndLiteralSegmentsUseDifferentTokenizerModes()
    {
        var session = new FakeNativeSession();
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        var trusted = operation.TokenizeTrusted("<|im_start|>user");
        var literal = operation.TokenizeLiteral("<|endoftext|>");

        Assert.Equal([true, false], session.TokenizeCalls.Select(call => call.AllowSpecialTokens));
        Assert.Equal("<|im_start|>user", session.TokenizeCalls[0].Text);
        Assert.Equal("<|endoftext|>", session.TokenizeCalls[1].Text);
        Assert.Equal(1, (int)Assert.Single(trusted));
        Assert.Equal(0, (int)Assert.Single(literal));
    }

    [Fact]
    public async Task EogIsTheOnlySuccessfulCompleteResult()
    {
        var session = new FakeNativeSession { BatchSize = 2 };
        session.EnqueueSampleScript((LLamaToken)1, (LLamaToken)(-1));
        session.TokenPieces[1] = Encoding.UTF8.GetBytes("你好");
        var deltas = new List<string>();
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        string result = await operation.GenerateAsync(
            Tokens(5), Sampling, maxTokens: 4, deltas.Add, CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal("你好", result);
        Assert.Equal(["你好"], deltas);
        Assert.Equal([(0, 2, false), (2, 2, false), (4, 1, true)], session.PromptBatches);
        Assert.Equal(Sampling, Assert.Single(session.SamplingProfiles));
        Assert.True(Assert.Single(session.Samplers).WasDisposed);
    }

    [Fact]
    public async Task EmptyPromptAndNonPositiveOutputAreRejectedBeforeNativeWork()
    {
        var session = new FakeNativeSession();
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        var emptyPrompt = await Assert.ThrowsAsync<LearningLocalRuntimeException>(() =>
            operation.GenerateAsync(Tokens(0), Sampling, 1, null, CancellationToken.None));
        var emptyOutput = await Assert.ThrowsAsync<LearningLocalRuntimeException>(() =>
            operation.GenerateAsync(Tokens(1), Sampling, 0, null, CancellationToken.None));

        Assert.Equal(LearningLocalRuntimeFailure.InvalidRequest, emptyPrompt.Failure);
        Assert.Equal(LearningLocalRuntimeFailure.InvalidRequest, emptyOutput.Failure);
        Assert.Empty(session.PromptBatches);
        Assert.Equal(0, session.ClearMemoryCount);
        Assert.Empty(session.Samplers);
    }

    [Fact]
    public async Task InvalidSamplerIsRejectedBeforeNativeWork()
    {
        var session = new FakeNativeSession();
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        var failure = await Assert.ThrowsAsync<LearningLocalRuntimeException>(() =>
            operation.GenerateAsync(Tokens(1), Sampling with { Temperature = double.NaN }, 1, null, CancellationToken.None));

        Assert.Equal(LearningLocalRuntimeFailure.InvalidRequest, failure.Failure);
        Assert.Empty(session.PromptBatches);
        Assert.Equal(0, session.ClearMemoryCount);
        Assert.Empty(session.Samplers);
    }

    [Fact]
    public async Task PromptAndOutputThatEqualActualContextAreRejectedBeforeFirstDecode()
    {
        // The selected setting requests 4,096 tokens, but the runtime reports the loaded context
        // size. Validation must use that actual value before clearing KV or issuing any batch.
        var session = new FakeNativeSession { ContextWindowTokens = 4 };
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        var failure = await Assert.ThrowsAsync<LearningLocalRuntimeException>(() =>
            operation.GenerateAsync(Tokens(2), Sampling, 2, null, CancellationToken.None));

        Assert.Equal(LearningLocalRuntimeFailure.PromptBudgetExceeded, failure.Failure);
        Assert.Empty(session.PromptBatches);
        Assert.Equal(0, session.ClearMemoryCount);
        Assert.Empty(session.Samplers);
    }

    [Fact]
    public async Task OperationMetadataAndContextAreCachedBeforeItsNativeSessionCanBeReleased()
    {
        var session = new FakeNativeSession();
        var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);
        int metadataReads = session.MetadataReadCount;
        int contextReads = session.ContextWindowReadCount;

        await operation.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.True(session.WasDisposed);
        Assert.Equal(new LearningGgufMetadata("test-architecture", "test-model", "test-template"), operation.Metadata);
        Assert.Equal(4_096, operation.ContextWindowTokens);
        Assert.Equal(metadataReads, session.MetadataReadCount);
        Assert.Equal(contextReads, session.ContextWindowReadCount);
    }

    [Fact]
    public async Task SwitchingManualTemplateToAutomaticLoadsItsOwnMetadataBeforePlanning()
    {
        var manualSession = new FakeNativeSession();
        var automaticSession = new FakeNativeSession();
        var factory = new FakeSessionFactory(manualSession, automaticSession);
        await using var runtime = new LearningLocalRuntime(factory);
        var manual = new LearningLocalModelConfiguration("test-model.gguf", LearningChatTemplate.Qwen3, 4_096);
        await using (var operation = await runtime.BeginOperationAsync(manual, CancellationToken.None))
            Assert.Equal(1, factory.LoadCount);

        // Manual delimiters and sampler settings belong to the facade, not native residency.
        var otherManual = new LearningLocalModelConfiguration("test-model.gguf", LearningChatTemplate.ChatMl, 4_096);
        await using (var operation = await runtime.BeginOperationAsync(otherManual, CancellationToken.None))
            Assert.Equal(1, factory.LoadCount);
        Assert.False(manualSession.WasDisposed);

        await using (var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None))
        {
            Assert.Equal(2, factory.LoadCount);
            Assert.True(manualSession.WasDisposed);
            Assert.Equal(automaticSession.Metadata, operation.Metadata);
        }
    }

    [Fact]
    public async Task ExplanationMetadataRequirementCannotReuseAnUncheckedResidentAllocation()
    {
        var uncheckedSession = new FakeNativeSession(); var checkedSession = new FakeNativeSession();
        var factory = new FakeSessionFactory(uncheckedSession, checkedSession);
        await using var runtime = new LearningLocalRuntime(factory);
        var translation = new LearningLocalModelConfiguration("model.gguf", LearningChatTemplate.ChatMl);
        await using (await runtime.BeginOperationAsync(translation, default)) { }
        var explanation = new LearningLocalModelConfiguration("model.gguf", LearningChatTemplate.ChatMl, requiresGeneralInstructions: true);
        await using (await runtime.BeginOperationAsync(explanation, default)) { }
        Assert.Equal(2, factory.LoadCount); Assert.True(uncheckedSession.WasDisposed);
        await using (await runtime.BeginOperationAsync(explanation, default)) { }
        Assert.Equal(2, factory.LoadCount);
    }

    [Fact]
    public async Task ReachingTokenLimitWithoutEogThrowsAfterOnlyProvisionalDeltas()
    {
        var session = new FakeNativeSession();
        session.EnqueueSampleScript((LLamaToken)2);
        session.TokenPieces[2] = Encoding.UTF8.GetBytes("draft");
        var deltas = new List<string>();
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        var failure = await Assert.ThrowsAsync<LearningLocalRuntimeException>(() =>
            operation.GenerateAsync(Tokens(1), Sampling, 1, deltas.Add, CancellationToken.None));

        Assert.Equal(LearningLocalRuntimeFailure.OutputLimitReached, failure.Failure);
        Assert.Equal(["draft"], deltas);
        Assert.True(Assert.Single(session.Samplers).WasDisposed);
        Assert.Equal(2, session.ClearMemoryCount);
    }

    [Fact]
    public async Task IncrementalDecoderWaitsForCompleteUtf8AndRejectsIncompleteEogOutput()
    {
        var validSession = new FakeNativeSession();
        validSession.EnqueueSampleScript(
            (LLamaToken)3,
            (LLamaToken)4,
            (LLamaToken)(-1));
        validSession.TokenPieces[3] = [0xF0, 0x9F];
        validSession.TokenPieces[4] = [0x8E, 0x89];
        var validDeltas = new List<string>();
        await using (var runtime = new LearningLocalRuntime(new FakeSessionFactory(validSession)))
        await using (var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None))
        {
            string result = await operation.GenerateAsync(
                Tokens(1), Sampling, 3, validDeltas.Add, CancellationToken.None).WaitAsync(TestTimeout);

            Assert.Equal("🎉", result);
            Assert.Equal(["🎉"], validDeltas);
        }

        var invalidSession = new FakeNativeSession();
        invalidSession.EnqueueSampleScript((LLamaToken)5, (LLamaToken)(-1));
        invalidSession.TokenPieces[5] = [0xF0, 0x9F];
        var invalidDeltas = new List<string>();
        await using var invalidRuntime = new LearningLocalRuntime(new FakeSessionFactory(invalidSession));
        await using var invalidOperation = await invalidRuntime.BeginOperationAsync(Configuration, CancellationToken.None);

        var failure = await Assert.ThrowsAsync<LearningLocalRuntimeException>(() =>
            invalidOperation.GenerateAsync(Tokens(1), Sampling, 2, invalidDeltas.Add, CancellationToken.None));

        Assert.Equal(LearningLocalRuntimeFailure.InvalidUtf8, failure.Failure);
        Assert.Empty(invalidDeltas);
    }

    [Fact]
    public async Task OutputByteCapRejectsBeforePublishingOversizedToken()
    {
        var session = new FakeNativeSession();
        session.EnqueueSampleScript((LLamaToken)6);
        session.TokenPieces[6] = new byte[48_001];
        var deltas = new List<string>();
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        var failure = await Assert.ThrowsAsync<LearningLocalRuntimeException>(() =>
            operation.GenerateAsync(Tokens(1), Sampling, 2, deltas.Add, CancellationToken.None));

        Assert.Equal(LearningLocalRuntimeFailure.OutputTooLarge, failure.Failure);
        Assert.Empty(deltas);
    }

    [Fact]
    public async Task CancellationBetweenPrefillBatchesStopsBeforeTheNextBatch()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new FakeNativeSession { BatchSize = 2 };
        session.AfterPromptBatch = batch =>
        {
            if (batch.Offset == 0) cancellation.Cancel();
        };
        var deltas = new List<string>();
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            operation.GenerateAsync(Tokens(5), Sampling, 4, deltas.Add, cancellation.Token).WaitAsync(TestTimeout));

        Assert.Equal([(0, 2, false)], session.PromptBatches);
        Assert.Empty(session.Samplers);
        Assert.Empty(deltas);
        Assert.Equal(2, session.ClearMemoryCount);
    }

    [Fact]
    public async Task EachRequestGetsFreshSamplerAndClearedKvState()
    {
        var session = new FakeNativeSession();
        session.EnqueueSampleScript((LLamaToken)(-1));
        session.EnqueueSampleScript((LLamaToken)(-1));
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        _ = await operation.GenerateAsync(Tokens(1), Sampling, 2, null, CancellationToken.None).WaitAsync(TestTimeout);
        _ = await operation.GenerateAsync(Tokens(1), Sampling with { Seed = 456 }, 2, null, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Equal(2, session.Samplers.Count);
        Assert.NotSame(session.Samplers[0], session.Samplers[1]);
        Assert.All(session.Samplers, sampler => Assert.True(sampler.WasDisposed));
        Assert.Equal([Sampling, Sampling with { Seed = 456 }], session.SamplingProfiles);
        Assert.Equal(4, session.ClearMemoryCount);
    }

    [Fact]
    public async Task DisposingRuntimeDrainsDecodeBeforeReleasingContext()
    {
        var decodeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDecode = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeNativeSession
        {
            BatchSize = 1,
            OnPromptBatch = _ =>
            {
                decodeStarted.TrySetResult(true);
                releaseDecode.Task.GetAwaiter().GetResult();
            },
        };
        var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);
        Task<string> generation = operation.GenerateAsync(Tokens(1), Sampling, 2, null, CancellationToken.None);
        Task? runtimeDisposal = null;

        try
        {
            await decodeStarted.Task.WaitAsync(TestTimeout);
            runtimeDisposal = runtime.DisposeAsync().AsTask();

            Assert.False(runtimeDisposal.IsCompleted);
            Assert.False(session.WasDisposed);
        }
        finally
        {
            releaseDecode.TrySetResult(true);
            runtimeDisposal ??= runtime.DisposeAsync().AsTask();
            try { await generation.WaitAsync(TestTimeout); }
            catch (Exception) { }
            try { await runtimeDisposal.WaitAsync(TestTimeout); }
            catch (Exception) { }
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation.WaitAsync(TestTimeout));
        await runtimeDisposal!.WaitAsync(TestTimeout);

        Assert.True(session.WasDisposed);
        await operation.DisposeAsync().AsTask().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task DisposingOneLeaseDoesNotAdmitNextUntilItsDecodeHasDrained()
    {
        var decodeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDecode = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeNativeSession
        {
            BatchSize = 1,
            OnPromptBatch = _ =>
            {
                decodeStarted.TrySetResult(true);
                releaseDecode.Task.GetAwaiter().GetResult();
            },
        };
        var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);
        Task<string> generation = operation.GenerateAsync(Tokens(1), Sampling, 2, null, CancellationToken.None);
        Task<ILearningLocalOperation>? nextOperation = null;
        try
        {
            await decodeStarted.Task.WaitAsync(TestTimeout);
            var leaseDisposal = operation.DisposeAsync().AsTask();
            nextOperation = runtime.BeginOperationAsync(Configuration, CancellationToken.None);
            Assert.False(leaseDisposal.IsCompleted);
            Assert.False(nextOperation.IsCompleted);
            Assert.False(session.WasDisposed);
            releaseDecode.TrySetResult(true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation.WaitAsync(TestTimeout));
            await leaseDisposal.WaitAsync(TestTimeout);
            var next = await nextOperation.WaitAsync(TestTimeout);
            Assert.False(session.WasDisposed);
            await next.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
        finally
        {
            releaseDecode.TrySetResult(true);
            try { await generation.WaitAsync(TestTimeout); } catch { }
            await operation.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            if (nextOperation is not null)
            {
                try { await nextOperation.WaitAsync(TestTimeout); } catch { }
            }
        }
        Assert.True(session.WasDisposed);
    }

    [Fact]
    public async Task DeltaCallbackExceptionIsPreservedWhileKvIsCleared()
    {
        var session = new FakeNativeSession();
        session.EnqueueSampleScript((LLamaToken)7);
        session.TokenPieces[7] = Encoding.UTF8.GetBytes("draft");
        var callbackError = new InvalidOperationException("test callback failure");
        await using var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        await using var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.GenerateAsync(Tokens(1), Sampling, 2, _ => throw callbackError, CancellationToken.None));

        Assert.Same(callbackError, thrown);
        Assert.Equal(2, session.ClearMemoryCount);
        Assert.True(Assert.Single(session.Samplers).WasDisposed);
    }

    [Fact]
    public async Task UnloadReleasesIdleWeightsAndAllowsTheNextRequestToReload()
    {
        var first = new FakeNativeSession();
        var second = new FakeNativeSession();
        var factory = new FakeSessionFactory(first, second);
        await using var runtime = new LearningLocalRuntime(factory);
        await using (var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None)) { }

        await runtime.UnloadAsync().WaitAsync(TestTimeout);
        Assert.True(first.WasDisposed);
        await using (var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None))
        {
            Assert.Equal(2, factory.LoadCount);
            Assert.False(second.WasDisposed);
        }
    }

    [Fact]
    public async Task UnloadRetiresQueuedAdmissionBeforeItCanPrepareAnotherModel()
    {
        var session = new FakeNativeSession();
        var factory = new FakeSessionFactory(session);
        var runtime = new LearningLocalRuntime(factory);
        var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);
        try
        {
            var queued = runtime.BeginOperationAsync(
                new LearningLocalModelConfiguration("other.gguf"), CancellationToken.None);
            Assert.False(queued.IsCompleted);
            var unload = runtime.UnloadAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TestTimeout));
            await unload.WaitAsync(TestTimeout);
            Assert.True(session.WasDisposed);
            Assert.Equal(1, factory.LoadCount);
        }
        finally
        {
            await operation.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReleaseDuringNativeLoadRefusesTheRetiredLeaseAndReleasesTheLoadedSession(bool shutdown)
    {
        var session = new FakeNativeSession();
        var factory = new GatedLoadFactory(session);
        var runtime = new LearningLocalRuntime(factory);
        var begin = runtime.BeginOperationAsync(Configuration, CancellationToken.None);
        try
        {
            await factory.Entered.Task.WaitAsync(TestTimeout);
            var unload = shutdown ? runtime.DisposeAsync().AsTask() : runtime.UnloadAsync();
            Assert.False(unload.IsCompleted);
            factory.Continue.TrySetResult();

            if (shutdown)
            {
                var failure = await Assert.ThrowsAsync<LearningLocalRuntimeException>(() => begin.WaitAsync(TestTimeout));
                Assert.Equal(LearningLocalRuntimeFailure.RuntimeDisposed, failure.Failure);
            }
            else
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => begin.WaitAsync(TestTimeout));
            await unload.WaitAsync(TestTimeout);
            Assert.True(session.WasDisposed);
        }
        finally
        {
            factory.Continue.TrySetResult();
            await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnloadDrainsDecodeAndCannotFreeANewerAdmission(bool admitNewRequest)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeNativeSession
        {
            OnPromptBatch = _ =>
            {
                entered.TrySetResult();
                resume.Task.GetAwaiter().GetResult();
            },
        };
        var factory = new FakeSessionFactory(session);
        var runtime = new LearningLocalRuntime(factory);
        var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);
        var generation = operation.GenerateAsync(Tokens(1), Sampling, 2, null, CancellationToken.None);
        Task<ILearningLocalOperation>? next = null;
        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            var unload = runtime.UnloadAsync();
            if (admitNewRequest)
                next = runtime.BeginOperationAsync(Configuration, CancellationToken.None);
            Assert.False(unload.IsCompleted);
            Assert.False(session.WasDisposed);
            resume.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation.WaitAsync(TestTimeout));

            if (next is not null)
            {
                var newer = await next.WaitAsync(TestTimeout);
                try
                {
                    Assert.False(session.WasDisposed);
                    Assert.Equal(1, factory.LoadCount);
                    Assert.Single(newer.TokenizeLiteral("still owned by the new request"));
                }
                finally { await newer.DisposeAsync().AsTask().WaitAsync(TestTimeout); }
            }
            await unload.WaitAsync(TestTimeout);
            Assert.Equal(!admitNewRequest, session.WasDisposed);
        }
        finally
        {
            resume.TrySetResult();
            try { await generation.WaitAsync(TestTimeout); } catch { }
            await operation.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledNewAdmissionDoesNotInvalidateAPendingUnload(bool failMetadata)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeNativeSession
        {
            OnPromptBatch = _ => { entered.TrySetResult(); resume.Task.GetAwaiter().GetResult(); },
        };
        var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);
        var generation = operation.GenerateAsync(Tokens(1), Sampling, 2, null, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            var unload = runtime.UnloadAsync();
            var next = runtime.BeginOperationAsync(Configuration, cancellation.Token);
            if (failMetadata)
            {
                session.FailMetadataReads = true;
                resume.TrySetResult();
                await Assert.ThrowsAsync<InvalidOperationException>(() => next.WaitAsync(TestTimeout));
            }
            else
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(TestTimeout));
                resume.TrySetResult();
            }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation.WaitAsync(TestTimeout));
            await unload.WaitAsync(TestTimeout);
            Assert.True(session.WasDisposed);
        }
        finally
        {
            resume.TrySetResult();
            try { await generation.WaitAsync(TestTimeout); } catch { }
            await operation.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task ConcurrentReleaseAndShutdownDisposeTheNativeAllocationExactlyOnce()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeNativeSession
        {
            OnPromptBatch = _ => { entered.TrySetResult(); resume.Task.GetAwaiter().GetResult(); },
        };
        var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        var operation = await runtime.BeginOperationAsync(Configuration, CancellationToken.None);
        var generation = operation.GenerateAsync(Tokens(1), Sampling, 2, null, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            var first = runtime.UnloadAsync();
            var second = runtime.UnloadAsync();
            var shutdown = runtime.DisposeAsync().AsTask();
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(shutdown.IsCompleted);
            Assert.Equal(0, session.DisposeCount);
            resume.TrySetResult();
            await Task.WhenAll(first, second, shutdown).WaitAsync(TestTimeout);

            Assert.Equal(1, session.DisposeCount);
            await runtime.UnloadAsync().WaitAsync(TestTimeout);
            Assert.Equal(1, session.DisposeCount);
            var failure = await Assert.ThrowsAsync<LearningLocalRuntimeException>(() =>
                runtime.BeginOperationAsync(Configuration, CancellationToken.None).WaitAsync(TestTimeout));
            Assert.Equal(LearningLocalRuntimeFailure.RuntimeDisposed, failure.Failure);
        }
        finally
        {
            resume.TrySetResult();
            try { await generation.WaitAsync(TestTimeout); } catch { }
            await operation.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectCancellationFlagRejectsLoadedLeaseBeforeLinkedCallbacksRun(bool ownerToken)
    {
        var session = new FakeNativeSession();
        var factory = new GatedLoadFactory(session);
        var runtime = new LearningLocalRuntime(factory);
        using var cancellation = new CancellationTokenSource();
        var begin = runtime.BeginOperationAsync(Configuration,
            ownerToken ? CancellationToken.None : cancellation.Token,
            ownerToken ? cancellation.Token : CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? cancelling = null;
        CancellationTokenRegistration registration = default;
        try
        {
            await factory.Entered.Task.WaitAsync(TestTimeout);
            // Register last so the callback stack blocks before propagating cancellation to the
            // runtime's linked token. The direct caller/owner flag is already cancelled meanwhile.
            registration = cancellation.Token.Register(() =>
            {
                entered.TrySetResult();
                resume.Task.GetAwaiter().GetResult();
            });
            cancelling = cancellation.CancelAsync();
            await entered.Task.WaitAsync(TestTimeout);
            Assert.False(cancelling.IsCompleted);
            factory.Continue.TrySetResult();
            try
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => begin.WaitAsync(TestTimeout));
                await runtime.UnloadAsync().WaitAsync(TestTimeout);
                Assert.True(session.WasDisposed);
            }
            finally { resume.TrySetResult(); }
        }
        finally
        {
            resume.TrySetResult();
            factory.Continue.TrySetResult();
            registration.Dispose();
            if (cancelling is not null) await cancelling.WaitAsync(TestTimeout);
            await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveDecodeChecksDirectCancellationBeforeSamplingWhileCallbacksAreBlocked(bool ownerToken)
    {
        var decoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeDecode = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeNativeSession
        {
            OnPromptBatch = _ => { decoded.TrySetResult(); resumeDecode.Task.GetAwaiter().GetResult(); },
        };
        var runtime = new LearningLocalRuntime(new FakeSessionFactory(session));
        using var cancellation = new CancellationTokenSource();
        var operation = await runtime.BeginOperationAsync(Configuration,
            ownerToken ? CancellationToken.None : cancellation.Token,
            ownerToken ? cancellation.Token : CancellationToken.None);
        var deltas = new List<string>();
        var generation = operation.GenerateAsync(Tokens(1), Sampling, 2, deltas.Add, CancellationToken.None);
        CancellationTokenRegistration registration = default;
        Task? cancelling = null;
        try
        {
            await decoded.Task.WaitAsync(TestTimeout);
            registration = cancellation.Token.Register(() =>
            {
                callbackEntered.TrySetResult();
                resumeCallback.Task.GetAwaiter().GetResult();
            });
            cancelling = cancellation.CancelAsync();
            await callbackEntered.Task.WaitAsync(TestTimeout);
            resumeDecode.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation.WaitAsync(TestTimeout));
            Assert.False(cancelling.IsCompleted);
            Assert.Empty(session.Samplers);
            Assert.Empty(deltas);
        }
        finally
        {
            resumeDecode.TrySetResult();
            resumeCallback.TrySetResult();
            registration.Dispose();
            if (cancelling is not null) await cancelling.WaitAsync(TestTimeout);
            try { await generation.WaitAsync(TestTimeout); } catch { }
            await operation.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    private sealed class GatedLoadFactory(FakeNativeSession session) : ILearningLocalNativeSessionFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ILearningLocalNativeSession> LoadAsync(
            LearningLocalModelConfiguration configuration, CancellationToken ct)
        {
            Entered.TrySetResult();
            // A native load can remain inside an uncancellable synchronous call. Runtime retirement
            // must reject its returned lease even when the requesting token itself was not cancelled.
            await Continue.Task;
            return session;
        }
    }

    private static IReadOnlyList<LLamaToken> Tokens(int count) =>
        Enumerable.Range(10, count).Select(value => (LLamaToken)value).ToArray();

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private sealed class FakeSessionFactory(params FakeNativeSession[] sessions) : ILearningLocalNativeSessionFactory
    {
        public int LoadCount { get; private set; }

        public Task<ILearningLocalNativeSession> LoadAsync(
            LearningLocalModelConfiguration configuration,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<ILearningLocalNativeSession>(sessions[LoadCount++]);
        }
    }

    private sealed class FakeNativeSession : ILearningLocalNativeSession
    {
        private readonly Queue<LLamaToken[]> _sampleScripts = new();
        private readonly HashSet<int> _eogTokens = [-1];
        private LearningGgufMetadata _metadata;
        private int _contextWindowTokens = 4_096;

        public FakeNativeSession() =>
            _metadata = new LearningGgufMetadata("test-architecture", "test-model", "test-template");

        public LearningGgufMetadata Metadata
        {
            get
            {
                ThrowIfDisposed();
                if (FailMetadataReads) throw new InvalidOperationException("Synthetic metadata failure.");
                MetadataReadCount++;
                return _metadata;
            }
        }

        public int ContextWindowTokens
        {
            get
            {
                ThrowIfDisposed();
                ContextWindowReadCount++;
                return _contextWindowTokens;
            }
            init => _contextWindowTokens = value;
        }

        public int BatchSize { get; init; } = 512;
        public Dictionary<int, byte[]> TokenPieces { get; } = [];
        public List<(string Text, bool AllowSpecialTokens)> TokenizeCalls { get; } = [];
        public List<(int Offset, int Count, bool RequestLogits)> PromptBatches { get; } = [];
        public List<LearningModelSamplingValues> SamplingProfiles { get; } = [];
        public List<FakeSampler> Samplers { get; } = [];
        public Action<(int Offset, int Count, bool RequestLogits)>? AfterPromptBatch { get; set; }
        public Action<(int Offset, int Count, bool RequestLogits)>? OnPromptBatch { get; set; }
        public int ClearMemoryCount { get; private set; }
        public bool FailMetadataReads { get; set; }
        public int MetadataReadCount { get; private set; }
        public int ContextWindowReadCount { get; private set; }
        public bool WasDisposed { get; private set; }
        public int DisposeCount { get; private set; }

        public void EnqueueSampleScript(params LLamaToken[] tokens) => _sampleScripts.Enqueue(tokens);

        public IReadOnlyList<LLamaToken> Tokenize(string text, bool allowSpecialTokens)
        {
            ThrowIfDisposed();
            TokenizeCalls.Add((text, allowSpecialTokens));
            return [(LLamaToken)(allowSpecialTokens ? 1 : 0)];
        }

        public void ClearMemory()
        {
            ThrowIfDisposed();
            ClearMemoryCount++;
        }

        public int DecodeBatch(
            IReadOnlyList<LLamaToken> tokens,
            int offset,
            int count,
            int position,
            bool requestLogits)
        {
            ThrowIfDisposed();
            var batch = (offset, count, requestLogits);
            PromptBatches.Add(batch);
            OnPromptBatch?.Invoke(batch);
            AfterPromptBatch?.Invoke(batch);
            return requestLogits ? count - 1 : -1;
        }

        public int DecodeToken(LLamaToken token, int position)
        {
            ThrowIfDisposed();
            return 0;
        }

        public bool IsEndOfGeneration(LLamaToken token)
        {
            ThrowIfDisposed();
            return _eogTokens.Contains((int)token);
        }

        public byte[] TokenToBytes(LLamaToken token)
        {
            ThrowIfDisposed();
            return TokenPieces.TryGetValue((int)token, out var bytes) ? bytes : [];
        }

        public ILearningLocalSampler CreateSampler(LearningModelSamplingValues values)
        {
            ThrowIfDisposed();
            SamplingProfiles.Add(values);
            var sampler = new FakeSampler(_sampleScripts.Count == 0 ? [] : _sampleScripts.Dequeue());
            Samplers.Add(sampler);
            return sampler;
        }

        public void Dispose()
        {
            DisposeCount++;
            WasDisposed = true;
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(WasDisposed, this);
    }

    private sealed class FakeSampler(IEnumerable<LLamaToken> script) : ILearningLocalSampler
    {
        private readonly Queue<LLamaToken> _tokens = new(script);

        public bool WasDisposed { get; private set; }
        public int SampleCount { get; private set; }
        public List<LLamaToken> Accepted { get; } = [];

        public LLamaToken Sample(int logitsIndex)
        {
            ObjectDisposedException.ThrowIf(WasDisposed, this);
            SampleCount++;
            return _tokens.Dequeue();
        }

        public void Accept(LLamaToken token) => Accepted.Add(token);

        public void Dispose() => WasDisposed = true;
    }
}
