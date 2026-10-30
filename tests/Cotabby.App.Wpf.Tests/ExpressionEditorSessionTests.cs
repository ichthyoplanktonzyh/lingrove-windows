using System.Runtime.Versioning;
using System.Windows.Automation;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.ExpressionAssist;
using Cotabby.Win32.Focus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cotabby.App.Wpf.Tests;

[SupportedOSPlatform("windows8.1")]
public sealed class ExpressionEditorSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnicodeCommitKeepsSecureAndCredentialFieldAdmissionAtDispatch(bool credentialLabel)
    {
        await using var fixture = new Fixture("abc？？", new(5, 0), "？？");
        fixture.Effects.BeforeSend = () =>
        {
            fixture.Sensitive = !credentialLabel; fixture.CredentialLabel = credentialLabel;
            var changed = fixture.Focus.RefreshGuarded(() => true); Assert.NotNull(changed);
            Assert.True(credentialLabel ? CredentialFieldDetector.IsCredentialField(changed) : changed.IsSecure);
        };
        Assert.True(fixture.Session.EnterPanel()); Assert.True(fixture.Session.BeginReturn());
        Assert.Equal(ExpressionEditOutcome.Unverified, await fixture.Session.ResumeAndReplaceAsync("X", default));
        Assert.Equal(0, fixture.Effects.Dispatched); Assert.Equal("abc？？", fixture.Effects.Value);
    }
    [Theory]
    [InlineData("？？")]
    [InlineData("😀😀")]
    public async Task NativeEntryAcceptsConfiguredUnicodeBeforeTheUnavailableTargetBoundary(string trigger)
    {
        await using var fixture = new Fixture(); int reads = fixture.FocusReads;
        await using var editor = new Win32ExpressionEditor(fixture.Focus, fixture.Keyboard, fixture.Gate);
        Assert.Null(await editor.CaptureAsync(trigger, 0, () => false, null, default));
        Assert.Equal(reads, fixture.FocusReads);
    }

    [Fact]
    public async Task RetirementJoinsAnAcceptedRangeCommitAndCannotPublishItsLateSuccess()
    {
        await using var fixture = new Fixture("abc？？", new(5, 0), "？？");
        fixture.Effects.RangeHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(fixture.Session.EnterPanel()); Assert.True(fixture.Session.BeginReturn());
        var edit = fixture.Session.ResumeAndReplaceAsync("X", default);
        await fixture.Effects.RangeEntered.Task;
        var disposal = fixture.Session.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
        fixture.Effects.RangeHeld.SetResult(ExpressionEditOutcome.Verified);
        Assert.Equal(ExpressionEditOutcome.Unverified, await edit); await disposal;
    }

    [Fact]
    public async Task UnicodePasteLosingOwnershipCannotSendOrFallBackAndStillRestoresTheLease()
    {
        await using var fixture = new Fixture("abc？？", new(5, 0), "？？");
        fixture.Effects.BeforeSend = () => fixture.Effects.Lease.Current = false;
        Assert.True(fixture.Session.EnterPanel()); Assert.True(fixture.Session.BeginReturn());
        Assert.Equal(ExpressionEditOutcome.Unverified, await fixture.Session.ResumeAndReplaceAsync(new string('x', 25), default));
        Assert.Equal(1, fixture.Effects.RangeCalls); Assert.Equal(0, fixture.Effects.Dispatched);
        Assert.True(fixture.Effects.Lease.Disposed); Assert.Equal("abc？？", fixture.Effects.Value);
    }
    [Theory]
    [InlineData("？？", "X")]
    [InlineData("？？", "")]
    [InlineData("😀😀", "日")]
    public async Task UnicodeTriggerTransfersToOneExactRangeCommit(string trigger, string replacement)
    {
        await using var fixture = new Fixture("before" + trigger + "after", new(6 + trigger.Length, 0), trigger);
        Assert.True(fixture.Session.EnterPanel()); Assert.True(fixture.Session.BeginReturn());
        Assert.Equal(ExpressionEditOutcome.Verified, await fixture.Session.ResumeAndReplaceAsync(replacement, default));
        Assert.Equal("before" + replacement + "after", fixture.Effects.Value);
        Assert.Equal(new LearningTextRange(6, trigger.Length), fixture.Effects.LastRange);
        Assert.Equal(1, fixture.Effects.RangeCalls); Assert.Equal(1, fixture.Effects.Dispatched);
        Assert.Equal(replacement.Length == 0 ? 1 : 0, fixture.Effects.LastBackspaces);
    }

    [Fact]
    public async Task UnicodeRangeRefusalOrRetirementNeverFallsBackToCaretDeletion()
    {
        await using var fixture = new Fixture("abc？？", new(5, 0), "？？");
        fixture.Effects.RejectRange = true;
        Assert.True(fixture.Session.EnterPanel()); Assert.True(fixture.Session.BeginReturn());
        Assert.Equal(ExpressionEditOutcome.Rejected, await fixture.Session.ResumeAndReplaceAsync("X", default));
        Assert.Equal(1, fixture.Effects.RangeCalls); Assert.Equal(0, fixture.Effects.Dispatched);
        Assert.Equal("abc？？", fixture.Effects.Value);
    }
    [Fact]
    public async Task InstructionPermitUsesDirectRetainedEvidenceBeforeNotification()
    {
        await using var fixture = new Fixture();
        Assert.False(fixture.Session.CanTransmitRequest);
        Assert.True(fixture.Session.EnterPanel()); Assert.True(fixture.Session.CanTransmitRequest);
        fixture.Retained.Retire(1); // No session notification is delivered.
        Assert.False(fixture.Session.CanTransmitRequest);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task InstructionPermitRejectsForegroundProcessAndReturnChanges(int change)
    {
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel()); Assert.True(fixture.Session.CanTransmitRequest);
        switch (change)
        {
            case 0: fixture.Effects.Foreground = 30; break;
            case 1: fixture.HostPid++; break;
            case 2: fixture.PanelPid++; break;
            default: Assert.True(fixture.Session.BeginReturn()); break;
        }
        Assert.False(fixture.Session.CanTransmitRequest);
    }

    [Fact]
    public async Task PanelPreflightRequiresLiveCapturedSessionAndOriginalForeground()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        fixture.Effects.Foreground = 10;
        Assert.True(fixture.Session.CanPresentPanel);
        fixture.Effects.Foreground = 30;
        Assert.False(fixture.Session.CanPresentPanel);
        fixture.Effects.Foreground = 10;
        fixture.Session.Retire();
        Assert.False(fixture.Session.CanPresentPanel);
    }

    [Fact]
    public async Task PanelPreflightRejectsAfterPanelEntry()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        Assert.True(fixture.Session.EnterPanel());
        fixture.Effects.Foreground = 10;
        Assert.False(fixture.Session.CanPresentPanel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CaptureCompletedBeforeRetirementIsNotPublished(int invalidation)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        await using var replacement = new Fixture();
        var editor = new Win32ExpressionEditor(fixture.Focus, fixture.Keyboard, fixture.Gate);
        editor.AttachSessionForTest(fixture.Session);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ExpressionEditorSession?> publication = editor.PublishCaptureAsync(fixture.Session, Task.FromResult(true),
            CancellationToken.None, () => { entered.SetResult(); return release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (invalidation == 0) await editor.DisposeAsync();
        else if (invalidation == 1) fixture.Session.Retire();
        else editor.AttachSessionForTest(replacement.Session);
        release.SetResult();
        Assert.Null(await publication.WaitAsync(TimeSpan.FromSeconds(5)));
        await editor.DisposeAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EditorDisposeFromActiveSessionNotificationDoesNotDeadlockAndExternalDisposeDrainsIt(bool externalStarts)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        var editor = new Win32ExpressionEditor(fixture.Focus, fixture.Keyboard, fixture.Gate);
        editor.AttachSessionForTest(fixture.Session);
        using var callbackReturned = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Session.Invalidated += (_, _) =>
        {
            editor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            callbackReturned.Set();
            release.Wait();
        };
        Task? external = externalStarts ? editor.DisposeAsync().AsTask() : null;
        if (!externalStarts) fixture.Session.Retire();
        try
        {
            Assert.True(callbackReturned.Wait(TimeSpan.FromSeconds(5)));
            external ??= editor.DisposeAsync().AsTask();
            Assert.False(external.IsCompleted);
            Task secondExternal = editor.DisposeAsync().AsTask();
            Assert.False(secondExternal.IsCompleted);
            release.Set();
            await Task.WhenAll(external, secondExternal).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task ConcurrentEditorDisposalsJoinTheSameHeldWork()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        var editor = new Win32ExpressionEditor(fixture.Focus, fixture.Keyboard, fixture.Gate);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Track(release.Task);
        Task first = editor.DisposeAsync().AsTask();
        Task second = editor.DisposeAsync().AsTask();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RetiredCaptureDoesNotQueryFocusedElementAfterHeldFocusReadReturns()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Effects.Foreground = 10;
        fixture.BeforeFocusRead = () => { entered.Set(); release.Wait(); };
        await using var capture = new ExpressionEditorSession(fixture.Focus, fixture.Keyboard, fixture.Retained,
            fixture.Gate, fixture.Effects, 20, () => true, null);
        Task<bool> work = Task.Run(() => capture.Capture("??", CancellationToken.None));
        capture.Track(work);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            capture.Retire();
            fixture.Effects.Foreground = 30;
            release.Set();
            Assert.False(await work.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, fixture.FocusReads);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task TimedOutPostDispatchVerificationDoesNotRefreshAfterRetirement()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(fixture.Session.BeginReturn());
        fixture.Effects.BeforeRead = () => { if (Interlocked.Increment(ref reads) == 4) { entered.Set(); release.Wait(); } };
        try
        {
            Task<ExpressionEditOutcome> pending = fixture.Session.ResumeAndReplaceAsync("X", CancellationToken.None);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(ExpressionEditOutcome.Unverified, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            int prior = fixture.FocusReads;
            fixture.Effects.Foreground = 30;
            release.Set();
            await fixture.Session.DisposeAsync();
            Assert.Equal(prior, fixture.FocusReads);
            Assert.Equal(1, fixture.Effects.Dispatched);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task TimedOutReadDoesNotRefreshAnotherFieldAfterUnblocking()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Assert.True(fixture.Session.EnterPanel());
        Assert.True(fixture.Session.BeginReturn());
        fixture.Effects.BeforeRead = () => { entered.Set(); release.Wait(); };
        try
        {
            Task<ExpressionEditOutcome> pending = fixture.Session.ResumeAndReplaceAsync("X", CancellationToken.None);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(ExpressionEditOutcome.Rejected, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            fixture.Effects.Foreground = 30;
            release.Set();
            await fixture.Session.DisposeAsync();
            Assert.Equal(2, fixture.FocusReads); // initial seed and the first returned-host read only
            Assert.Equal(0, fixture.Effects.Dispatched);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task DisposeDrainsHeldInvalidationNotification()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int callbacks = 0;
        fixture.Session.Invalidated += (_, _) => { entered.Set(); release.Wait(); Interlocked.Increment(ref callbacks); };
        fixture.Session.Retire();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task disposing = fixture.Session.DisposeAsync().AsTask();
        try
        {
            Assert.False(disposing.IsCompleted);
            release.Set();
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, callbacks);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task InvalidationHandlerMaySynchronouslyDisposeWithoutSelfDeadlock()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        await using var fixture = new Fixture();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Session.Invalidated += (_, _) =>
        {
            fixture.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            done.TrySetResult();
        };
        fixture.Session.Retire();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

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
        public int FocusReads;
        public int HostPid = 42, PanelPid = Environment.ProcessId;
        public bool Sensitive, CredentialLabel;
        public Action? BeforeFocusRead;
        public Fixture(string value = "abc??", ExpressionEditorRange? selection = null, string? trigger = "??")
        {
            Effects.Value = value; Effects.Selection = selection ?? new(value.Length, 0);
            Focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance, new FakeFocusOwner(), _ => { }, _ => { },
                _ => { Interlocked.Increment(ref FocusReads); BeforeFocusRead?.Invoke(); return Field(Effects.Value, Effects.Selection, 2)
                    with { IsSecure = Sensitive, IsSingleLine = CredentialLabel, Label = CredentialLabel ? "password" : "" }; });
            Focus.Start();
            var initial = new ExpressionEditorObservation(42, "field", 1, 1, value, Effects.Selection, false, true, InputCompositionState.Clear, 1);
            var receipt = ExpressionEditorReceiptPolicy.Capture(initial, trigger)!;
            Session = new ExpressionEditorSession(Focus, Keyboard, Retained, Gate, Effects, 20, () => true, null)
            { WindowProcessForTest = window => window == 10 ? HostPid : window == 20 ? PanelPid : 0 };
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
        public bool RejectRange;
        public int RangeCalls;
        public LearningTextRange LastRange;
        public TaskCompletionSource<ExpressionEditOutcome>? RangeHeld;
        public readonly TaskCompletionSource RangeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? BeforeSend;
        public Action? BeforeRead;
        public readonly FakeLease Lease = new();
        public bool Activate(nint host) { Foreground = host; return true; }
        public UiaExpressionSelectionReader.ValueSelection? Read(AutomationElement? element, long token, Func<bool> allowed)
        {
            BeforeRead?.Invoke();
            return allowed() ? new(Value, Selection) : null;
        }
        public Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct) => Task.FromResult<IClipboardPasteLease?>(Lease);
        public bool SendUnicode(FocusedField field, int backspaces, string text, Func<bool> authorized)
        {
            BeforeSend?.Invoke();
            if (!authorized()) return false;
            Dispatched++;
            LastBackspaces = backspaces;
            int deletedBefore = Selection.Length > 0 && backspaces > 0 ? backspaces - 1 : backspaces;
            int start = Selection.Start - deletedBefore;
            Value = Value.Remove(start, Selection.Length + deletedBefore).Insert(start, text);
            Selection = new(start + text.Length, 0);
            return !Partial;
        }
        public bool SendPaste(FocusedField field, int backspaces, Func<bool> authorized) => SendUnicode(field, backspaces, new string('x', 25), authorized);
        public Task<ExpressionEditOutcome> ReplaceRangeAsync(long token, long version, FocusedField field, LearningTextRange range,
            string source, string expected, Func<bool> permit, Func<Func<bool>, bool> dispatch)
        {
            RangeCalls++; LastRange = range;
            RangeEntered.TrySetResult();
            if (RangeHeld is not null) return RangeHeld.Task;
            if (RejectRange || !permit()) return Task.FromResult(ExpressionEditOutcome.Rejected);
            Assert.Equal(source, Value.Substring(range.Location, range.Length)); Selection = new(range.Location, range.Length);
            bool sent = dispatch(permit);
            return Task.FromResult(sent && Value == expected ? ExpressionEditOutcome.Verified : ExpressionEditOutcome.Unverified);
        }
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
        public NativeTextRangeReader.ValueSelection? ReadRetained(long token, Func<bool> allowed) => null;
        private bool _active = true;
        public long? Register(int processId, string fieldId, nint hostWindow, nint panelWindow, Action changed, Func<bool> allowed, CancellationToken ct) => allowed() ? 1 : null;
        public (InputCompositionState State, long Version) Observe(long token, Func<bool> panelOwnsKeyboard, CancellationToken ct) =>
            _active ? (InputCompositionState.Clear, 1) : default;
        public bool IsCurrent(long token, long version) => _active && token == 1 && version == 1;
        public void Retire(long token) => _active = false;
        public Task DrainAsync() => Task.CompletedTask;
    }
    private sealed class FakeFocusOwner : ICompositionEvidenceOwner
    {
        public NativeTextRangeReader.Window? ReadFocused(int pid, string field, Func<bool> allowed) => null;
        public long Version => 1;
        public bool Execute(Action action) { action(); return true; }
        public (InputCompositionState State, long Version) Observe(int pid, string field, long focusEpoch, Func<bool>? allowed = null) => default;
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
