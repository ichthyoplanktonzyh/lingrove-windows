using System.Windows.Threading;
using Cotabby.App.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingSessionTests(WpfDispatcher dispatcher)
{
    private static readonly ReadingSelection Selection = new(Guid.NewGuid(), "Hello world!", 123, "field", 1, 0, 12, false, true, InputCompositionState.Clear, 1, ScreenRect.Empty);
    private static readonly ReadingSettings Settings = new(true, true, true, LearningLanguage.Chinese,
        new("unused label", true, true, "https://example.test/v1"), new("Local lookup", false, true), 1);
    private static async Task Until(Func<bool> predicate)
    { for (int i = 0; i < 150 && !predicate(); i++) await Task.Delay(10); Assert.True(predicate()); }

    [Fact]
    public Task DestinationAppearsBeforeBorrowedProviderAndUnknownSourceStaysUnknown() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel(); LearningLanguage? observed = LearningLanguage.English;
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (capture, source, target, partial, token) =>
            {
                Assert.True(panel.Shown); Assert.Equal("Endpoint: https://example.test/v1", panel.Last!.Destination);
                observed = source; Assert.Equal(Selection.Text, capture.Text); return Task.FromResult("你好世界！");
            }, (_, _, _, _, _, _) => throw new InvalidOperationException());
        session.Begin(Settings); await Until(() => panel.Last?.Status == ReadingStatus.Ready);
        Assert.Null(observed); Assert.Equal("你好世界！", panel.Last!.Text); Assert.True(panel.Last.Route.IsTranslation);
    });

    [Fact]
    public Task OversizedProviderPrefixCannotBecomeAReadingResult() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel();
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (_, _, _, partial, _) => { partial(new string('x', 12_001)); return Task.FromResult("apparently complete"); },
            (_, _, _, _, _, _) => throw new InvalidOperationException());
        session.Begin(Settings); await Until(() => panel.Last?.Status == ReadingStatus.Error);
        Assert.NotEqual("apparently complete", panel.Last!.Text);
    });

    [Fact]
    public Task SettingsChangeRetiresPendingWorkAndRejectsLateResult() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel(); var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (_, _, _, _, _) => { calls++; return pending.Task; }, (_, _, _, _, _, _) => throw new InvalidOperationException());
        try
        {
            session.Begin(Settings); await Until(() => calls == 1);
            session.SettingsChanged(Settings with { Revision = 2 });
            Assert.False(session.IsActive); pending.SetResult("late");
            await Task.Delay(30); Assert.NotEqual(ReadingStatus.Ready, panel.Last?.Status);
        }
        finally { pending.TrySetResult("late"); }
    });

    [Fact]
    public Task WordRoutesToBorrowedLookupOwnerWithoutInventingLanguage() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); reader.Lease.Value = Selection with { Text = "bank", SelectionLength = 4 };
        var panel = new Panel(); int lookups = 0; LearningLanguage? observed = LearningLanguage.English;
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 1,
            (_, _, _, _, _) => throw new InvalidOperationException(),
            (_, route, source, _, _, _) => { lookups++; observed = source; Assert.Equal(ReadingRouteReason.StandaloneUnit, route.Reason); return Task.CompletedTask; });
        session.Begin(Settings); await Until(() => lookups == 1);
        Assert.Null(observed); Assert.Equal(ReadingStatus.Lookup, panel.Last?.Status);
    });

    [Fact]
    public Task RevokedReceiptSuppressesUncooperativeLateResultAndShutdownJoins() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel();
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
        var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (_, _, _, _, _) => { calls++; return pending.Task; }, (_, _, _, _, _, _) => throw new InvalidOperationException());
        session.Begin(Settings); await Until(() => calls == 1);
        reader.Lease.Invalidate(); Assert.False(session.IsActive); Assert.False(panel.Shown);
        var disposal = session.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
        pending.SetResult("late"); await disposal;
        Assert.NotEqual(ReadingStatus.Ready, panel.Last?.Status); Assert.True(reader.Lease.Retired);
    });

    [Fact]
    public Task EndpointWithoutExplicitDestinationConsentNeverInvokesProvider() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel();
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (_, _, _, _, _) => throw new InvalidOperationException(), (_, _, _, _, _, _) => throw new InvalidOperationException());
        session.Begin(Settings with { Translation = new("Endpoint: example.test", true, false) });
        await Until(() => !session.IsActive);
        Assert.Equal(1, reader.Captures); Assert.False(panel.Shown); Assert.False(session.IsActive);
        session.Begin(Settings with { Translation = new("local", true, true, "https://user:secret@example.test/") });
        await Until(() => !session.IsActive);
        Assert.Equal(2, reader.Captures); Assert.False(panel.Shown); Assert.False(session.IsActive);
    });

    [Fact]
    public Task LatePartialsAndQueuedWorkAreRetiredAndDrained() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel(); Action<string>? partial = null;
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (_, _, _, callback, _) => { partial = callback; return pending.Task; }, (_, _, _, _, _, _) => Task.CompletedTask);
        session.Begin(Settings); await Until(() => partial is not null);
        Task.Run(() => partial!("queued")).GetAwaiter().GetResult();
        var oldInvalidation = reader.Lease.SnapshotInvalidation();
        var disposal = session.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
        pending.SetResult("late"); await disposal;
        var before = panel.Updates;
        await Task.Run(() => { partial!("after disposal"); oldInvalidation?.Invoke(); });
        await Task.Delay(30); Assert.Equal(before, panel.Updates); Assert.NotEqual("queued", panel.Last?.Text);
    });

    [Fact]
    public Task CompletionRetiresRetainedPartialCallback() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel(); Action<string>? partial = null;
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (_, _, _, callback, _) => { partial = callback; return Task.FromResult("finished"); }, (_, _, _, _, _, _) => Task.CompletedTask);
        session.Begin(Settings); await Until(() => panel.Last?.Status == ReadingStatus.Ready);
        var before = panel.Updates; await Task.Run(() => partial!("late")); await Task.Delay(30);
        Assert.Equal(before, panel.Updates); Assert.Equal("finished", panel.Last!.Text);
    });

    [Fact]
    public async Task RetainedCallbacksAreHarmlessAfterDispatcherShutdown()
    {
        Action<string>? partial = null; Action? invalidation = null;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var current = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(current));
            current.InvokeAsync(async () =>
            {
                try
                {
                    var reader = new Reader(); var panel = new Panel();
                    var session = new ReadingSession(current, reader, panel,
                        _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
                        (_, _, _, callback, _) => { partial = callback; return Task.FromResult("done"); }, (_, _, _, _, _, _) => Task.CompletedTask);
                    session.Begin(Settings); await Until(() => panel.Last?.Status == ReadingStatus.Ready);
                    // Leave the session active to exercise dispatcher-shutdown admission itself;
                    // the fake owns no external resources.
                    invalidation = reader.Lease.SnapshotInvalidation();
                }
                catch (Exception error) { finished.TrySetException(error); }
                finally { current.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run(); finished.TrySetResult();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await finished.Task; thread.Join();
        await Task.Run(() => { partial!("after shutdown"); invalidation?.Invoke(); });
    }

    private sealed class Reader : IReadingSelectionReader
    {
        public int Captures; public Lease Lease { get; private set; } = new();
        public Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token, int? initiatingVirtualKey = null)
        { if (Captures++ > 0) Lease = new(); return Task.FromResult<IReadingSelectionLease?>(Lease); }
    }
    private sealed class Lease : IReadingSelectionLease
    {
        public ReadingSelection Value = ReadingSessionTests.Selection;
        public ReadingSelection Selection => Value; public bool Retired;
        public event Action? Invalidated;
        public void Invalidate() => Invalidated?.Invoke();
        public Action? SnapshotInvalidation() => Invalidated;
        public Task<bool> IsCurrentAsync(CancellationToken token) => Task.FromResult(!Retired);
        public void Retire() => Retired = true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Panel : IReadingPresentation
    {
        public bool Shown; public ReadingState? Last; public int Updates;
        public Task ShowAsync(ReadingState state) { Shown = true; Last = state; return Task.CompletedTask; }
        public void Update(ReadingState state) { Updates++; Last = state; }
        public void ShowCaptureError(string message) { }
        public void Hide() => Shown = false;
    }
}
