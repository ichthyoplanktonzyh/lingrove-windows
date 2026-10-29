using Cotabby.Core.Suggestions;
using Cotabby.Core.Models;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class CompletionModelAdmissionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly SuggestionRequest Request = new() { RequestId = "fixture", Prefix = "hello", Suffix = "", SingleLine = true, MaxTokens = 10 };
    private sealed class Engine : ISuggestionEngine
    {
        public bool IsReady { get; set; } = true;
        public bool SpacingIsExact => true;
        public Func<Task<string>> Run { get; set; } = () => Task.FromResult(" result");
        public Task<string> GenerateAsync(SuggestionRequest request, CancellationToken cancellationToken) => Run();
    }
    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<bool> Prepare(CompletionModelAdmission owner, CompletionModelAdmission.Ticket ticket, Func<Task>? load = null) =>
        owner.PrepareAsync(ticket, _ => Task.CompletedTask, load ?? (() => Task.CompletedTask), () => true, CancellationToken.None);

    [Fact]
    public async Task SharedColdPreparationSurvivesCancelledWaitAndRetiresOnSelectionChange()
    {
        await using var owner = new CompletionModelAdmission(new Engine()); var sharing = new CompletionPreparationSharing();
        var first = owner.Select("A", true); var entered = Signal<bool>(); var release = Signal<bool>(); int starts = 0;
        Task<bool> Start() { starts++; return Prepare(owner, first, async () => { entered.SetResult(true); await release.Task; }); }
        var shared = sharing.GetOrStart(first.Epoch, Start); await entered.Task.WaitAsync(Timeout);
        using var waiter = new CancellationTokenSource(); var cancelled = shared.WaitAsync(waiter.Token); waiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled); Assert.Same(shared, sharing.GetOrStart(first.Epoch, Start)); Assert.Equal(1, starts);
        var second = owner.Select("B", true); var newWork = sharing.GetOrStart(second.Epoch, () => Prepare(owner, second));
        release.SetResult(true); Assert.False(await shared.WaitAsync(Timeout)); Assert.True(await newWork.WaitAsync(Timeout)); Assert.True(owner.IsReady);
    }

    [Fact]
    public void BootstrapReconcilesCompatibleMissingChoicesButPreservesIncompatibleAndUnknownIds()
    {
        var first = ModelCatalog.All[0]; var other = ModelCatalog.All[1];
        Assert.Equal(first.Id, CompletionModelSelection.Reconcile(other.Id, [first]));
        Assert.Null(CompletionModelSelection.Reconcile(other.Id, []));
        Assert.Equal(first.Id, CompletionModelSelection.Reconcile(null, [first]));
        Assert.Equal("tabby-2-base", CompletionModelSelection.Reconcile(null, ModelCatalog.All));
        Assert.Equal("custom:TranslateGemma.gguf", CompletionModelSelection.Reconcile("custom:TranslateGemma.gguf", [first]));
        Assert.Equal("TABBY-2-MINI", CompletionModelSelection.Reconcile("TABBY-2-MINI", [first]));
        Assert.Equal("custom:NOT-FOUND.gguf", CompletionModelSelection.Reconcile("custom:NOT-FOUND.gguf",
            [new CotabbyModel { Id = "custom:not-found.gguf", FileName = "not-found.gguf", DisplayName = "Custom", DownloadUrl = "", ApproxSizeBytes = 1, ContextLength = 4096 }]));
    }

    [Fact]
    public void PowerTransitionsSelectInstalledProfilesWithoutOverwritingLaterManualChoices()
    {
        var ac = new CompletionModelSelection.PowerProfileState(true, false, "BATTERY", "AC");
        var battery = new CompletionModelSelection.PowerProfileState(true, true, "BATTERY", "AC");
        Assert.Equal("AC", CompletionModelSelection.ApplyPower(null, ac, "original", "AC"));
        Assert.Equal("manual", CompletionModelSelection.ApplyPower(ac, ac, "manual", "AC"));
        Assert.Equal("BATTERY", CompletionModelSelection.ApplyPower(ac, battery, "manual", "BATTERY"));
        Assert.Equal("manual", CompletionModelSelection.ApplyPower(ac, battery, "manual", null));
        Assert.Equal("manual", CompletionModelSelection.ApplyPower(battery, new(false, false, "", ""), "manual", null));
        Assert.Equal("AC", CompletionModelSelection.ApplyPower(ac, ac, "manual", "AC", discoveryChanged: true));
        Assert.Equal("AC", CompletionModelSelection.ApplyPower(ac, ac with { BatteryModelId = "NEW" }, "manual", "AC"));
        Assert.Equal("manual", CompletionModelSelection.ApplyPower(ac, ac with { BatteryModelId = "NEW" }, "manual", null));
        Assert.Equal("NEW", CompletionModelSelection.ApplyPower(ac, ac with { PluggedInModelId = "NEW" }, "manual", "NEW"));
    }

    [Fact]
    public async Task SelectionWithdrawalRejectsOldGenerationAndSerializesRuntimeSwap()
    {
        var engine = new Engine(); var started = Signal<bool>(); var finish = Signal<string>();
        engine.Run = () => { started.TrySetResult(true); return finish.Task; };
        await using var owner = new CompletionModelAdmission(engine);
        var a = owner.Select("A", true); Assert.True(await Prepare(owner, a));
        var generation = owner.GenerateAsync(Request, CancellationToken.None); await started.Task.WaitAsync(Timeout);
        var b = owner.Select("B", true); Assert.False(owner.IsReady);
        var loaded = Signal<bool>(); var loading = Prepare(owner, b, () => { loaded.TrySetResult(true); return Task.CompletedTask; });
        Assert.False(loaded.Task.IsCompleted);
        finish.SetResult(" old output"); Assert.Equal("", await generation.WaitAsync(Timeout));
        Assert.True(await loading.WaitAsync(Timeout)); Assert.True(loaded.Task.IsCompleted); Assert.True(owner.IsReady);
    }

    [Fact]
    public async Task PowerWithdrawalDuringResolutionCannotReauthorizeAnOldChoice()
    {
        await using var owner = new CompletionModelAdmission(new Engine());
        var before = owner.Epoch;
        owner.Withdraw();
        Assert.Null(owner.SelectIfEpoch(before, "old-power-model", true));
        Assert.Null(owner.Capture("old-power-model"));
        var current = owner.SelectIfEpoch(owner.Epoch, "new-power-model", true);
        Assert.NotNull(current); Assert.True(await Prepare(owner, current.Value));
        Assert.True(owner.IsReady);
        owner.Withdraw(); bool staleCommit = false;
        Assert.False(owner.CommitIfEpoch(current.Value.Epoch, () => staleCommit = true));
        Assert.False(staleCommit);
    }

    [Fact]
    public async Task AToBToARejectsObsoleteLoadEvenWhenModelIdentityMatchesAgain()
    {
        var engine = new Engine(); await using var owner = new CompletionModelAdmission(engine);
        var started = Signal<bool>(); var release = Signal<bool>();
        var a = owner.Select("A", true);
        var old = Prepare(owner, a, async () => { started.SetResult(true); await release.Task; });
        await started.Task.WaitAsync(Timeout);
        var b = owner.Select("B", true); var bCalls = 0;
        var skipped = Prepare(owner, b, () => { bCalls++; return Task.CompletedTask; });
        var aAgain = owner.Select("A", true); var freshCalls = 0;
        var fresh = Prepare(owner, aAgain, () => { freshCalls++; return Task.CompletedTask; });
        release.SetResult(true);
        Assert.False(await old.WaitAsync(Timeout)); Assert.False(await skipped.WaitAsync(Timeout));
        Assert.True(await fresh.WaitAsync(Timeout)); Assert.Equal(0, bCalls); Assert.Equal(1, freshCalls); Assert.True(owner.IsReady);
    }

    [Fact]
    public async Task ObsoleteFailureCannotPublishOverNewerReadyStatus()
    {
        await using var owner = new CompletionModelAdmission(new Engine());
        var gate = new CompletionStatusGate();
        var a = owner.Select("A", true); var oldStatus = gate.Begin(a.Epoch);
        var started = Signal<bool>(); var release = Signal<bool>();
        var old = Prepare(owner, a, async () => { started.SetResult(true); await release.Task; throw new IOException("obsolete failure"); });
        await started.Task.WaitAsync(Timeout);
        var b = owner.Select("B", true); var newStatus = gate.Begin(b.Epoch);
        string status = "loading B";
        var next = Prepare(owner, b);
        release.SetResult(true);
        if (await old.WaitAsync(Timeout) == false && gate.IsCurrent(oldStatus, owner.LocalSelectionEpoch)) status = "old failure";
        Assert.True(await next.WaitAsync(Timeout));
        if (gate.IsCurrent(newStatus, owner.LocalSelectionEpoch)) status = "ready B";
        Assert.Equal("ready B", status);
        Assert.False(gate.IsCurrent(oldStatus, owner.LocalSelectionEpoch));
        owner.Select(null, false);
        Assert.False(gate.IsCurrent(newStatus, owner.LocalSelectionEpoch));
        Assert.Null(owner.LocalSelectionEpoch);
    }

    [Fact]
    public void StatusSequenceRejectsEarlierOperationsWithinTheSameEpoch()
    {
        var gate = new CompletionStatusGate();
        var first = gate.Begin(42); var second = gate.Begin(42);
        Assert.False(gate.IsCurrent(first, 42)); Assert.True(gate.IsCurrent(second, 42));
        gate.Invalidate(); Assert.False(gate.IsCurrent(second, 42));
    }
    [Fact]
    public async Task FailedSwitchNeverExposesAnOldStillReadyEngine()
    {
        var engine = new Engine(); await using var owner = new CompletionModelAdmission(engine);
        Assert.True(await Prepare(owner, owner.Select("A", true))); Assert.True(owner.IsReady);
        var next = owner.Select("B", true);
        await Assert.ThrowsAsync<IOException>(() => Prepare(owner, next, () => Task.FromException(new IOException("fixture"))));
        Assert.True(engine.IsReady); Assert.False(owner.IsReady);
        Assert.Equal("", await owner.GenerateAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task ObsoleteDownloadCannotBeginLoadAndEndpointRemainsIndependent()
    {
        var engine = new Engine(); var endpoint = new Engine(); await using var owner = new CompletionModelAdmission(engine);
        var ticket = owner.Select("A", true); var started = Signal<bool>(); var finish = Signal<bool>(); var loads = 0;
        var work = owner.PrepareAsync(ticket, async _ => { started.SetResult(true); await finish.Task; }, () => { loads++; return Task.CompletedTask; }, () => true, CancellationToken.None);
        await started.Task.WaitAsync(Timeout); owner.Withdraw();
        var router = new SuggestionEngineRouter(owner, endpoint) { UseEndpoint = true };
        Assert.True(router.IsReady); Assert.Equal(" result", await router.GenerateAsync(Request, CancellationToken.None));
        finish.SetResult(true); Assert.False(await work.WaitAsync(Timeout)); Assert.Equal(0, loads); Assert.False(owner.IsReady);
    }

    [Fact]
    public async Task WithdrawalSignalsActiveGenerationAndSuppressesItsLateOutput()
    {
        var started = Signal<CancellationToken>(); var finish = Signal<string>();
        var tokenEngine = new CancellationAwareEngine(started, finish);
        await using var observed = new CompletionModelAdmission(tokenEngine); var current = observed.Select("A", true);
        Assert.True(await Prepare(observed, current));
        var work = observed.GenerateAsync(Request, CancellationToken.None); var token = await started.Task.WaitAsync(Timeout);
        var cancelled = Signal<bool>(); using var registration = token.Register(() => cancelled.TrySetResult(true));
        observed.Withdraw();
        Assert.False(observed.IsReady); await cancelled.Task.WaitAsync(Timeout);
        finish.SetResult(" late"); Assert.Equal("", await work.WaitAsync(Timeout));
    }

    private sealed class CancellationAwareEngine(TaskCompletionSource<CancellationToken> started, TaskCompletionSource<string> finish) : ISuggestionEngine
    {
        public bool IsReady => true;
        public bool SpacingIsExact => true;
        public Task<string> GenerateAsync(SuggestionRequest request, CancellationToken cancellationToken)
        { started.SetResult(cancellationToken); return finish.Task; }
    }

    [Fact]
    public async Task ShutdownClosesAdmissionAndJoinsAcceptedNativeWork()
    {
        var engine = new Engine(); var owner = new CompletionModelAdmission(engine); var ticket = owner.Select("A", true);
        var started = Signal<bool>(); var finish = Signal<bool>();
        var work = Prepare(owner, ticket, async () => { started.SetResult(true); await finish.Task; });
        await started.Task.WaitAsync(Timeout); var stopping = owner.DisposeAsync().AsTask();
        Assert.False(owner.IsReady); Assert.False(stopping.IsCompleted);
        Assert.False(await Prepare(owner, ticket));
        finish.SetResult(true); Assert.False(await work.WaitAsync(Timeout)); await stopping.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ShutdownJoinsCancellationCallbacksAsWellAsTheRequest()
    {
        var engine = new Engine(); var owner = new CompletionModelAdmission(engine); var ticket = owner.Select("A", true);
        var started = Signal<bool>(); var callbackStarted = Signal<bool>(); var callbackRelease = Signal<bool>(); var finish = Signal<bool>();
        var work = owner.PrepareAsync(ticket, async token =>
        {
            token.Register(() => { callbackStarted.TrySetResult(true); callbackRelease.Task.GetAwaiter().GetResult(); });
            started.TrySetResult(true); await finish.Task;
        }, () => Task.CompletedTask, () => true, CancellationToken.None);
        await started.Task.WaitAsync(Timeout);
        var stopping = owner.DisposeAsync().AsTask();
        try
        {
            await callbackStarted.Task.WaitAsync(Timeout); finish.TrySetResult(true);
            Assert.False(stopping.IsCompleted);
        }
        finally { callbackRelease.TrySetResult(true); finish.TrySetResult(true); }
        Assert.False(await work.WaitAsync(Timeout)); await stopping.WaitAsync(Timeout);
    }
}
