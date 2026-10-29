using System.Windows.Threading;
using Cotabby.App.Settings;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Actual app adapter with synthetic events only. Denied commands never construct the
/// real panel. No AppHost, user's settings, external editor, native input or model is used.</summary>
[Collection("WPF views")]
public sealed class ExpressionAssistApplicationTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task NoticesAreReplacedAndClosedOnRelevantSettingsOrShutdown() => dispatcher.RunAsync(async () =>
    {
        var app = new ExpressionAssistApplication(Dispatcher.CurrentDispatcher, new Focus(), new Hook(),
            _ => throw new InvalidOperationException(), new UnavailableExpressionAssistEditor(),
            (_, _) => throw new InvalidOperationException(), () => { });
        var settings = new AppSettings(); app.ApplySettings(settings, false);
        app.ShowNotice(ScreenRect.Empty, "First", null);
        var first = app.Notice!; Assert.True(first.IsVisible); Assert.False(first.IsActive);
        app.ShowNotice(ScreenRect.Empty, "Second", null);
        var second = app.Notice!; Assert.False(first.IsVisible); Assert.True(second.IsVisible);
        settings.LearningTargetLanguage = Cotabby.Core.LanguageLearning.LearningLanguage.French;
        app.ApplySettings(settings, false); Assert.False(second.IsVisible); Assert.Null(app.Notice);
        app.ShowNotice(ScreenRect.Empty, "Last", null); var last = app.Notice!;
        await app.DisposeAsync(); Assert.False(last.IsVisible); Assert.Null(app.Notice);
        app.ShowNotice(ScreenRect.Empty, "Too late", null); Assert.Null(app.Notice);
    });
    [Fact]
    public Task ReadingSourceShortcutChangesRetireExpressionSnapshot() => dispatcher.RunAsync(async () =>
    {
        await using var app = new ExpressionAssistApplication(Dispatcher.CurrentDispatcher, new Focus(), new Hook(),
            _ => throw new InvalidOperationException(), new UnavailableExpressionAssistEditor(),
            (_, _) => throw new InvalidOperationException(), () => { });
        var settings = new AppSettings(); app.ApplySettings(settings, false);
        app.ShowNotice(ScreenRect.Empty, "First", null);
        settings.LearningReadingPointerEnabled = true; app.ApplySettings(settings, false);
        Assert.Null(app.Notice);
        app.ShowNotice(ScreenRect.Empty, "Second", null);
        settings.LearningReadingScreenshotShortcut = new(0x45, KeyModifiers.Control, "Ctrl+E");
        app.ApplySettings(settings, false); Assert.Null(app.Notice);
    });
    private static FocusedField Field => new()
    {
        ProcessId = Environment.ProcessId + 1, ProcessName = "editor", FieldId = "field", Text = "word??",
        CaretOffset = 6, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty, IsSingleLine = true,
        IsSecure = false, HasReliableSelection = true, Composition = InputCompositionState.Clear,
        CompositionSequence = 1, FocusChangeSequence = 1,
    };

    [Fact]
    public Task EligibilityIsDetachedAndExplicitSelectionKeepsAllEnvironmentGates() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var hook = new Hook(); int calls = 0;
        await using var app = new ExpressionAssistApplication(Dispatcher.CurrentDispatcher, focus, hook,
            _ => throw new InvalidOperationException("No native refresh expected"), new UnavailableExpressionAssistEditor(),
            (_, _) => { calls++; throw new InvalidOperationException("No model expected"); }, () => calls++);
        var settings = new AppSettings { ExpressionAssistEnabled = true, LearningEndpointEnabled = true };
        app.ApplySettings(settings, false);
        Assert.True(app.FieldAvailable(Field with { HasSelection = true }));
        settings.BlockedApps.Add("editor");
        Assert.True(app.FieldAvailable(Field));
        app.ApplySettings(settings, false); Assert.False(app.FieldAvailable(Field));
        settings.BlockedApps.Clear(); app.ApplySettings(settings, false);
        settings.SuggestInIntegratedTerminals = true; app.ApplySettings(settings, false);
        Assert.False(app.FieldAvailable(Field with { ClassName = "xterm-helper-textarea" }));
        Assert.False(app.FieldAvailable(Field with { IsSecure = true }));
        Assert.False(app.FieldAvailable(Field with { ProcessId = Environment.ProcessId }));
        Assert.False(app.FieldAvailable(Field with { ProcessName = "powershell" }));
        settings.Enabled = false; app.ApplySettings(settings, false); Assert.False(app.FieldAvailable(Field));
        settings.Enabled = true; app.ApplySettings(settings, true); Assert.False(app.FieldAvailable(Field));
        Assert.Equal(0, calls); Assert.Equal(0, hook.Starts); Assert.Equal(0, focus.Starts);
    });

    [Fact]
    public Task BlockedAppDoesNotConsumeShortcutProbeOrCreatePanelAndDisposalJoinsOnUi() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var hook = new Hook(); int effects = 0;
        var app = new ExpressionAssistApplication(Dispatcher.CurrentDispatcher, focus, hook,
            _ => { effects++; throw new InvalidOperationException(); }, new UnavailableExpressionAssistEditor(),
            (_, _) => { effects++; throw new InvalidOperationException(); }, () => effects++);
        var settings = new AppSettings
        {
            ExpressionAssistEnabled = true, ExpressionAssistShortcut = new(0x45, KeyModifiers.Control, "Ctrl+E"),
            BlockedApps = ["editor"],
        };
        app.ApplySettings(settings, false);
        Assert.False(hook.Emit(new() { Kind = KeyKind.Shortcut, Character = '\0', VirtualKey = 0x45,
            Modifiers = KeyModifiers.Control, IsKeyDown = true }).Suppress);
        hook.Emit(new() { Kind = KeyKind.Character, Character = '?', IsKeyDown = true });
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(0, effects); Assert.False(app.IsActive);
        await Task.Run(async () => await app.DisposeAsync());
        await app.DisposeAsync();
    });

    private sealed class Focus : IFocusTracker
    {
        public int Starts;
        public FocusedField? Current => Field;
        public event EventHandler<FocusedField?>? FocusChanged { add { } remove { } }
        public FocusedField? Refresh() => throw new InvalidOperationException();
        public void Start() => Starts++; public void Stop() { } public void Dispose() { }
    }
    private sealed class Hook : IKeyboardHook
    {
        public int Starts; public event EventHandler<KeyEventArgs>? KeyEvent;
        public KeyEventArgs Emit(KeyboardEvent key) { var args = new KeyEventArgs(key); KeyEvent?.Invoke(this, args); return args; }
        public void Start() => Starts++; public void Stop() { } public void Dispose() { }
    }
}
