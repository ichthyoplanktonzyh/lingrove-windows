using System.Windows.Threading;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ExpressionAssistFeatureTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RetiredQueuedRefreshCannotReadOrPublish(bool shutdown) => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus { Current = Field("word?") }; var hook = new Hook(); int captures = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        FocusedField? Guarded(Func<bool> permit)
        {
            entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10));
            return permit() ? focus.Refresh() : null;
        }
        var feature = new ExpressionAssistFeature(Dispatcher.CurrentDispatcher, focus, hook, Guarded,
            _ => (Session(new Editor(() => captures++), new Panel()), () => { }), (_, _) => false);
        var settings = new ExpressionAssistFeatureSettings(true, "??", 0, KeyModifiers.None, true, 1);
        try
        {
            feature.Start(settings);
            hook.Emit(new KeyboardEvent { Kind = KeyKind.Character, Character = '?', IsKeyDown = true });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task? drain = null;
            if (shutdown) { drain = feature.DisposeAsync().AsTask(); Assert.False(drain.IsCompleted); }
            else feature.SettingsChanged(settings with { Available = false, Revision = 2 });
            focus.Current = Field("different??") with { FieldId = "other", FocusChangeSequence = 2 };
            release.Set();
            if (drain is not null) await drain.WaitAsync(TimeSpan.FromSeconds(10));
            else await Task.Delay(100);
            Assert.Equal(0, focus.Refreshes); Assert.Equal(0, captures);
        }
        finally { release.Set(); await feature.DisposeAsync(); }
    });

    private static FocusedField Field(string text) => new()
    {
        ProcessId = 1, ProcessName = "editor", FieldId = "field", FocusChangeSequence = 1,
        Text = text, CaretOffset = text.Length, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty,
        IsSingleLine = true, IsSecure = false, HasReliableSelection = true,
        Composition = InputCompositionState.Clear, CompositionSequence = 1,
    };
    private static async Task Until(Func<bool> condition)
    { for (int i = 0; i < 150 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }

    [Fact]
    public Task ShortcutConsumesSynchronouslyButCaptureIsDeferredAndSettingsRetire() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus { Current = Field("") }; var hook = new Hook(); int captures = 0; int closed = 0; bool conflict = true;
        var settings = new ExpressionAssistFeatureSettings(true, "??", 0x45, KeyModifiers.Control, true, 1);
        await using var feature = new ExpressionAssistFeature(Dispatcher.CurrentDispatcher, focus, hook, permit => permit() ? focus.Refresh() : null,
            _ => (Session(new Editor(() => captures++), new Panel()), () => closed++), (_, _) => conflict);
        feature.Start(settings);
        var binding = new KeyboardEvent { Kind = KeyKind.Shortcut, Character = '\0', IsKeyDown = true, VirtualKey = 0x45, Modifiers = KeyModifiers.Control };
        Assert.False(hook.Emit(binding).Suppress); conflict = false;
        Assert.True(hook.Emit(binding with { IsRepeat = true }).Suppress); Assert.Equal(0, captures);
        var press = hook.Emit(binding);
        Assert.True(press.Suppress); Assert.Equal(0, captures); Assert.Equal(0, focus.Refreshes);
        await Until(() => captures == 1 && feature.IsActive);
        var during = hook.Emit(new KeyboardEvent { Kind = KeyKind.Shortcut, Character = '\0', IsKeyDown = true, VirtualKey = 0x45, Modifiers = KeyModifiers.Control });
        Assert.False(during.Suppress);
        feature.SettingsChanged(settings with { Available = false, Revision = 2 });
        await Until(() => !feature.IsActive && closed == 1);
        Assert.False(hook.Emit(new KeyboardEvent { Kind = KeyKind.Shortcut, Character = '\0', IsKeyDown = true, VirtualKey = 0x45, Modifiers = KeyModifiers.Control }).Suppress);
        Assert.Equal(0, hook.Starts); Assert.Equal(0, focus.Starts);
    });

    [Fact]
    public Task DelayedShortcutRejectsNewSecureOrComposingFocus() => dispatcher.RunAsync(async () =>
    {
        foreach (bool secure in new[] { false, true })
        {
            var focus = new Focus { Current = Field("") }; var hook = new Hook(); int captures = 0;
            await using var feature = new ExpressionAssistFeature(Dispatcher.CurrentDispatcher, focus, hook, permit => permit() ? focus.Refresh() : null,
                _ => (Session(new Editor(() => captures++), new Panel()), () => { }), (_, _) => false);
            feature.Start(new(true, "??", 0x45, KeyModifiers.Control, true, 1));
            hook.Emit(new KeyboardEvent { Kind = KeyKind.Shortcut, Character = '\0', IsKeyDown = true, VirtualKey = 0x45, Modifiers = KeyModifiers.Control });
            focus.Current = secure ? Field("") with { IsSecure = true } : Field("") with { Composition = InputCompositionState.Composing };
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(0, captures); Assert.False(feature.IsActive);
        }
    });

    [Fact]
    public Task RefreshCanPublishNewClearCompositionReceiptWithoutFocusEvent() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus { Current = Field("word?") }; var hook = new Hook(); int captures = 0;
        await using var feature = new ExpressionAssistFeature(Dispatcher.CurrentDispatcher, focus, hook, permit => permit() ? focus.Refresh() : null,
            _ => (Session(new Editor(() => captures++), new Panel()), () => { }), (_, _) => false);
        feature.Start(new(true, "??", 0, KeyModifiers.None, true, 1));
        hook.Emit(new KeyboardEvent { Kind = KeyKind.Character, Character = '?', IsKeyDown = true });
        focus.Current = Field("word??") with { CompositionSequence = 2 };
        await Until(() => captures == 1);
    });

    [Fact]
    public Task SettingsWithdrawalRetiresQueuedShortcutBeforeCapture() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus { Current = Field("") }; var hook = new Hook(); int captures = 0;
        await using var feature = new ExpressionAssistFeature(Dispatcher.CurrentDispatcher, focus, hook, permit => permit() ? focus.Refresh() : null,
            _ => (Session(new Editor(() => captures++), new Panel()), () => { }), (_, _) => false);
        var settings = new ExpressionAssistFeatureSettings(true, "??", 0x45, KeyModifiers.Control, true, 1);
        feature.Start(settings);
        Assert.True(hook.Emit(new KeyboardEvent { Kind = KeyKind.Shortcut, Character = '\0', IsKeyDown = true, VirtualKey = 0x45, Modifiers = KeyModifiers.Control }).Suppress);
        feature.SettingsChanged(settings with { Enabled = false, Revision = 2 });
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(0, captures); Assert.False(feature.IsActive);
    });

    [Fact]
    public Task FocusChangeRetiresOnlyAfterPanelLosesKeyboard() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus { Current = Field("") }; var hook = new Hook(); var panel = new Panel(); int closed = 0;
        await using var feature = new ExpressionAssistFeature(Dispatcher.CurrentDispatcher, focus, hook, permit => permit() ? focus.Refresh() : null,
            _ => (Session(new Editor(() => { }), panel), () => closed++), (_, _) => false);
        feature.Start(new(true, "??", 0x45, KeyModifiers.Control, true, 1));
        hook.Emit(new KeyboardEvent { Kind = KeyKind.Shortcut, Character = '\0', IsKeyDown = true, VirtualKey = 0x45, Modifiers = KeyModifiers.Control });
        await Until(() => feature.IsActive);
        focus.Publish(null); await Task.Delay(30); Assert.True(feature.IsActive);
        panel.LoseFocus(); focus.Publish(null);
        await Until(() => !feature.IsActive && closed == 1);
    });

    [Fact]
    public Task ProbeIsBoundedAndShutdownJoinsOutstandingRefresh() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus { Current = Field("word?") }; var hook = new Hook(); int captures = 0;
        var feature = new ExpressionAssistFeature(Dispatcher.CurrentDispatcher, focus, hook, permit => permit() ? focus.Refresh() : null,
            _ => (Session(new Editor(() => captures++), new Panel()), () => { }), (_, _) => false);
        feature.Start(new(true, "??", 0, KeyModifiers.None, true, 1));
        hook.Emit(new KeyboardEvent { Kind = KeyKind.Character, Character = '?', IsKeyDown = true });
        await Until(() => focus.Refreshes == 4); await Task.Delay(60);
        Assert.Equal(4, focus.Refreshes); Assert.Equal(0, captures);
        using var block = new ManualResetEventSlim();
        focus.Wait = block;
        hook.Emit(new KeyboardEvent { Kind = KeyKind.Character, Character = '?', IsKeyDown = true });
        await Until(() => focus.Refreshes == 5);
        var shutdown = feature.DisposeAsync().AsTask(); Assert.False(shutdown.IsCompleted);
        block.Set(); await shutdown; Assert.Equal(0, captures);
    });

    [Fact]
    public Task MutationProbesCommittedFocusOnlyAndRetiresOldEpochs() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus { Current = Field("word?") }; var hook = new Hook(); int captures = 0; string? marker = null;
        var editor = new Editor(() => captures++, value => marker = value);
        await using var feature = new ExpressionAssistFeature(Dispatcher.CurrentDispatcher, focus, hook, permit => permit() ? focus.Refresh() : null,
            _ => (Session(editor, new Panel()), () => { }), (_, _) => false);
        feature.Start(new(true, "??", 0, KeyModifiers.None, true, 1));
        focus.Publish(Field("word??")); await Task.Delay(50); Assert.Equal(0, captures); // Focus entry alone is not a command.
        focus.Current = Field("word?");
        hook.Emit(new KeyboardEvent { Kind = KeyKind.Character, Character = 'a', IsKeyDown = true });
        await Task.Delay(40); Assert.Equal(0, focus.Refreshes);
        hook.Emit(new KeyboardEvent { Kind = KeyKind.Character, Character = '?', IsKeyDown = true });
        focus.Current = Field("word??") with { Composition = InputCompositionState.Composing };
        await Task.Delay(60); Assert.Equal(0, captures);
        focus.Publish(Field("word??"));
        await Until(() => captures == 1); Assert.Equal("??", marker); Assert.InRange(focus.Refreshes, 1, 4);
    });

    private static ExpressionAssistSession Session(Editor editor, Panel panel) => new(Dispatcher.CurrentDispatcher, editor, panel,
        (_, _) => throw new InvalidOperationException(), () => (LearningLanguage.English, LearningLanguage.Chinese));
    private sealed class Hook : IKeyboardHook
    {
        public event EventHandler<KeyEventArgs>? KeyEvent; public int Starts;
        public KeyEventArgs Emit(KeyboardEvent key) { var args = new KeyEventArgs(key); KeyEvent?.Invoke(this, args); return args; }
        public void Start() => Starts++; public void Stop() { } public void Dispose() { }
    }
    private sealed class Focus : IFocusTracker
    {
        public FocusedField? Current { get; set; } public int Refreshes, Starts; public ManualResetEventSlim? Wait;
        public event EventHandler<FocusedField?>? FocusChanged;
        public void Publish(FocusedField? value) { Current = value; FocusChanged?.Invoke(this, value); }
        public FocusedField? Refresh() { Interlocked.Increment(ref Refreshes); Wait?.Wait(TimeSpan.FromSeconds(10)); return Current; }
        public void Start() => Starts++; public void Stop() { } public void Dispose() { }
    }
    private sealed class Editor(Action capture, Action<string?>? marker = null) : IExpressionAssistEditor
    {
        public Task<IExpressionAssistEditorSession?> CaptureAsync(string? trigger, nint hwnd, int? key, Func<bool> owns, CancellationToken ct)
        { capture(); marker?.Invoke(trigger); return Task.FromResult<IExpressionAssistEditorSession?>(new FakeCapture(trigger)); }
    }
    private sealed class FakeCapture(string? trigger) : IExpressionAssistEditorSession
    {
        private readonly ExpressionEditorObservation _field = new(1, "field", 1, 1, "??", new(2, 0), false, true, InputCompositionState.Clear, 1);
        public ExpressionEditorReceipt Receipt => ExpressionEditorReceiptPolicy.Capture(_field, trigger)!;
        public event Action? Invalidated { add { } remove { } }
        public bool EnterPanel() => true; public Task<bool> IsUnchangedAsync(CancellationToken ct) => Task.FromResult(true);
        public bool BeginReturn() => true; public Task<bool> ResumeAndReplaceAsync(string text, CancellationToken ct) => Task.FromResult(true);
        public void Retire() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Panel : IExpressionAssistPanel
    {
        private bool _visible, _owns; public nint WindowHandle => 1; public bool OwnsKeyboard => _owns; public bool IsVisible => _visible;
        public void LoseFocus() => _owns = false;
        public Task ShowAsync(ExpressionEditorReceipt receipt, string query) { _visible = _owns = true; return Task.CompletedTask; }
        public void Update(ExpressionAssistResult? result, string? message, bool loading) { } public void Hide(bool restoringHost) { _visible = _owns = false; }
        public void ShowFailure(string candidate, string message) { } public void ShowUnavailable(string message) { }
    }
}
