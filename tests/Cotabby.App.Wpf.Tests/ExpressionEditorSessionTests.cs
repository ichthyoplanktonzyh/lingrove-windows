using System.Runtime.Versioning;
using System.Windows.Automation;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.Insertion;
using Cotabby.Win32.ExpressionAssist;
using Cotabby.Win32.Focus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cotabby.App.Wpf.Tests;

[SupportedOSPlatform("windows8.1")]
public sealed class ExpressionEditorSessionTests
{
    [Fact]
    public async Task TriggerCancelDeletesOnlyExactMarkerOnce()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(fixture.Session.BeginReturn());
        Assert.Equal(ExpressionEditOutcome.Verified, await fixture.Session.ResumeAndReplaceAsync("", CancellationToken.None));
        Assert.Equal("abc", fixture.Effects.Value);
        Assert.Equal(2, fixture.Effects.LastBackspaces);
        Assert.Equal(1, fixture.Effects.Dispatched);
    }

    [Fact]
    public async Task ChangedHostValueWhilePanelOwnsInputRetiresReceipt()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel());
        fixture.Effects.Value = "xyz??";
        Assert.False(await fixture.Session.IsUnchangedAsync(CancellationToken.None));
        fixture.Effects.Value = "abc??";
        Assert.False(fixture.Session.BeginReturn());
        Assert.Equal(0, fixture.Effects.Dispatched);
    }

    [Fact]
    public async Task ExactReturnDispatchesOnceAndVerifiesExpectedValue()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(await fixture.Session.IsUnchangedAsync(CancellationToken.None));
        Assert.True(fixture.Session.BeginReturn());
        Assert.Equal(ExpressionEditOutcome.Verified, await fixture.Session.ResumeAndReplaceAsync("X", CancellationToken.None));
        Assert.Equal("abcX", fixture.Effects.Value);
        Assert.Equal(1, fixture.Effects.Dispatched);
        Assert.Equal(ExpressionEditOutcome.Rejected, await fixture.Session.ResumeAndReplaceAsync("again", CancellationToken.None));
    }

    [Fact]
    public async Task SameProcessDifferentWindowBeforeDispatchRejectsWithoutInput()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(fixture.Session.BeginReturn());
        fixture.Effects.BeforeSend = () => fixture.Effects.Foreground = 30;
        Assert.Equal(ExpressionEditOutcome.Unverified, await fixture.Session.ResumeAndReplaceAsync("X", CancellationToken.None));
        Assert.Equal(0, fixture.Effects.Dispatched);
        Assert.Equal("abc??", fixture.Effects.Value);
    }

    [Fact]
    public async Task PartialDispatchIsUnverifiedAndCannotRetry()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(fixture.Session.BeginReturn());
        fixture.Effects.Partial = true;
        Assert.Equal(ExpressionEditOutcome.Unverified, await fixture.Session.ResumeAndReplaceAsync("X", CancellationToken.None));
        Assert.Equal(1, fixture.Effects.Dispatched);
        Assert.Equal(ExpressionEditOutcome.Rejected, await fixture.Session.ResumeAndReplaceAsync("X", CancellationToken.None));
        Assert.Equal(1, fixture.Effects.Dispatched);
    }

    [Fact]
    public async Task ClipboardLeaseLosingOwnershipRejectsAndDisposesBeforeInput()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(fixture.Session.BeginReturn());
        fixture.Effects.Lease.Current = false;
        Assert.Equal(ExpressionEditOutcome.Rejected, await fixture.Session.ResumeAndReplaceAsync(new string('x', 25), CancellationToken.None));
        Assert.True(fixture.Effects.Lease.Disposed);
        Assert.Equal(0, fixture.Effects.Dispatched);
    }

    [Fact]
    public async Task ClipboardCleanupFailureIsUnverifiedAfterOneDispatch()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(fixture.Session.BeginReturn());
        fixture.Effects.Lease.FailDispose = true;
        Assert.Equal(ExpressionEditOutcome.Unverified, await fixture.Session.ResumeAndReplaceAsync(new string('x', 25), CancellationToken.None));
        Assert.Equal(1, fixture.Effects.Dispatched);
        Assert.Equal(ExpressionEditOutcome.Rejected, await fixture.Session.ResumeAndReplaceAsync("X", CancellationToken.None));
    }

    [Fact]
    public async Task PhysicalInputDuringBatchPreparationRevokesEvenWithSameWindow()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(fixture.Session.BeginReturn());
        fixture.Effects.BeforeSend = fixture.Keyboard.RaiseKey;
        Assert.Equal(ExpressionEditOutcome.Unverified, await fixture.Session.ResumeAndReplaceAsync("X", CancellationToken.None));
        Assert.Equal(0, fixture.Effects.Dispatched);
    }

    [Fact]
    public async Task UnicodeSelectionIsReplacedWithoutCountingItsUtf16UnitsAsBackspaces()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture("a😀b", new(1, 2), null);
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(fixture.Session.BeginReturn());
        Assert.Equal(ExpressionEditOutcome.Verified, await fixture.Session.ResumeAndReplaceAsync("日", CancellationToken.None));
        Assert.Equal("a日b", fixture.Effects.Value);
        Assert.Equal(0, fixture.Effects.LastBackspaces);
    }

    [Fact]
    public async Task CancellationWhileWaitingForSharedGateNeverDispatches()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherOwner = fixture.Gate.RunAsync(async _ => { entered.SetResult(); await release.Task; return 0; }, CancellationToken.None);
        await entered.Task;
        try
        {
            Assert.True(fixture.Session.EnterPanel());
            Assert.True(fixture.Session.BeginReturn());
            using var cancel = new CancellationTokenSource();
            var result = fixture.Session.ResumeAndReplaceAsync("X", cancel.Token);
            await Task.Delay(120);
            cancel.Cancel();
            Assert.Equal(ExpressionEditOutcome.Rejected, await result);
            Assert.Equal(0, fixture.Effects.Dispatched);
        }
        finally { release.TrySetResult(); await otherOwner; }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly FakeKeyboard Keyboard = new();
        public readonly FakeRetained Retained = new();
        public readonly FakeEffects Effects = new();
        public readonly InsertionTransactionGate Gate = new();
        public readonly UiaFocusTracker Focus;
        public readonly ExpressionEditorSession Session;
        public Fixture(string value = "abc??", ExpressionEditorRange? selection = null, string? trigger = "??")
        {
            Effects.Value = value; Effects.Selection = selection ?? new(value.Length, 0);
            Focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance, new FakeFocusOwner(), _ => { }, _ => { },
                _ => Field(Effects.Value, Effects.Selection, 2));
            Focus.Start();
            var initial = new ExpressionEditorObservation(42, "field", 1, 1, value, Effects.Selection, false, true, InputCompositionState.Clear, 1);
            var receipt = ExpressionEditorReceiptPolicy.Capture(initial, trigger)!;
            Session = new ExpressionEditorSession(Focus, Keyboard, Retained, Gate, Effects, 20, () => true, null);
            Session.SetCapturedForTest(receipt, 10, 1);
        }
        public async ValueTask DisposeAsync() { await Session.DisposeAsync(); Focus.Dispose(); }
        private static FocusedField Field(string value, ExpressionEditorRange selection, long epoch) => new()
        {
            ProcessId = 42, ProcessName = "test", Text = value.Remove(selection.Start, selection.Length), CaretOffset = selection.Start,
            CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty, IsSingleLine = false, IsSecure = false,
            HasSelection = selection.Length > 0, HasReliableSelection = true, IsTextWindowTruncated = false, FieldId = "field", FocusChangeSequence = epoch,
            Composition = InputCompositionState.Clear, CompositionSequence = 1,
        };
    }

    private sealed class FakeEffects : IExpressionSessionEffects
    {
        public nint Foreground { get; set; } = 20;
        public string Value = "abc??";
        public ExpressionEditorRange Selection = new(5, 0);
        public int Dispatched;
        public int LastBackspaces;
        public bool Partial;
        public Action? BeforeSend;
        public readonly FakeLease Lease = new();
        public bool Activate(nint host) { Foreground = host; return true; }
        public UiaExpressionSelectionReader.ValueSelection? Read(AutomationElement? element, Func<bool> allowed) =>
            allowed() ? new(Value, Selection) : null;
        public Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct) => Task.FromResult<IClipboardPasteLease?>(Lease);
        public bool SendUnicode(FocusedField field, int backspaces, string text, Func<bool> authorized)
        {
            BeforeSend?.Invoke();
            if (!authorized()) return false;
            Dispatched++;
            LastBackspaces = backspaces;
            int start = Selection.Start - backspaces;
            Value = Value.Remove(start, Selection.Length + backspaces).Insert(start, text);
            Selection = new(start + text.Length, 0);
            return !Partial;
        }
        public bool SendPaste(FocusedField field, int backspaces, Func<bool> authorized) => SendUnicode(field, backspaces, new string('x', 25), authorized);
    }
    private sealed class FakeLease : IClipboardPasteLease
    {
        public bool Current = true;
        public bool Disposed;
        public bool FailDispose;
        public bool IsCurrent => Current;
        public ValueTask DisposeAsync() { Disposed = true; return FailDispose ? ValueTask.FromException(new InvalidOperationException()) : ValueTask.CompletedTask; }
    }
    private sealed class FakeRetained : IExpressionRetainedEvidence
    {
        private bool _active = true;
        public long? Register(int processId, string fieldId, nint hostWindow, nint panelWindow, Action changed, CancellationToken ct) => 1;
        public (InputCompositionState State, long Version) Observe(long token, bool panelOwnsKeyboard, CancellationToken ct) =>
            _active ? (InputCompositionState.Clear, 1) : default;
        public bool IsCurrent(long token, long version) => _active && token == 1 && version == 1;
        public void Retire(long token) => _active = false;
        public Task DrainAsync() => Task.CompletedTask;
    }
    private sealed class FakeFocusOwner : ICompositionEvidenceOwner
    {
        public long Version => 1;
        public bool Execute(Action action) { action(); return true; }
        public (InputCompositionState State, long Version) Observe(int pid, string field, long focusEpoch) => default;
        public long Invalidate() => 1;
        public void RetireAsync(long expectedEpoch) { }
        public void Dispose() { }
    }
    private sealed class FakeKeyboard : IKeyboardHook
    {
        public event EventHandler<KeyEventArgs>? KeyEvent;
        public void RaiseKey() => KeyEvent?.Invoke(this, new(new KeyboardEvent { Kind = KeyKind.Character, Character = 'x', IsKeyDown = true, VirtualKey = 88 }));
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
