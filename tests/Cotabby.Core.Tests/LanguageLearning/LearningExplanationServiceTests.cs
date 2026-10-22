using System.Threading.Channels;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.LanguageLearning;

/// <summary>Mac service regressions and delayed effects; no real runtime, endpoint, UI or notebook.</summary>
public sealed class LearningExplanationServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly LearningModelSnapshot Profile = new()
    {
        LocalModel = "Qwen3.5-4B-Q4_K_M.gguf", Template = LearningChatTemplate.Qwen3, MaximumOutputTokens = 768,
    };
    private static readonly LearningExplanationResult Answer = new("告知", "let someone know 表示让某人知道。");
    private static LearningExplanationInput Input(string target = "know", string context = "Let me know.") =>
        new(target, target, LearningLanguage.English, VocabularyEntryKind.Word, context, [new(0, 1)]);

    [Fact]
    public async Task IndependentProfileCachesCompleteRecordsAndRefreshCreatesANewArtifact()
    {
        int calls = 0, disposed = 0, published = 0, displays = 0;
        var clock = new FixedClock();
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese, snapshot =>
        {
            Assert.Equal(Profile, snapshot);
            return new Engine((input, language, output, _) =>
            {
                Assert.Equal(Input(), input); Assert.Equal(LearningLanguage.Chinese, language); Assert.Equal(768, output);
                Interlocked.Increment(ref calls); return Task.FromResult(Answer);
            }, () => { disposed++; return ValueTask.CompletedTask; });
        }, clock);
        service.Explained += _ => published++;
        service.Displayed += request => { Assert.Equal("req_display", request); displays++; };
        var first = await service.GenerateAsync(Input(), "req_first").WaitAsync(Timeout);
        Assert.Equal(first, await service.GenerateAsync(Input(), "req_cached").WaitAsync(Timeout));
        Assert.Equal(1, calls); Assert.Equal(1, disposed); Assert.Equal(1, published);
        Assert.Equal(clock.GetUtcNow(), first.CreatedAt); Assert.Equal(Profile.LocalModel, first.Model);
        Assert.Equal(Input(), first.Input); Assert.Equal(Answer, first.Result); Assert.NotEqual(Guid.Empty, first.Id);
        service.RecordDisplay("req_display"); Assert.Equal(1, displays);
        var refreshed = await service.GenerateAsync(Input(), "req_refresh", refresh: true).WaitAsync(Timeout);
        Assert.NotEqual(first.Id, refreshed.Id); Assert.Equal(2, calls); Assert.Equal(2, disposed); Assert.Equal(2, published);
    }

    [Fact]
    public async Task SavedLookupPrecedesCacheAndRefreshSkipsBoth()
    {
        int calls = 0, loads = 0, lookups = 0;
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, _) => { calls++; return Task.FromResult(Answer); }));
        var cached = await service.GenerateAsync(Input(), "req_cached").WaitAsync(Timeout);
        var saved = cached with { Id = Guid.NewGuid() };
        service.LoadPersisted = (_, _) => { loads++; return Task.FromResult<LearningExplanationRecord?>(null); };
        service.LookupSaved = (_, _) => { lookups++; return saved; };
        Assert.Equal(saved, await service.GenerateAsync(Input(), "req_saved").WaitAsync(Timeout));
        Assert.Equal(1, calls); Assert.Equal(1, loads); Assert.Equal(1, lookups);
        await service.GenerateAsync(Input(), "req_refresh", refresh: true).WaitAsync(Timeout);
        Assert.Equal(2, calls); Assert.Equal(1, loads); Assert.Equal(1, lookups);
    }

    [Fact]
    public async Task LanguageChangeDuringSavedLookupCannotReturnOrCacheTheOldRecord()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<LearningExplanationRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int engines = 0;
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => { engines++; throw new InvalidOperationException(); });
        service.LoadPersisted = (_, language) => { Assert.Equal(LearningLanguage.Chinese, language); started.TrySetResult(); return pending.Task; };
        var work = service.GenerateAsync(Input(), "req_saved");
        await started.Task.WaitAsync(Timeout);
        service.ApplyConfiguration(Profile, LearningLanguage.French);
        pending.SetResult(new(Guid.NewGuid(), Input(), LearningLanguage.Chinese, Answer, "saved", DateTimeOffset.UtcNow));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(Timeout));
        Assert.Null(service.Cached(Input())); Assert.Equal(0, engines);
    }

    [Fact]
    public async Task StandaloneSavedRestorationChecksTheCommittedLanguageAfterAwait()
    {
        var pending = new TaskCompletionSource<LearningExplanationRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese, _ => throw new InvalidOperationException());
        service.LoadPersisted = (_, _) => pending.Task;
        var lookup = service.LoadSavedAsync(Input());
        service.ApplyConfiguration(Profile, LearningLanguage.English);
        pending.SetResult(new(Guid.NewGuid(), Input(), LearningLanguage.Chinese, Answer, "saved", DateTimeOffset.UtcNow));
        Assert.Null(await lookup.WaitAsync(Timeout));
    }

    [Theory]
    [InlineData("model")]
    [InlineData("language")]
    [InlineData("caller")]
    public async Task UncooperativeLateAnswersNeverPublishOrEnterTheCache(string change)
    {
        var calls = Channel.CreateUnbounded<Pending>(); int disposed = 0, published = 0;
        using var caller = new CancellationTokenSource();
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, token) =>
            {
                var pending = new Pending(token); calls.Writer.TryWrite(pending); return pending.Completion.Task;
            }, () => { disposed++; return ValueTask.CompletedTask; }));
        service.Explained += _ => published++;
        var work = service.GenerateAsync(Input(), "req_late", cancellationToken: caller.Token);
        var pending = await calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        if (change == "caller") caller.Cancel();
        else service.ApplyConfiguration(change == "model" ? Profile with { LocalModel = "other.gguf" } : Profile,
            change == "language" ? LearningLanguage.French : LearningLanguage.Chinese);
        // CancelAsync sets the owner flag immediately; linked callbacks can run later.
        pending.Completion.SetResult(Answer);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(Timeout));
        Assert.Null(service.Cached(Input())); Assert.Equal(0, published); Assert.Equal(1, disposed);
    }

    [Theory]
    [InlineData("temperature")]
    [InlineData("output")]
    [InlineData("context")]
    [InlineData("template")]
    public async Task CompleteProfileIdentitySeparatesCachedAnswers(string parameter)
    {
        int calls = 0;
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, _) => { calls++; return Task.FromResult(Answer); }));
        var first = await service.GenerateAsync(Input(), "req_first").WaitAsync(Timeout);
        var changed = parameter switch
        {
            "temperature" => Profile with { SamplingOverrides = new(Temperature: 0.7) },
            "output" => Profile with { MaximumOutputTokens = 1024 },
            "context" => Profile with { ContextWindowTokens = 16384 },
            "template" => Profile with { Template = LearningChatTemplate.ChatMl },
            _ => throw new InvalidOperationException(),
        };
        service.ApplyConfiguration(changed, LearningLanguage.Chinese);
        Assert.Null(service.Cached(Input()));
        Assert.NotEqual(first.Id, (await service.GenerateAsync(Input(), "req_changed").WaitAsync(Timeout)).Id);
        service.ApplyConfiguration(Profile, LearningLanguage.Chinese);
        Assert.Equal(first, await service.GenerateAsync(Input(), "req_return").WaitAsync(Timeout)); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SpecialistOrEmptyProfileNeverCreatesAnEngineAndInputValidationComesFirst()
    {
        int calls = 0;
        await using var service = new LearningExplanationService(Profile with { LocalModel = "translategemma.gguf" }, LearningLanguage.Chinese,
            _ => { calls++; throw new InvalidOperationException(); });
        Assert.False(service.IsConfigured);
        var bad = new LearningExplanationInput("", "", LearningLanguage.English, VocabularyEntryKind.Word, "", []);
        Assert.Equal(LearningExplanationFailure.InvalidContent,
            (await Assert.ThrowsAsync<LearningExplanationException>(() => service.GenerateAsync(bad, "req_invalid"))).Failure);
        Assert.Equal(LearningExplanationFailure.InstructionModelRequired,
            (await Assert.ThrowsAsync<LearningExplanationException>(() => service.GenerateAsync(Input(), "req_specialist"))).Failure);
        service.ApplyConfiguration(new(), LearningLanguage.Chinese);
        Assert.False(service.IsConfigured);
        await Assert.ThrowsAsync<LearningExplanationException>(() => service.GenerateAsync(Input(), "req_empty")); Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("invalid-result")]
    [InlineData("foreign-failure")]
    [InlineData("dispose-failure")]
    public async Task FailedOrInvalidWorkCannotBeCachedAndForeignErrorsStayContentFree(string failure)
    {
        int calls = 0;
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, _) =>
            {
                calls++;
                return failure == "foreign-failure" ? Task.FromException<LearningExplanationResult>(new IOException("private text"))
                    : Task.FromResult(failure == "invalid-result" ? new LearningExplanationResult("", "") : Answer);
            }, () => failure == "dispose-failure" ? ValueTask.FromException(new IOException("private path")) : ValueTask.CompletedTask));
        for (int index = 0; index < 2; index++)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => service.GenerateAsync(Input(), "req_failed").WaitAsync(Timeout));
            Assert.True(error is LearningExplanationException or LearningTranslationException);
            Assert.DoesNotContain("private", error.Message); Assert.Null(service.Cached(Input()));
        }
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FifoLimitIsFortyEightAndCacheHitsDoNotPromoteOldEntries()
    {
        int calls = 0;
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, _) => { calls++; return Task.FromResult(Answer); }));
        for (int index = 0; index < 48; index++) await service.GenerateAsync(Input("word" + index), "req_fill").WaitAsync(Timeout);
        await service.GenerateAsync(Input("word0"), "req_hit").WaitAsync(Timeout);
        await service.GenerateAsync(Input("word48"), "req_evict").WaitAsync(Timeout);
        Assert.Null(service.Cached(Input("word0"))); Assert.NotNull(service.Cached(Input("word1")));
        await service.GenerateAsync(Input("word0"), "req_recompute").WaitAsync(Timeout); Assert.Equal(50, calls);
    }

    [Fact]
    public async Task RetainedTextEstimateCountsUtf8BytesAndEvictsBeforeTheEntryLimit()
    {
        int calls = 0;
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, _) => { calls++; return Task.FromResult(Answer); }));
        string context = new('é', 130_000);
        for (int index = 0; index < 17; index++) await service.GenerateAsync(Input("word" + index, context), "req_fill").WaitAsync(Timeout);
        Assert.Null(service.Cached(Input("word0", context))); Assert.NotNull(service.Cached(Input("word16", context)));
        await service.GenerateAsync(Input("word0", context), "req_evicted").WaitAsync(Timeout); Assert.Equal(18, calls);
    }

    [Fact]
    public async Task DisposeRetiresTheRequestThenDrainsItsFacadeWithoutOwningTheRuntime()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<LearningExplanationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int disposed = 0;
        var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, token) => { started.SetResult(token); return pending.Task; },
                () => { disposed++; return ValueTask.CompletedTask; }));
        var work = service.GenerateAsync(Input(), "req_shutdown");
        var token = await started.Task.WaitAsync(Timeout);
        var disposal = service.DisposeAsync().AsTask();
        Assert.True(token.IsCancellationRequested); Assert.False(disposal.IsCompleted);
        pending.SetResult(Answer);
        await disposal.WaitAsync(Timeout); await service.DisposeAsync().AsTask().WaitAsync(Timeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.Equal(1, disposed); Assert.Null(service.Cached(Input()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.GenerateAsync(Input(), "req_after"));
    }

    [Fact]
    public async Task DisposalDrainsAdmittedSavedLookupsAndRejectsNewOnes()
    {
        var pending = new TaskCompletionSource<LearningExplanationRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new LearningExplanationService(Profile, LearningLanguage.Chinese, _ => throw new InvalidOperationException());
        service.LoadPersisted = (_, _) => pending.Task;
        var lookup = service.LoadSavedAsync(Input());
        var disposal = service.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        pending.SetResult(new(Guid.NewGuid(), Input(), LearningLanguage.Chinese, Answer, "saved", DateTimeOffset.UtcNow));
        Assert.Null(await lookup.WaitAsync(Timeout));
        await disposal.WaitAsync(Timeout);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.LoadSavedAsync(Input()));
        Assert.Throws<ObjectDisposedException>(() => service.SavedRecord(Input()));
    }

    [Fact]
    public async Task SynchronousSavedReadCannotReturnAfterAConcurrentLanguageChange()
    {
        using var entered = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese, _ => throw new InvalidOperationException());
        service.LookupSaved = (_, language) =>
        {
            entered.Set(); Assert.True(finish.Wait(Timeout));
            return new(Guid.NewGuid(), Input(), language, Answer, "saved", DateTimeOffset.UtcNow);
        };
        var lookup = Task.Run(() => service.SavedRecord(Input()));
        Assert.True(entered.Wait(Timeout));
        service.ApplyConfiguration(Profile, LearningLanguage.French);
        finish.Set(); Assert.Null(await lookup.WaitAsync(Timeout));
    }

    [Fact]
    public async Task DisposalAlsoDrainsASynchronousSavedCallbackWithoutReturningItsRecord()
    {
        using var entered = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        var service = new LearningExplanationService(Profile, LearningLanguage.Chinese, _ => throw new InvalidOperationException());
        service.LookupSaved = (_, language) =>
        {
            entered.Set(); Assert.True(finish.Wait(Timeout));
            return new(Guid.NewGuid(), Input(), language, Answer, "saved", DateTimeOffset.UtcNow);
        };
        var lookup = Task.Run(() => service.SavedRecord(Input()));
        Assert.True(entered.Wait(Timeout));
        var disposal = service.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        finish.Set(); Assert.Null(await lookup.WaitAsync(Timeout));
        await disposal.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedAdapterFailuresHaveNoForeignTextOrInnerException(bool asynchronous)
    {
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese, _ => throw new InvalidOperationException());
        service.LookupSaved = (_, _) => throw new IOException("private saved path");
        service.LoadPersisted = (_, _) => Task.FromException<LearningExplanationRecord?>(new IOException("private saved text"));
        var error = asynchronous
            ? await Assert.ThrowsAsync<LearningExplanationException>(() => service.LoadSavedAsync(Input()))
            : Assert.Throws<LearningExplanationException>(() => service.SavedRecord(Input()));
        Assert.DoesNotContain("private", error.Message); Assert.Null(error.InnerException);
        Assert.Equal(LearningExplanationFailure.ProviderFailure, error.Failure);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("language")]
    [InlineData("result")]
    [InlineData("null-result")]
    public async Task MiskeyedOrInvalidSavedRowsAreMissesAndCannotBypassGeneration(string mismatch)
    {
        int calls = 0;
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, _) => { calls++; return Task.FromResult(Answer); }));
        var row = new LearningExplanationRecord(Guid.NewGuid(), Input(), LearningLanguage.Chinese, Answer, "saved", DateTimeOffset.UtcNow);
        row = mismatch switch
        {
            "input" => row with { Input = Input("another occurrence") },
            "language" => row with { Language = LearningLanguage.French },
            "null-result" => row with { Result = null! },
            _ => row with { Result = new("", "") },
        };
        service.LoadPersisted = (_, _) => Task.FromResult<LearningExplanationRecord?>(row);
        service.LookupSaved = (_, _) => row;
        Assert.Null(service.SavedRecord(Input())); Assert.Null(await service.LoadSavedAsync(Input()).WaitAsync(Timeout));
        var generated = await service.GenerateAsync(Input(), "req_fallback").WaitAsync(Timeout);
        Assert.NotEqual(row.Id, generated.Id); Assert.Equal(1, calls); Assert.Equal(Answer, generated.Result);
    }

    [Fact]
    public async Task DormantSettingsDoNotRetireLocalWorkOrSplitItsCache()
    {
        int calls = 0, changes = 0;
        await using var service = new LearningExplanationService(Profile, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, _) => { calls++; return Task.FromResult(Answer); }));
        service.Changed += () => changes++;
        var first = await service.GenerateAsync(Input(), "req_first").WaitAsync(Timeout);
        service.ApplyConfiguration(Profile with
        {
            BaseUrl = "https://future.invalid", EndpointModel = "dormant", CredentialRevision = 19,
            TranslationStyle = new(LearningTranslationRegister.Custom, "dormant translation instruction"),
        }, LearningLanguage.Chinese);
        Assert.Equal(first, await service.GenerateAsync(Input(), "req_same").WaitAsync(Timeout));
        Assert.Equal(0, changes); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CanonicallyEqualProfileNamesAndRecordsKeepMacValueIdentity()
    {
        var nfc = Profile with { LocalModel = "modèle.gguf" };
        var nfd = Profile with { LocalModel = "mode\u0300le.gguf" };
        Assert.Equal(nfc, nfd); Assert.Equal(nfc.GetHashCode(), nfd.GetHashCode());
        int calls = 0, changes = 0;
        await using var service = new LearningExplanationService(nfc, LearningLanguage.Chinese,
            _ => new Engine((_, _, _, _) => { calls++; return Task.FromResult(Answer); }));
        service.Changed += () => changes++;
        var first = await service.GenerateAsync(Input(), "req_first").WaitAsync(Timeout);
        service.ApplyConfiguration(nfd, LearningLanguage.Chinese);
        Assert.Equal(first, await service.GenerateAsync(Input(), "req_equal").WaitAsync(Timeout));
        Assert.Equal(0, changes); Assert.Equal(1, calls);
        Assert.Equal(first, first with { Model = nfd.LocalModel });
        Assert.Equal(first.GetHashCode(), (first with { Model = nfd.LocalModel }).GetHashCode());
    }

    [Fact]
    public void EndpointEffectiveIdentityClearsLocalParametersAndRetainsItsOwnCredentialRevision()
    {
        var snapshot = Profile with { Backend = LearningModelBackend.Endpoint, EndpointModel = "tutor", BaseUrl = "http://localhost:11434/v1",
            CredentialRevision = 3, SamplingOverrides = new(Temperature: 0.5, TopK: 19) };
        var effective = snapshot.ForExplanation();
        Assert.Empty(effective.LocalModel); Assert.Equal(LearningChatTemplate.Automatic, effective.Template);
        Assert.Equal(3, effective.CredentialRevision); Assert.Equal(snapshot.BaseUrl, effective.BaseUrl);
        Assert.Null(effective.SamplingOverrides.TopK); Assert.Equal(0.5, effective.SamplingOverrides.Temperature);
        Assert.NotEqual(effective, effective with { CredentialRevision = 4 });
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 22, 1, 2, 3, TimeSpan.Zero);
    }
    private sealed record Pending(CancellationToken Token)
    {
        public TaskCompletionSource<LearningExplanationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Engine(Func<LearningExplanationInput, LearningLanguage, int, CancellationToken, Task<LearningExplanationResult>> generate,
        Func<ValueTask>? dispose = null) : ILearningExplanationEngine
    {
        public Task<LearningExplanationResult> GenerateAsync(LearningExplanationInput input, LearningLanguage responseLanguage,
            int maximumOutputTokens, CancellationToken ct) => generate(input, responseLanguage, maximumOutputTokens, ct);
        public ValueTask DisposeAsync() => dispose?.Invoke() ?? ValueTask.CompletedTask;
    }
}
