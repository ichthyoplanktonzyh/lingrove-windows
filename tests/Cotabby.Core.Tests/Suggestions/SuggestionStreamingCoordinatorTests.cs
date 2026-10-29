using Cotabby.Core.Suggestions;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class SuggestionStreamingCoordinatorTests : CoordinatorHarness
{
    [Fact]
    public async Task FinalResultRetiresPartialQueuedBehindBlockedUi()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.StreamOverride = async (_, partial, _) => { entered.TrySetResult(); await emit.Task; partial!(" wrong "); emitted.TrySetResult(); return " right"; };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = true, PredictAheadWhileTyping = false });
        try
        {
            Type("hello"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            BlockUiUntil(uiRelease.Task, uiEntered); await uiEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            emit.TrySetResult(); await emitted.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Task.Delay(100);
            uiRelease.TrySetResult(); await Shown(" right"); Assert.DoesNotContain(" wrong ", Overlay.ShownTexts);
        }
        finally { uiRelease.TrySetResult(); emit.TrySetResult(); }
    }
    [Fact]
    public async Task BlockedUiCoalescesPartialBurstAndLatestEventuallyRenders()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int refreshes = 0; Focus.AfterRefresh = _ => Interlocked.Increment(ref refreshes);
        Engine.StreamOverride = async (_, partial, _) =>
        {
            entered.TrySetResult(); await emit.Task;
            for (int i = 0; i < 1000; i++) partial!(" world " + new string('x', i + 1));
            emitted.TrySetResult(); await finish.Task; return " world " + new string('x', 1000);
        };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = true, PredictAheadWhileTyping = false });
        try
        {
            Type("hello"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            BlockUiUntil(uiRelease.Task, uiEntered); await uiEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            int baseline = Volatile.Read(ref refreshes); emit.TrySetResult(); await emitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The single worker may refresh once more after the typing-pause delay.
            await Task.Delay(100); Assert.InRange(Volatile.Read(ref refreshes) - baseline, 0, 2);
            uiRelease.TrySetResult(); await Shown(" world " + new string('x', 1000)); finish.TrySetResult();
        }
        finally { uiRelease.TrySetResult(); emit.TrySetResult(); finish.TrySetResult(); }
    }
    [Fact]
    public async Task LocalWordEndingPrefetchIsPreparedBeforeAcceptanceAndReusedAfterIt()
    {
        var prefetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.CanSpeculateLocally = true;
        Engine.GenerateOverride = (request, _) =>
        {
            if (request.Prefix == "I build ") { prefetched.TrySetResult(); return Task.FromResult("a house"); }
            return Task.FromResult("ild");
        };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { UsesEndpoint = false, PredictAheadWhileTyping = false });
        Type("I bu"); await Shown("ild"); await prefetched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("I bu", Focus.Current!.Text); Assert.True(Hook.Press(Backtick));
        await Shown(" a house"); await Eventually(() => Focus.Current!.Text == "I build", "the word ending is accepted");
        Assert.Equal(2, Engine.Requests.Count);
    }
    [Fact]
    public async Task LocalWordEndingPrefetchIsReusedWhenUserTypesThroughIt()
    {
        var prefetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.CanSpeculateLocally = true;
        Engine.GenerateOverride = (request, _) =>
        {
            if (request.Prefix == "I build ") { prefetched.TrySetResult(); return Task.FromResult("a house"); }
            return Task.FromResult("ild");
        };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { UsesEndpoint = false, PredictAheadWhileTyping = false });
        Type("I bu"); await Shown("ild"); await prefetched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Type("ild"); await Shown(" a house"); Assert.Equal(2, Engine.Requests.Count);
    }
    [Fact]
    public async Task EndpointWordEndingDoesNotStartHypotheticalContinuation()
    {
        Engine.CanSpeculateLocally = true; Engine.Completion = _ => "ild";
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { UsesEndpoint = true });
        Type("I bu"); await Shown("ild"); await Task.Delay(150);
        Assert.Single(Engine.Requests); Assert.Equal("I bu", Focus.Current!.Text);
    }
    [Fact]
    public async Task PrefetchedContinuationCannotMoveToAnotherField()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.CanSpeculateLocally = true;
        Engine.GenerateOverride = async (request, _) =>
        {
            if (request.Prefix == "I build ") { entered.TrySetResult(); await release.Task; return "a house"; }
            return "ild";
        };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { UsesEndpoint = false, PredictAheadWhileTyping = false });
        try
        {
            Type("I bu"); await Shown("ild"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Focus.Focus(Fields.Text("other", pid: 200)); release.TrySetResult();
            await Task.Delay(150); Assert.Equal("", Overlay.Text); Assert.Empty(Focus.Insertions);
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task AcceptedLocalContinuationWaitsForExactHostPublication()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.CanSpeculateLocally = true;
        Engine.GenerateOverride = (request, _) =>
        {
            if (request.Prefix.EndsWith("world ", StringComparison.Ordinal)) { started.TrySetResult(); return Task.FromResult("again"); }
            return Task.FromResult(" world");
        };
        Focus.InsertOverride = (_, _, _) => Task.FromResult(true); // host publication is intentionally held
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { PredictAheadWhileTyping = false, UsesEndpoint = false });
        Type("hello"); await Shown(" world"); Assert.True(Hook.Press(Backtick));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal("", Overlay.Text);
        Focus.Append(" world"); await Shown(" again");
        Assert.Contains(Engine.Requests, request => request.Prefix == "hello world ");
    }
    [Fact]
    public async Task AcceptingStreamKeepsCompletedHiddenWordsWhenGenerationIsCancelled()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.StreamOverride = async (_, partial, _) => { partial!(" world again "); await release.Task; return " world again later"; };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = true, PredictAheadWhileTyping = false, ShowFollowingWords = false });
        try
        {
            Type("hello"); await Shown(" world"); Assert.True(Hook.Press(Backtick));
            await Shown(" again"); release.TrySetResult(); await Task.Delay(100);
            Assert.Equal(" again", Overlay.Text); Assert.Single(Engine.Requests);
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task DivergentTypingRetiresOldPredictionAndLateResultCannotPublish()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
        Engine.StreamOverride = async (_, partial, _) =>
        {
            if (Interlocked.Increment(ref calls) != 1) return " new value";
            partial!(" world again"); entered.TrySetResult(); await release.Task; return " world again";
        };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = false, PredictAheadWhileTyping = true, UsesEndpoint = false });
        try
        {
            Type("hello"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Type("x");
            await Shown(" new value"); release.TrySetResult(); await Task.Delay(100);
            Assert.Equal(" new value", Overlay.Text); Assert.Equal(2, Engine.Requests.Count);
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task OneWordModeAcceptsOnlyVisibleWordThenRevealsBufferedTail()
    {
        Engine.Completion = _ => " world again";
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { ShowFollowingWords = false, PredictAheadWhileTyping = false });
        Type("hello"); await Shown(" world"); Assert.True(Hook.Press(Backtick));
        await Shown(" again"); await Eventually(() => Focus.Current!.Text == "hello world", "only the visible word is inserted");
        Assert.Single(Engine.Requests);
    }
    [Fact]
    public async Task OneWordModeConsumesOnlyBufferedSeparatorWhenAutoSpaceIsEnabled()
    {
        Engine.Completion = _ => " world again";
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { ShowFollowingWords = false, PredictAheadWhileTyping = false, AddSpaceAfterAccept = true });
        Type("hello"); await Shown(" world"); Assert.True(Hook.Press(Backtick));
        await Shown("again"); await Eventually(() => Focus.Current!.Text == "hello world ", "the separator is inserted exactly once");
        Assert.Single(Engine.Requests);
    }
    [Fact]
    public async Task FinalWaitsForHostPublicationBeforeRebasing()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.StreamOverride = async (_, partial, _) => { partial!(" world again"); entered.TrySetResult(); await release.Task; return " world again"; };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = false, PredictAheadWhileTyping = true, UsesEndpoint = false });
        try
        {
            Type("hello"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Hook.Press(Keys.Char(' ')); await RunOnUiAsync(() => { }); release.TrySetResult();
            await Task.Delay(80); Assert.Equal("", Overlay.Text); Focus.Append(" ");
            await Shown("world again"); Assert.Single(Engine.Requests);
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task EndpointDoesNotCollectSpeculativePartialsWhenDisplayIsOff()
    {
        var entered = new TaskCompletionSource<Action<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.StreamOverride = async (_, partial, _) => { entered.TrySetResult(partial); await release.Task; return " world"; };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = false, PredictAheadWhileTyping = true, UsesEndpoint = true });
        try { Type("hello"); Assert.Null(await entered.Task.WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task MatchingTypingReusesLocalPredictionEvenWhenPartialsAreHidden()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.StreamOverride = async (_, partial, _) => { partial!(" world again"); entered.TrySetResult(); await release.Task; return " world again"; };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = false, PredictAheadWhileTyping = true, UsesEndpoint = false });
        try
        {
            Type("hello"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Type(" w"); await RunOnUiAsync(() => { }); release.TrySetResult();
            await Shown("orld again"); Assert.Single(Engine.Requests);
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task MatchingTypingRebasesVisibleStreamAndFinalWithoutAnotherRequest()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.StreamOverride = async (_, partial, _) => { partial!(" world again"); await release.Task; return " world again soon"; };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = true, PredictAheadWhileTyping = true, UsesEndpoint = false });
        try
        {
            Type("hello"); await Shown(" world again");
            Type(" wo"); await RunOnUiAsync(() => { }); release.TrySetResult();
            await Shown("rld again soon"); Assert.Single(Engine.Requests);
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task PartialIsAcceptableBeforeFinalAndLateOutputCannotReturn()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Engine.StreamOverride = async (_, partial, _) => { if (Interlocked.Increment(ref calls) != 1) return ""; partial!(" world "); entered.TrySetResult(); await release.Task; return " world again"; };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = true, PredictAheadWhileTyping = false });
        try
        {
            Type("hello"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Shown(" world ");
            Assert.True(Hook.Press(Keys.Tab()));
            await Eventually(() => Focus.Current!.Text == "hello world", "the streamed word is accepted");
            release.TrySetResult(); await Task.Delay(150);
            Assert.NotEqual(" world again", Overlay.Text);
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task FinalSuppressionClearsAlreadyVisiblePartial()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.StreamOverride = async (_, partial, _) => { partial!(" world "); await release.Task; return ""; };
        await RunOnUiAsync(() => Coordinator.Settings = Coordinator.Settings with { StreamSuggestionsWhileGenerating = true, PredictAheadWhileTyping = false });
        try { Type("hello"); await Shown(" world "); release.TrySetResult(); await Eventually(() => Overlay.Text == "", "the final suppression clears the partial"); }
        finally { release.TrySetResult(); }
    }
}
