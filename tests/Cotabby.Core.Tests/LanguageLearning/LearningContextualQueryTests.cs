using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.LanguageLearning;

/// <summary>
/// Adapts the three pinned Mac contextual-query regressions, plus Windows foreign-callback and
/// occurrence lifetime cases. Providers are uncooperative effect doubles; no model/editor is used.
/// </summary>
public sealed class LearningContextualQueryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static LearningUnit Pattern => new(7, "keep me posted", "keep someone posted", LearningUnitKind.Pattern,
        [new(7, 14)], [new("keep", new(7, 4)), new("me", new(12, 2)), new("posted", new(15, 6))]);
    private static LearningExplanationInput Capture => new("keep someone posted", "keep someone posted", LearningLanguage.English,
        VocabularyEntryKind.Pattern, "Please keep me posted.", [new(7, 14)], LearningExplanationContext.Sentence, "occurrence");
    private static LearningUnit Word => new(0, "know", "know", LearningUnitKind.Word, [new(0, 4)], []);

    [Fact]
    public async Task DefaultSenseIsImmediateAndDictionaryIsLazyWithoutRestartingStoppedWork()
    {
        var provider = new Dictionary(); var explainer = new Explainer();
        await using var session = new LearningDictionarySession(provider, explainer);
        try
        {
            await session.OpenAsync(Pattern, "doc", LearningLanguage.English, Capture.Context, Capture);
            var first = await explainer.Next();
            Assert.Equal(LearningDictionaryPage.Contextual, session.State.Page);
            Assert.True(session.State.Explanation!.IsGenerating); Assert.Equal(Capture, first.Input);
            Assert.Equal(0, provider.Calls);
            var identity = session.State.TargetIdentity;
            await session.SelectPageAsync(LearningDictionaryPage.Dictionary, identity).WaitAsync(Timeout);
            Assert.Equal(LearningDictionaryContentStatus.Result, session.State.Status);
            await session.SelectPageAsync(LearningDictionaryPage.Contextual, identity);
            session.CancelExplanation(identity, session.State.Explanation!);
            await session.SelectPageAsync(LearningDictionaryPage.Dictionary, identity);
            await session.SelectPageAsync(LearningDictionaryPage.Contextual, identity);
            Assert.Equal(identity, session.State.TargetIdentity);
            Assert.Equal(1, provider.Calls); Assert.Equal(1, explainer.Generations);
            first.Complete(); await Settled(() => !session.State.Explanation!.IsGenerating);
            Assert.Null(session.State.Explanation!.Record);
            await session.SelectWordAsync(Pattern.Words[0], identity);
            var second = await explainer.Next();
            Assert.Equal("keep", second.Input.Target); Assert.Equal(Capture.Context, second.Input.Context);
            Assert.Equal(new VocabularyTextRange(7, 4), Assert.Single(second.Input.Ranges));
            Assert.NotEqual(identity, session.State.TargetIdentity);
            session.Close(); second.Complete();
            Assert.Null(session.State.Explanation); Assert.Null(session.State.Unit); Assert.Equal(0, explainer.Subscribers);
        }
        finally { session.Close(); explainer.FinishAll(); }
    }

    [Fact]
    public async Task UnconfiguredSenseKeepsDictionaryAvailableWithoutStartingAModel()
    {
        var provider = new Dictionary(); var explainer = new Explainer { Configured = false };
        await using var session = new LearningDictionarySession(provider, explainer);
        await session.OpenAsync(Word, "doc", LearningLanguage.English);
        Assert.Equal(LearningDictionaryPage.Contextual, session.State.Page);
        Assert.False(session.State.ExplanationConfigured); Assert.Equal(0, explainer.Generations); Assert.Equal(0, provider.Calls);
        await session.SelectPageAsync(LearningDictionaryPage.Dictionary, session.State.TargetIdentity).WaitAsync(Timeout);
        Assert.Equal(1, provider.Calls);
        explainer.Configured = true; explainer.Change();
        Assert.True(session.State.ExplanationConfigured); Assert.Equal(0, explainer.Generations);
        await session.SelectPageAsync(LearningDictionaryPage.Contextual);
        Assert.Equal(0, explainer.Generations); // A settings/tab change is not another explicit lookup.
    }

    [Fact]
    public async Task QueuedNavigationAndCommandsCannotAffectAReopenedIdenticalQuery()
    {
        var provider = new Dictionary(); var explainer = new Explainer { Configured = false };
        await using var session = new LearningDictionarySession(provider, explainer);
        await session.OpenAsync(Word, "doc", LearningLanguage.English);
        var oldTarget = session.State.TargetIdentity; var oldState = session.State.Explanation!;
        session.Close(); await session.OpenAsync(Word, "doc", LearningLanguage.English);
        var current = session.State.TargetIdentity;
        await session.SelectPageAsync(LearningDictionaryPage.Dictionary, oldTarget);
        await session.ExplainAsync(oldTarget, oldState); session.CancelExplanation(oldTarget, oldState);
        session.Close(oldTarget);
        Assert.NotEqual(oldTarget, current); Assert.Equal(current, session.State.TargetIdentity);
        Assert.Equal(LearningDictionaryPage.Contextual, session.State.Page); Assert.Equal(0, provider.Calls);
        await session.SelectPageAsync(LearningDictionaryPage.Dictionary, current).WaitAsync(Timeout);
        await session.SelectPageAsync(LearningDictionaryPage.Contextual, current);
        Assert.Equal(current, session.State.TargetIdentity); Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ReturnToExpressionRestoresTheFrozenCaptureAndRejectsLateComponentReplies()
    {
        var explainer = new Explainer(); await using var session = new LearningDictionarySession(new Dictionary(), explainer);
        try
        {
            await session.OpenAsync(Pattern, "doc", LearningLanguage.English, Capture.Context, Capture);
            var parent = await explainer.Next(); parent.Complete();
            await Settled(() => session.State.Explanation?.Record is not null);
            await session.SelectWordAsync(Pattern.Words[1], session.State.TargetIdentity);
            var component = await explainer.Next(); Assert.Equal("me", component.Input.Target);
            await session.ReturnToExpressionAsync(session.State.TargetIdentity);
            var restored = await explainer.Next();
            Assert.Equal(Capture, restored.Input); Assert.Null(session.State.SelectedWord);
            component.Complete(); Assert.Null(session.State.Explanation!.Record);
            restored.Complete(); await Settled(() => session.State.Explanation?.Record is not null);
            Assert.Equal(Capture, session.State.Explanation!.Record!.Input);
            Assert.Equal(1, explainer.Subscribers);
        }
        finally { session.Close(); explainer.FinishAll(); }
    }

    [Fact]
    public async Task RepeatedWordUsesTheSelectedOccurrenceAndOriginalSentenceOffset()
    {
        var unit = new LearningUnit(13, "had had", "had had", LearningUnitKind.Expression,
            [new(13, 7)], [new("had", new(13, 3)), new("had", new(17, 3))]);
        var input = new LearningExplanationInput("had had", "had had", LearningLanguage.English, VocabularyEntryKind.Expression,
            "One had had enough.", [new(4, 7)], LearningExplanationContext.Sentence, "exact-sentence");
        var explainer = new Explainer(); await using var session = new LearningDictionarySession(new Dictionary(), explainer);
        try
        {
            await session.OpenAsync(unit, "doc", LearningLanguage.English, "Earlier. One had had enough.", input);
            await explainer.Next();
            await session.SelectWordAsync(unit.Words[1], session.State.TargetIdentity);
            var selected = await explainer.Next();
            Assert.Equal(new VocabularyTextRange(8, 3), Assert.Single(selected.Input.Ranges));
            Assert.Equal(input.Context, selected.Input.Context); Assert.Equal("had", selected.Input.Target);
        }
        finally { session.Close(); explainer.FinishAll(); }
    }

    [Fact]
    public async Task FailedComponentMappingDoesNotLeakItsParentCaptureButDictionaryStillWorks()
    {
        var unit = new LearningUnit(7, Pattern.Surface, Pattern.Query, Pattern.Kind, Pattern.Ranges, [new("bad", new(7, 3))]);
        var provider = new Dictionary(); var explainer = new Explainer();
        await using var session = new LearningDictionarySession(provider, explainer);
        try
        {
            await session.OpenAsync(unit, "doc", LearningLanguage.English, Capture.Context, Capture); await explainer.Next();
            await session.SelectWordAsync(unit.Words[0], session.State.TargetIdentity);
            Assert.Null(session.State.Explanation!.Input); Assert.Equal(1, explainer.Generations);
            await session.SelectPageAsync(LearningDictionaryPage.Dictionary).WaitAsync(Timeout);
            Assert.Equal(1, provider.Calls); Assert.Equal("bad", session.State.Query);
        }
        finally { session.Close(); explainer.FinishAll(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingProjectionUsesMacUnscopedFallbackButForeignInputIsRejected(bool foreign)
    {
        var provider = new Dictionary(); var explainer = new Explainer();
        await using var session = new LearningDictionarySession(provider, explainer);
        LearningExplanationInput? input = foreign ? new("different", "different", LearningLanguage.English,
            VocabularyEntryKind.Word, "different", [new(0, 9)], LearningExplanationContext.None) : null;
        try
        {
            await session.OpenAsync(Pattern, "doc", LearningLanguage.English, Capture.Context, input);
            if (foreign) { Assert.Null(session.State.Explanation!.Input); Assert.Equal(0, explainer.Generations); }
            else
            {
                var call = await explainer.Next();
                Assert.Equal(Capture.Target, call.Input.Target); Assert.Equal(Capture.Target, call.Input.Context);
                Assert.Equal(LearningExplanationContext.None, call.Input.ContextKind); Assert.Null(call.Input.OccurrenceIdentity);
            }
            await session.SelectPageAsync(LearningDictionaryPage.Dictionary).WaitAsync(Timeout);
            Assert.Equal(1, provider.Calls);
        }
        finally { session.Close(); explainer.FinishAll(); }
    }

    [Fact]
    public async Task ReentrantSavedLookupCanNavigateWithoutAnOlderActivationOverwritingIt()
    {
        var explainer = new Explainer(); await using var session = new LearningDictionarySession(new Dictionary(), explainer);
        explainer.Saved = input =>
        {
            if (input.Target == Capture.Target) session.OpenAsync(Word, "new", LearningLanguage.English).GetAwaiter().GetResult();
            return null;
        };
        try
        {
            await session.OpenAsync(Pattern, "old", LearningLanguage.English, Capture.Context, Capture);
            var current = await explainer.Next();
            Assert.Equal("new", session.State.DocumentId); Assert.Equal("know", current.Input.Target);
            Assert.Equal(1, explainer.Generations); Assert.Equal(1, explainer.Subscribers);
        }
        finally { session.Close(); explainer.FinishAll(); }
    }

    [Fact]
    public async Task BlockedForeignSavedCallbackDoesNotHoldTheDictionaryLockOrReviveItsTarget()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var explainer = new Explainer(); await using var session = new LearningDictionarySession(new Dictionary(), explainer);
        explainer.Saved = input => { if (input.Target == Capture.Target) { entered.Set(); Assert.True(release.Wait(Timeout)); } return null; };
        var old = Task.Run(() => session.OpenAsync(Pattern, "old", LearningLanguage.English, Capture.Context, Capture));
        try
        {
            Assert.True(entered.Wait(Timeout));
            await session.OpenAsync(Word, "new", LearningLanguage.English).WaitAsync(Timeout);
            var current = await explainer.Next(); Assert.Equal("know", current.Input.Target);
            release.Set(); await old.WaitAsync(Timeout);
            Assert.Equal("new", session.State.DocumentId); Assert.Equal(1, explainer.Generations);
        }
        finally { release.Set(); await old.WaitAsync(Timeout); session.Close(); explainer.FinishAll(); }
    }

    [Fact]
    public async Task BlockedEventSubscriptionCannotHoldNavigationOrSurviveRetirement()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int subscriptions = 0;
        var explainer = new Explainer(); await using var session = new LearningDictionarySession(new Dictionary(), explainer);
        var visibleDocuments = new ConcurrentQueue<string?>();
        session.StateChanged += state => visibleDocuments.Enqueue(state.DocumentId);
        explainer.Subscribe = () =>
        { if (Interlocked.Increment(ref subscriptions) == 1) { entered.Set(); Assert.True(release.Wait(Timeout)); } };
        var old = Task.Run(() => session.OpenAsync(Pattern, "old", LearningLanguage.English, Capture.Context, Capture));
        try
        {
            Assert.True(entered.Wait(Timeout));
            await session.OpenAsync(Word, "new", LearningLanguage.English).WaitAsync(Timeout);
            var current = await explainer.Next(); Assert.Equal("know", current.Input.Target);
            Assert.Equal(1, explainer.Subscribers);
            Assert.Contains("new", visibleDocuments); // Old blocked activation cannot park notification draining.
            release.Set(); await old.WaitAsync(Timeout);
            Assert.Equal("new", session.State.DocumentId); Assert.Equal(1, explainer.Generations);
            session.Close(); Assert.Equal(0, explainer.Subscribers);
        }
        finally { release.Set(); await old.WaitAsync(Timeout); session.Close(); explainer.FinishAll(); }
    }

    [Fact]
    public void RetiredContextualWorkDoesNotRetainTheDictionaryOwnerOrItsViewSubscriber()
    {
        var explainer = new Explainer(); var retired = CreateRetiredOwner(explainer);
        try
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(retired.Owner.IsAlive); Assert.False(retired.View.IsAlive);
            Assert.Equal(0, explainer.Subscribers); Assert.False(retired.Call.Completion.Task.IsCompleted);
        }
        finally { explainer.FinishAll(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Owner, WeakReference View, Call Call) CreateRetiredOwner(Explainer explainer)
    {
        var owner = new LearningDictionarySession(new Dictionary(), explainer); var view = new ViewProbe();
        owner.StateChanged += view.Observe;
        owner.OpenAsync(Word, "doc", LearningLanguage.English).GetAwaiter().GetResult();
        var call = explainer.Next().GetAwaiter().GetResult();
        owner.DisposeAsync().GetAwaiter().GetResult();
        return (new(owner), new(view), call);
    }
    private sealed class ViewProbe { public void Observe(LearningDictionaryState state) { } }

    [Fact]
    public async Task SnapshotCommandsDoNotTurnAStaleStopIntoARefresh()
    {
        var explainer = new Explainer(); await using var session = new LearningDictionarySession(new Dictionary(), explainer);
        try
        {
            await session.OpenAsync(Word, "doc", LearningLanguage.English); var call = await explainer.Next();
            var target = session.State.TargetIdentity; var generating = session.State.Explanation!;
            call.Complete(); await Settled(() => session.State.Explanation?.Record is not null);
            await session.ExplainAsync(target, generating, refresh: true); session.CancelExplanation(target, generating);
            Assert.Equal(1, explainer.Generations); Assert.NotNull(session.State.Explanation!.Record);
            var refresh = session.ExplainAsync(target, session.State.Explanation!, refresh: true);
            var next = await explainer.Next(); next.Complete(); await refresh.WaitAsync(Timeout);
            Assert.Equal(2, explainer.Generations); Assert.NotNull(session.State.Explanation!.Record);
        }
        finally { session.Close(); explainer.FinishAll(); }
    }

    [Fact]
    public async Task SelectingTheSameInputRetainsLiveWorkAndCompleteRecordLikeMacSetTarget()
    {
        var explainer = new Explainer(); await using var session = new LearningDictionarySession(new Dictionary(), explainer);
        try
        {
            await session.OpenAsync(Pattern, "doc", LearningLanguage.English, Capture.Context, Capture); await explainer.Next();
            await session.SelectWordAsync(Pattern.Words[0], session.State.TargetIdentity); var word = await explainer.Next();
            var previous = session.State.TargetIdentity;
            await session.SelectWordAsync(Pattern.Words[0], previous);
            Assert.NotEqual(previous, session.State.TargetIdentity); Assert.Equal(2, explainer.Generations);
            Assert.True(session.State.Explanation!.IsGenerating);
            word.Complete(); await Settled(() => session.State.Explanation?.Record is not null);
            var record = session.State.Explanation!.Record;
            await session.SelectWordAsync(Pattern.Words[0], session.State.TargetIdentity);
            Assert.Same(record, session.State.Explanation!.Record); Assert.True(session.State.ExplanationConfigured);
            Assert.Equal(2, explainer.Generations);
        }
        finally { session.Close(); explainer.FinishAll(); }
    }

    [Fact]
    public async Task DictionaryPageKeepsItsLateResultWithoutChangingTheContextualTarget()
    {
        var completion = new TaskCompletionSource<LearningDictionaryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var explainer = new Explainer(); var provider = new Dictionary { Lookup = () => completion.Task };
        await using var session = new LearningDictionarySession(provider, explainer);
        try
        {
            await session.OpenAsync(Word, "doc", LearningLanguage.English); var call = await explainer.Next();
            var target = session.State.TargetIdentity;
            var dictionary = session.SelectPageAsync(LearningDictionaryPage.Dictionary, target);
            await session.SelectPageAsync(LearningDictionaryPage.Contextual, target);
            call.Complete(); await Settled(() => session.State.Explanation?.Record is not null);
            var record = session.State.Explanation!.Record;
            completion.SetResult(LearningDictionaryResult.NotFound); await dictionary.WaitAsync(Timeout);
            Assert.Equal(target, session.State.TargetIdentity); Assert.Equal(LearningDictionaryPage.Contextual, session.State.Page);
            Assert.Same(record, session.State.Explanation!.Record); Assert.Equal(1, explainer.Generations);
        }
        finally { completion.TrySetResult(LearningDictionaryResult.NotFound); session.Close(); explainer.FinishAll(); }
    }

    private static async Task Settled(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); ++i) await Task.Delay(2);
        Assert.True(condition());
    }
    private sealed class Dictionary : ILearningDictionaryProviding
    {
        public int Calls;
        public Func<Task<LearningDictionaryResult>>? Lookup;
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return Lookup?.Invoke() ?? Task.FromResult(LearningDictionaryResult.NotFound); }
    }
    private sealed record Call(LearningExplanationInput Input, CancellationToken Token)
    {
        public TaskCompletionSource<LearningExplanationRecord> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => Completion.TrySetResult(new(Guid.NewGuid(), Input, LearningLanguage.Chinese,
            new("Contextual meaning", "Generated explanation."), "fixture", DateTimeOffset.UtcNow));
    }
    private sealed class Explainer : ILearningExplanationGenerating
    {
        private readonly object _subscriptionGate = new();
        private Action? _changed;
        public Action? Subscribe;
        public event Action? Changed
        {
            add { Subscribe?.Invoke(); lock (_subscriptionGate) _changed += value; }
            remove { lock (_subscriptionGate) _changed -= value; }
        }
        public int Subscribers { get { lock (_subscriptionGate) return _changed?.GetInvocationList().Length ?? 0; } }
        public bool Configured = true;
        public bool IsConfigured => Configured;
        public LearningLanguage Language => LearningLanguage.Chinese;
        public int Generations;
        public Func<LearningExplanationInput, LearningExplanationRecord?>? Saved;
        private readonly Channel<Call> _calls = Channel.CreateUnbounded<Call>();
        private readonly ConcurrentQueue<Call> _all = new();
        public Task<Call> Next() => _calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        public Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId, bool refresh = false, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Generations); var call = new Call(input, cancellationToken); _all.Enqueue(call); _calls.Writer.TryWrite(call); return call.Completion.Task; }
        public LearningExplanationRecord? SavedRecord(LearningExplanationInput input) => Saved?.Invoke(input);
        public Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input) => Task.FromResult<LearningExplanationRecord?>(null);
        public void RecordDisplay(string requestId) { }
        public void FinishAll() { foreach (var call in _all) call.Complete(); }
        public void Change() { Action? handlers; lock (_subscriptionGate) handlers = _changed; handlers?.Invoke(); }
    }
}
