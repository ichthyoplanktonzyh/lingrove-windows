using System.Windows.Threading;
using Cotabby.App.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Core.Runtime.Metrics;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingSessionTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task BackgroundInvalidationCancelsBeforeBlockedDispatcherCanStartSend() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel(); int sends = 0;
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            async (_, _, _, _, token) => { entered.SetResult(token); await release.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); Interlocked.Increment(ref sends); return "sent"; },
            (_, _, _, _, _, _) => Task.CompletedTask);
        session.Begin(Settings); var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task.Run(() => reader.Lease.Invalidate()).GetAwaiter().GetResult();
        Assert.True(token.IsCancellationRequested); release.SetResult();
        await Until(() => !session.IsActive); Assert.Equal(0, sends);
    });

    [Fact]
    public Task ExplicitEnrichmentFinishesBeforeProviderAndRetirementPreventsSend() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel(); int sends = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Lease.Enrich = async token => { entered.SetResult(); await release.Task; token.ThrowIfCancellationRequested(); };
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (_, _, _, _, _) => { sends++; return Task.FromResult("sent"); }, (_, _, _, _, _, _) => Task.CompletedTask);
        try
        {
            session.Begin(Settings); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(panel.Shown); Assert.Equal(0, sends);
            reader.Lease.Retire(); reader.Lease.Invalidate(); release.SetResult();
            await Until(() => !session.IsActive); Assert.Equal(0, sends);
        }
        finally { release.TrySetResult(); }
    });

    [Fact]
    public Task RetiredLeaseWithoutNotificationWithdrawsProviderAdmission() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int sends = 0;
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            async (selection, _, _, _, _) => { entered.SetResult(); await release.Task.ConfigureAwait(false);
                if (selection.Admission?.Invoke() == true) Interlocked.Increment(ref sends); return "result"; },
            (_, _, _, _, _, _) => Task.CompletedTask);
        try
        {
            session.Begin(Settings); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            reader.Lease.Retire(); release.SetResult(); await Task.Delay(50);
            Assert.Equal(0, Volatile.Read(ref sends));
        }
        finally { release.TrySetResult(); }
    });

    [Fact]
    public Task LateOldInvalidationDoesNotCancelNewSelection() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel();
        var tokens = new List<CancellationToken>(); var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (_, _, _, _, token) => { tokens.Add(token); return pending.Task; }, (_, _, _, _, _, _) => Task.CompletedTask);
        try
        {
            session.Begin(Settings); await Until(() => tokens.Count == 1);
            var old = reader.Lease.SnapshotInvalidation(); session.Begin(Settings); await Until(() => tokens.Count == 2);
            await Task.Run(() => old!()); Assert.False(tokens[1].IsCancellationRequested);
            pending.SetResult("done"); await Until(() => panel.Last?.Status == ReadingStatus.Ready);
        }
        finally { pending.TrySetResult("done"); }
    });
    private static readonly ReadingSelection Selection = new(Guid.NewGuid(), "Hello world!", 123, "field", 1, 0, 12, false, true, InputCompositionState.Clear, 1, ScreenRect.Empty);
    private static readonly ReadingSettings Settings = new(true, true, true, LearningLanguage.Chinese,
        new("unused label", true, true, "https://example.test/v1"), new("Local lookup", false, true), 1);
    private static async Task Until(Func<bool> predicate)
    { for (int i = 0; i < 150 && !predicate(); i++) await Task.Delay(10); Assert.True(predicate()); }

    [Fact]
    public Task DestinationAppearsBeforeBorrowedProviderAndUnknownSourceStaysUnknown() => dispatcher.RunAsync(async () =>
    {
        var reader = new Reader(); var panel = new Panel(); LearningLanguage? observed = LearningLanguage.English;
        var metrics = new List<PerformanceRequestObservation>();
        await using var session = new ReadingSession(Dispatcher.CurrentDispatcher, reader, panel,
            _ => new(null, ReadingLanguageEvidence.Unavailable), (_, _) => 2,
            (capture, source, target, partial, token) =>
            {
                Assert.True(panel.Shown); Assert.Equal("Endpoint: https://example.test/v1", panel.Last!.Destination);
                observed = source; Assert.Equal(Selection.Text, capture.Text); return Task.FromResult("你好世界！");
            }, (_, _, _, _, _, _) => throw new InvalidOperationException(), metrics.Add);
        session.Begin(Settings with { TranslationModelName = "configured-model" }); await Until(() => panel.Last?.Status == ReadingStatus.Ready);
        Assert.Null(observed); Assert.Equal("你好世界！", panel.Last!.Text); Assert.True(panel.Last.Route.IsTranslation);
        var metric = Assert.Single(metrics); Assert.Equal(ModelRequestSource.Translation, metric.Source);
        Assert.Equal(ModelRequestOutcome.Completed, metric.Outcome); Assert.Equal("configured-model", metric.ModelName);
        Assert.DoesNotContain("Hello", metric.RequestId);
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
        public bool IsLive => !Retired;
        public ReadingSelection Value = ReadingSessionTests.Selection;
        public Func<CancellationToken, Task>? Enrich;
        public ReadingSelection Selection => Value; public bool Retired;
        public event Action? Invalidated;
        public void Invalidate() => Invalidated?.Invoke();
        public Action? SnapshotInvalidation() => Invalidated;
        public Task<bool> IsCurrentAsync(CancellationToken token) => Task.FromResult(!Retired);
        public Task EnrichAsync(CancellationToken token) => Enrich?.Invoke(token) ?? Task.CompletedTask;
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
