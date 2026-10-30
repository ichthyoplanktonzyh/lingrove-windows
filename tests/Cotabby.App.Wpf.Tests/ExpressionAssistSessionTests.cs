using System.Windows.Threading;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ExpressionAssistSessionTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ResultCarriesTheExactFrozenRequestAndDetachedFavoriteNeedsNoNewHostRead() => dispatcher.RunAsync(async () =>
    {
        var editor = new Editor(false); var panel = new Panel(); var candidate = new ExpressionAssistCandidate("rule out", "排除", null);
        ExpressionAssistRequest? generated = null;
        await using var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, editor, panel,
            (request, _) => { generated = request; return Task.FromResult(new ExpressionAssistResult([candidate], null)); },
            () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin(null); await Until(() => panel.Visible);
        session.QueryChanged("排除", false); session.Submit(); await Until(() => panel.Result is not null);
        Assert.Same(generated, panel.Request); Assert.Equal("排除", panel.Request!.Query);
        int reads = editor.Session.Reads;
        Assert.True(await session.ValidateCandidateAsync(candidate));
        editor.Session.Unchanged = false; Assert.True(await session.ValidateCandidateAsync(candidate));
        Assert.Equal(reads, editor.Session.Reads);
        session.QueryChanged("new query", true); Assert.False(await session.ValidateCandidateAsync(candidate));
        Assert.Equal(0, editor.Session.Writes);
    });

    [Fact]
    public Task SessionShutdownJoinsItsPanelsAcceptedCandidateActionsExactlyOnce() => dispatcher.RunAsync(async () =>
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var panel = new Panel { CandidateDrain = held.Task };
        var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, new Editor(false), panel,
            (_, _) => Task.FromResult(new ExpressionAssistResult([], null)), () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin(null); await Until(() => panel.Visible);
        var disposal = session.DisposeAsync().AsTask(); await Until(() => panel.DrainCalls == 1);
        Assert.False(disposal.IsCompleted); held.SetResult(); await disposal;
        await session.DisposeAsync(); Assert.Equal(1, panel.DrainCalls);
    });
    private static async Task Until(Func<bool> predicate)
    {
        for (int i = 0; i < 100 && !predicate(); i++) await Task.Delay(10);
        Assert.True(predicate());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RetiredCaptureCannotActivateBeforeOrDuringPanelPreparation(bool duringPreparation) => dispatcher.RunAsync(async () =>
    {
        var editor = new Editor(false); var panel = new Panel();
        if (duringPreparation) panel.BeforeShow = editor.Session.Invalidate;
        else editor.Session.Invalidate(); // Notification occurred before UI subscription.
        await using var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, editor, panel,
            (_, _) => throw new InvalidOperationException(), () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin(null); await Until(() => !session.IsActive);
        Assert.Equal(0, panel.Shows); Assert.False(panel.Visible); Assert.Equal(0, editor.Session.Writes);
    });

    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 0)]
    public Task ConsumedNativeNotificationCannotSwallowUnverifiedFeedback(bool verified, bool hostStillForeground, int expectedFailures) => dispatcher.RunAsync(async () =>
    {
        var editor = new Editor(false); var panel = new Panel();
        editor.Session.InvalidateOnWrite = true; editor.Session.VerifyWrite = verified;
        editor.Session.IsHostForeground = hostStillForeground;
        var candidate = new ExpressionAssistCandidate("rule out", "排除", null);
        await using var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, editor, panel,
            (_, _) => Task.FromResult(new ExpressionAssistResult([candidate], null)), () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin(null); await Until(() => panel.Visible);
        session.QueryChanged("排除", false); session.Submit(); await Until(() => panel.Result is not null);
        session.Accept(candidate); await Until(() => !session.IsActive);
        Assert.Equal(1, editor.Session.Writes); Assert.Equal(expectedFailures, panel.Failures);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidationLatchReplaysTerminalStateAcrossSubscriptionHandoffs(bool retireFirst)
    {
        var latch = new ExpressionInvalidationLatch(); int calls = 0;
        if (retireFirst) latch.Retire();
        latch.Invalidated += () => calls++;
        latch.Retire(); latch.Retire();
        Assert.True(latch.IsRetired); Assert.Equal(1, calls);
    }

    [Fact]
    public Task MissingExplanationModelShowsConfigurationMessageWithoutWriting() => dispatcher.RunAsync(async () =>
    {
        var editor = new Editor(false); var panel = new Panel();
        await using var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, editor, panel,
            (_, _) => Task.FromException<ExpressionAssistResult>(new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured)),
            () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin(null); await Until(() => panel.Visible);
        session.QueryChanged("query", false); session.Submit();
        await Until(() => panel.Message is not null);
        Assert.Contains("AI explanation model", panel.Message); Assert.Equal(0, editor.Session.Writes);
    });

    [Fact]
    public Task EditorInvalidationDiscardsLateReplyWithoutWriting() => dispatcher.RunAsync(async () =>
    {
        var editor = new Editor(false); var panel = new Panel();
        var pending = new TaskCompletionSource<ExpressionAssistResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        await using var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, editor, panel,
            (_, _) => { calls++; return pending.Task; }, () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin(null); await Until(() => panel.Visible);
        session.QueryChanged("query", false); session.Submit(); await Until(() => calls == 1);
        editor.Session.Invalidate(); Assert.False(session.IsActive);
        pending.SetResult(new([new("late", "", null)], null));
        await Task.Delay(30); Assert.Null(panel.Result); Assert.Equal(0, editor.Session.Writes);
    });

    [Fact]
    public Task ChangedHostBeforePresentationOrGenerationClosesSession() => dispatcher.RunAsync(async () =>
    {
        var editor = new Editor(false); editor.Session.Unchanged = false;
        var panel = new Panel(); int calls = 0;
        await using var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, editor, panel,
            (_, _) => { calls++; throw new InvalidOperationException(); }, () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin(null); await Until(() => !session.IsActive);
        Assert.False(panel.Visible); Assert.Equal(0, calls); Assert.Equal(0, editor.Session.Writes);
    });

    [Fact]
    public Task UnsupportedTriggerIsExplicitAndNeverShowsAnInputOrWrites() => dispatcher.RunAsync(async () =>
    {
        var panel = new Panel();
        await using var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, new UnsupportedEditor(), panel,
            (_, _) => throw new InvalidOperationException(), () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin("？？"); await Until(() => panel.Unavailable is not null);
        Assert.Contains("ASCII", panel.Unavailable); Assert.False(panel.Visible); Assert.False(session.IsActive);
    });

    [Fact]
    public Task ShutdownInvalidatesAndDrainsUncooperativeGeneration() => dispatcher.RunAsync(async () =>
    {
        var editor = new Editor(false); var panel = new Panel();
        var pending = new TaskCompletionSource<ExpressionAssistResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, editor, panel,
            (_, _) => { calls++; return pending.Task; }, () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin(null); await Until(() => panel.Visible);
        session.QueryChanged("query", false); session.Submit(); await Until(() => calls == 1);
        var shutdown = session.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted); Assert.False(panel.Visible);
        pending.SetResult(new([new("late", "", null)], null));
        await shutdown; Assert.Null(panel.Result); Assert.Equal(0, editor.Session.Writes);
        await session.DisposeAsync();
    });

    [Fact]
    public Task CompositionRetiresOldReplyAndAcceptanceUsesOnlyVerifiedReturn() => dispatcher.RunAsync(async () =>
    {
        var editor = new Editor(false); var panel = new Panel();
        var pending = new TaskCompletionSource<ExpressionAssistResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        await using var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, editor, panel,
            (_, _) => { calls++; return pending.Task; }, () => (LearningLanguage.English, LearningLanguage.Chinese));
        session.Begin(null); await Until(() => panel.Visible);
        session.QueryChanged("排除", false); session.Submit(); await Until(() => calls == 1);
        session.QueryChanged("排除缓存", true);
        pending.SetResult(new([new("rule out", "排除", null)], null));
        await Task.Delay(30); Assert.Null(panel.Result);
        session.Accept(new("rule out", "排除", null)); Assert.Equal(0, editor.Session.Writes);
        session.QueryChanged("排除缓存", false); session.Submit(); await Until(() => panel.Result is not null);
        session.Accept(panel.Result!.Candidates[0]); await Until(() => editor.Session.Writes == 1);
        Assert.True(editor.Session.ReturnBegun); Assert.Equal("rule out", editor.Session.Written);
    });

    [Fact]
    public Task TriggerCancelRemovesOnlyMarkerWhileShortcutCancelDoesNotWrite() => dispatcher.RunAsync(async () =>
    {
        foreach (bool trigger in new[] { false, true })
        {
            var editor = new Editor(trigger); var panel = new Panel();
            await using var session = new ExpressionAssistSession(Dispatcher.CurrentDispatcher, editor, panel,
                (_, _) => throw new InvalidOperationException(), () => (LearningLanguage.English, LearningLanguage.Chinese));
            session.Begin(trigger ? "??" : null); await Until(() => panel.Visible);
            session.Cancel(); await Until(() => !session.IsActive);
            Assert.Equal(trigger ? 1 : 0, editor.Session.Writes);
            if (trigger) Assert.Equal("", editor.Session.Written);
        }
    });

    private sealed class Editor(bool trigger) : IExpressionAssistEditor
    {
        public EditorSession Session { get; } = new(trigger);
        public Task<IExpressionAssistEditorSession?> CaptureAsync(string? marker, nint hwnd, int? key, Func<bool> owns, CancellationToken ct) => Task.FromResult<IExpressionAssistEditorSession?>(Session);
    }
    private sealed class UnsupportedEditor : IExpressionAssistEditor
    {
        public Task<IExpressionAssistEditorSession?> CaptureAsync(string? marker, nint hwnd, int? key, Func<bool> owns, CancellationToken ct) =>
            Task.FromException<IExpressionAssistEditorSession?>(new NotSupportedException());
    }
    private sealed class EditorSession(bool trigger) : IExpressionAssistEditorSession
    {
        public bool Live = true;
        public bool CanPresentPanel => Live;
        public bool IsHostForeground { get; set; } = true;
        private readonly ExpressionEditorObservation _observation = new(1, "field", 1, 1, "??", new(2, 0), false, true, InputCompositionState.Clear, 1);
        public ExpressionEditorReceipt Receipt => ExpressionEditorReceiptPolicy.Capture(_observation, trigger ? "??" : null)!;
        public event Action? Invalidated;
        public void Invalidate() { Live = false; Invalidated?.Invoke(); }
        public bool InvalidateOnWrite, VerifyWrite = true;
        public int Writes, Reads; public string? Written; public bool ReturnBegun; public bool Unchanged = true;
        public bool EnterPanel() => true;
        public Task<bool> IsUnchangedAsync(CancellationToken ct) { Reads++; return Task.FromResult(Unchanged); }
        public bool BeginReturn() { ReturnBegun = true; return true; }
        public Task<bool> ResumeAndReplaceAsync(string text, CancellationToken ct)
        { Writes++; Written = text; if (InvalidateOnWrite) Invalidate(); return Task.FromResult(VerifyWrite); }
        public void Retire() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Panel : IExpressionAssistPanel
    {
        public nint WindowHandle => 1; public bool OwnsKeyboard => Visible; public bool IsVisible => Visible;
        public bool Visible; public ExpressionAssistResult? Result; public string? Unavailable; public string? Message;
        public Action? BeforeShow; public int Shows, Failures;
        internal ExpressionAssistRequest? Request; internal Task CandidateDrain = Task.CompletedTask; internal int DrainCalls;
        public Task ShowAsync(ExpressionEditorReceipt receipt, string query, Func<bool> mayActivate)
        { BeforeShow?.Invoke(); if (mayActivate()) { Visible = true; Shows++; } return Task.CompletedTask; }
        public void Update(ExpressionAssistResult? result, string? message, bool loading) { Result = result; Message = message; }
        public void Update(ExpressionAssistResult? result, string? message, bool loading, ExpressionAssistRequest request)
        { Request = request; Update(result, message, loading); }
        public ValueTask DrainCandidateActionsAsync() { DrainCalls++; return new(CandidateDrain); }
        public void Hide(bool restoringHost) => Visible = false;
        public void ShowFailure(string candidate, string message) => Failures++;
        public void ShowUnavailable(string message) => Unavailable = message;
    }
}
