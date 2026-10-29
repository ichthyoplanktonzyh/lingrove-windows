using System.Windows.Threading;
using Cotabby.App.Reading;
using Cotabby.App.Settings;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Reading;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingApplicationTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task PowerWithdrawalCancelsRequestBeforeUiRetirementAndJoinsProvider() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var hook = new Hook(); var reader = new Reader();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = new ReadingApplication(Dispatcher.CurrentDispatcher, hook, () => focus.Value, reader,
            (_, _) => [new("Hello", new(0, 5)), new("world", new(6, 5))], new Dictionary(),
            (_, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException(), () => { },
            (_, _) => (_, _, _, _, token) => { started.SetResult(token); return pending.Task; }, () => { }, () => false);
        app.ApplySettings(new AppSettings { LearningReadingEnabled = true, LearningTranslationEnabled = true }, 0, false);
        Assert.True(hook.Emit().Suppress); var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Run(app.Withdraw); Assert.True(token.IsCancellationRequested);
        var disposal = app.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
        pending.SetResult("late"); await disposal; Assert.True(reader.Lease.Retired);
    });
    [Fact]
    public Task CapturedDifferentProcessCannotReachModel() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var hook = new Hook(); var reader = new Reader(); int calls = 0;
        reader.Lease.Selection = reader.Lease.Selection with { ProcessId = Environment.ProcessId + 2 };
        var app = new ReadingApplication(Dispatcher.CurrentDispatcher, hook, () => focus.Value, reader,
            (_, _) => [], new Dictionary(), (_, _) => throw new InvalidOperationException(),
            (_, _, _) => throw new InvalidOperationException(), () => { },
            (_, _) => (_, _, _, _, _) => { calls++; return Task.FromResult("bad"); }, () => { }, () => false);
        var settings = new AppSettings { LearningReadingEnabled = true, LearningTranslationEnabled = true };
        app.ApplySettings(settings, 0, false); Assert.True(hook.Emit().Suppress);
        for (int i = 0; i < 100 && !reader.Lease.Retired; i++) await Task.Delay(10);
        Assert.True(reader.Lease.Retired); Assert.Equal(0, calls); await app.DisposeAsync();
    });
    [Fact]
    public Task QueuedShortcutIsWithdrawnBeforeCaptureWhenSettingsChange() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var hook = new Hook(); var reader = new Reader(); int effects = 0;
        var app = new ReadingApplication(Dispatcher.CurrentDispatcher, hook, () => focus.Value, reader,
            (_, _) => [], new Dictionary(), (_, _) => throw new InvalidOperationException(),
            (_, _, _) => throw new InvalidOperationException(), () => { },
            (_, _) => (_, _, _, _, _) => throw new InvalidOperationException(), () => effects++, () => false);
        var settings = new AppSettings { LearningReadingEnabled = true, LearningTranslationEnabled = true };
        app.ApplySettings(settings, 0, false); Assert.True(hook.Emit().Suppress);
        settings.LearningReadingEnabled = false; app.ApplySettings(settings, 0, false);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(0, reader.Calls); Assert.Equal(0, effects); Assert.False(app.IsActive);
        await app.DisposeAsync();
    });
    [Fact]
    public Task ReadOnlyOriginDoesNotRequireEditableSnapshotAndRetiresWhenSettingsChange() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var hook = new Hook(); var reader = new Reader(); int invoked = 0;
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = new ReadingApplication(Dispatcher.CurrentDispatcher, hook, () => focus.Value, reader,
            (_, _) => [new("Hello", new(0, 5)), new("world", new(6, 5))], new Dictionary(),
            (_, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException(), () => { },
            (_, _) => (_, source, _, _, _) => { Assert.Null(source); invoked++; return pending.Task; }, () => { }, () => false);
        var settings = new AppSettings { LearningReadingEnabled = true, LearningTranslationEnabled = true };
        app.ApplySettings(settings, 0, false);
        settings.BlockedApps.Add("editor"); app.ApplySettings(settings, 0, false);
        Assert.False(hook.Emit().Suppress); Assert.Equal(0, reader.Calls);
        settings.BlockedApps.Clear(); app.ApplySettings(settings, 0, false);
        Assert.True(hook.Emit().Suppress);
        for (int i = 0; i < 100 && invoked == 0; i++) await Task.Delay(10);
        Assert.Equal(1, invoked); Assert.Equal(1, reader.Calls); Assert.True(app.IsActive);
        settings.LearningReadingEnabled = false; app.ApplySettings(settings, 0, false);
        Assert.False(app.IsActive); Assert.True(reader.Lease.Retired);
        var disposal = app.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
        pending.SetResult("late"); await disposal;
        Assert.False(hook.Emit().Suppress);
    });

    [Fact]
    public Task UnknownCompositionReceiptIsRejectedBeforeAnyModelWork() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var hook = new Hook(); var reader = new Reader(); int calls = 0;
        reader.Lease.Selection = reader.Lease.Selection with { Composition = InputCompositionState.Unknown, CompositionSequence = 0 };
        var app = new ReadingApplication(Dispatcher.CurrentDispatcher, hook, () => focus.Value, reader,
            (_, _) => [], new Dictionary(), (_, _) => throw new InvalidOperationException(),
            (_, _, _) => throw new InvalidOperationException(), () => { },
            (_, _) => (_, _, _, _, _) => { calls++; return Task.FromResult("bad"); }, () => { }, () => false);
        app.ApplySettings(new AppSettings { LearningReadingEnabled = true, LearningTranslationEnabled = true }, 0, false);
        Assert.True(hook.Emit().Suppress);
        for (int i = 0; i < 100 && !reader.Lease.Retired; i++) await Task.Delay(10);
        Assert.True(reader.Lease.Retired); Assert.Equal(0, calls); await app.DisposeAsync();
    });
    [Fact]
    public Task FocusEpochChangeBeforeQueuedCaptureDoesNotReadSelection() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var hook = new Hook(); var reader = new Reader();
        var app = new ReadingApplication(Dispatcher.CurrentDispatcher, hook, () => focus.Value, reader,
            (_, _) => [], new Dictionary(), (_, _) => throw new InvalidOperationException(),
            (_, _, _) => throw new InvalidOperationException(), () => { },
            (_, _) => (_, _, _, _, _) => throw new InvalidOperationException(), () => { }, () => false);
        app.ApplySettings(new AppSettings { LearningReadingEnabled = true, LearningTranslationEnabled = true }, 0, false);
        Assert.True(hook.Emit().Suppress); focus.Value = focus.Value with { FocusEpoch = 2 };
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.Equal(0, reader.Calls); await app.DisposeAsync();
    });
    private sealed class Focus
    {
        public ReadingForeground Value = new((nint)42, Environment.ProcessId + 1, 1, "editor");
    }
    private sealed class Hook : IKeyboardHook
    {
        public event EventHandler<KeyEventArgs>? KeyEvent;
        public KeyEventArgs Emit() { var args = new KeyEventArgs(new() { Kind = KeyKind.Shortcut, Character = '\0',
            VirtualKey = 0x54, Modifiers = KeyModifiers.Alt | KeyModifiers.Shift, IsKeyDown = true }); KeyEvent?.Invoke(this, args); return args; }
        public void Start() { } public void Stop() { } public void Dispose() { }
    }
    private sealed class Reader : IReadingSelectionReader
    {
        public int Calls; public Lease Lease = new();
        public Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token, int? initiatingVirtualKey = null)
        { Calls++; Assert.Equal(0x54, initiatingVirtualKey); return Task.FromResult<IReadingSelectionLease?>(Lease); }
    }
    private sealed class Lease : IReadingSelectionLease
    {
        public ReadingSelection Selection { get; set; } = new(Guid.NewGuid(), "Hello world!", Environment.ProcessId + 1, "field", 1, 0, 12,
            false, true, InputCompositionState.Clear, 1, ScreenRect.Empty);
        public bool Retired; public event Action? Invalidated { add { } remove { } }
        public Task<bool> IsCurrentAsync(CancellationToken token) => Task.FromResult(!Retired);
        public void Retire() => Retired = true; public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Dictionary : ILearningDictionaryProviding
    {
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();
    }
}
