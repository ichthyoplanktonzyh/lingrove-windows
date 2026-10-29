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
        public void Invalidate() => Change();
        public void RetireAsync() { }
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
}
