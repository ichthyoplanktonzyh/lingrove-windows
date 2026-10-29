using System.Runtime.Versioning;
using System.Windows.Automation;
using Cotabby.Core.Focus;
using Cotabby.Win32.Focus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cotabby.App.Wpf.Tests;

[SupportedOSPlatform("windows8.1")]
public sealed class UiaFocusCompositionTests
{
    [Fact]
    public async Task DelayedOldStopCannotRetireNewStartReceipt()
    {
        using var owner = new DelayedRetireOwner();
        using var tracker = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance, owner, _ => { }, _ => { },
            sequence => Field(sequence, InputCompositionState.Clear, owner.Version));
        tracker.Start();
        Task stopping = Task.Run(tracker.Stop);
        Assert.True(owner.Entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            owner.Change(); // The restarted producer installs a later subscription epoch.
            tracker.Start();
            long current = owner.Version;
            Assert.Equal(current, tracker.Current?.CompositionSequence);
            owner.Release.Set();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(current, owner.Version);
            Assert.Equal(current, tracker.Current?.CompositionSequence);
        }
        finally { owner.Release.Set(); }
    }

    [Fact]
    public async Task FailedPollDoesNotTriggerItselfAndRealEventCanRecover()
    {
        UiaFocusTracker? tracker = null;
        using var owner = new UiaTextEditEvidence(() => tracker!.OnCompositionChanged(), () => tracker!.OnCompositionFailure());
        using var release = new ManualResetEventSlim();
        bool query = false;
        int captures = 0;
        tracker = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance, owner, _ => { }, _ => { }, sequence =>
        {
            Interlocked.Increment(ref captures);
            if (Volatile.Read(ref query)) { owner.Observe(123, "test-only", sequence); return null; }
            return Field(sequence, InputCompositionState.Clear, owner.Version);
        });
        using (tracker)
        {
            tracker.Start();
            Assert.False(owner.Execute(() => release.Wait()));
            try
            {
                Volatile.Write(ref query, true);
                owner.Invalidate();
                tracker.OnCompositionChanged();
                await Task.Delay(1600);
                Assert.Equal(2, Volatile.Read(ref captures)); // initial capture and one failed poll
            }
            finally { release.Set(); }
            Volatile.Write(ref query, false);
            var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tracker.FocusChanged += (_, field) => { if (field?.Composition == InputCompositionState.Clear) recovered.TrySetResult(); };
            owner.Invalidate();
            tracker.OnCompositionChanged();
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(CompositionEvidencePolicy.IsClear(tracker.Current));
        }
    }

    [Fact]
    public async Task FinalizationRepopulatesCurrentWithoutAnotherKeyOrFocusEvent()
    {
        using var owner = new FakeOwner();
        InputCompositionState state = InputCompositionState.Composing;
        using var tracker = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance, owner, _ => { }, _ => { },
            sequence => Field(sequence, state, owner.Version));
        var clear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.FocusChanged += (_, field) => { if (field?.Composition == InputCompositionState.Clear) clear.TrySetResult(); };
        tracker.Start();
        Assert.Equal(InputCompositionState.Composing, tracker.Current?.Composition);
        state = InputCompositionState.Clear;
        owner.Change();
        tracker.OnCompositionChanged();
        await clear.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(InputCompositionState.Clear, tracker.Current?.Composition);
        Assert.True(CompositionEvidencePolicy.IsClear(tracker.Current));
    }

    [Fact]
    public async Task TimedOutOldStartCannotStopOrRegisterOverRestart()
    {
        using var owner = new DelayedFirstOwner();
        var active = new List<AutomationFocusChangedEventHandler>();
        using var tracker = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance, owner, handler =>
        {
            active.Add(handler);
        }, handler => active.RemoveAll(item => ReferenceEquals(item, handler)),
            sequence => Field(sequence, InputCompositionState.Clear, owner.Version));
        Task first = Task.Run(tracker.Start);
        try
        {
            Assert.True(owner.Entered.Wait(TimeSpan.FromSeconds(5)));
            await Task.Run(tracker.Stop);
            await Task.Run(tracker.Start);
        }
        finally { owner.Release.Set(); }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(tracker.Current);
        Assert.Single(active);
        tracker.Stop();
        Assert.Empty(active);
    }

    private static FocusedField Field(long sequence, InputCompositionState state, long version) => new()
    {
        ProcessId = 42, ProcessName = "owned-test", Text = "hello", CaretOffset = 5,
        CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty, IsSingleLine = true,
        IsSecure = false, FieldId = "test", FocusChangeSequence = sequence,
        HasReliableSelection = true, IsTextWindowTruncated = false,
        Composition = state, CompositionSequence = version,
    };

    private class FakeOwner : ICompositionEvidenceOwner
    {
        private long _version = 1;
        public long Version => Interlocked.Read(ref _version);
        public void Change() => Interlocked.Increment(ref _version);
        public virtual bool Execute(Action action) { action(); return true; }
        public (InputCompositionState State, long Version) Observe(int pid, string field, long focusEpoch) => default;
        public virtual long Invalidate() { Change(); return Version; }
        public virtual void RetireAsync(long expectedEpoch) { }
        public virtual void Dispose() { }
    }

    private sealed class DelayedFirstOwner : FakeOwner
    {
        private int _calls;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public override bool Execute(Action action)
        {
            if (Interlocked.Increment(ref _calls) != 1) return base.Execute(action);
            Entered.Set();
            Release.Wait();
            action();
            return false;
        }
        public override void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }

    private sealed class DelayedRetireOwner : FakeOwner
    {
        private int _calls;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public override void RetireAsync(long expectedEpoch)
        {
            if (Interlocked.Increment(ref _calls) == 1) { Entered.Set(); Release.Wait(); }
            if (expectedEpoch == Version) Change();
        }
        public override void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
}
