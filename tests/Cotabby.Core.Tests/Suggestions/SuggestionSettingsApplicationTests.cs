using Cotabby.Core.Suggestions;

namespace Cotabby.Core.Tests.Suggestions;

/// <summary>Exercises the same application seam used by AppHost with the real coordinator and a fake endpoint.</summary>
public sealed class SuggestionSettingsApplicationTests : CoordinatorHarness
{
    [Fact]
    public async Task InFlightEndpointSurvivesLocalOnlyAndEquivalentSettingsButEndpointEditCancels()
    {
        await using var local = new CompletionModelAdmission(Engine);
        var application = new SuggestionSettingsApplication(Coordinator, local);
        var initial = Coordinator.Settings with { UsesEndpoint = true };
        application.Apply(initial, ("https://one.example", "model", 0), "local-A");
        await RunOnUiAsync(() => { });
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.GenerateOverride = (_, ct) => { started.TrySetResult(ct); return finish.Task; };
        Type("hello"); var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var copied = initial with { BlockedApps = new HashSet<string>(initial.BlockedApps, StringComparer.OrdinalIgnoreCase),
            ShortcutOverrides = initial.ShortcutOverrides.ToArray(), SpellingDictionaries = initial.SpellingDictionaries.ToArray(), Languages = initial.Languages.ToArray() };
        var transition = application.Apply(copied, ("https://one.example", "model", 0), "local-B");
        Assert.False(transition.CoordinatorChanged); Assert.False(token.IsCancellationRequested);
        finish.SetResult(" world again"); await Shown(" world again");

        var nextStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextFinish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.GenerateOverride = (_, ct) => { nextStarted.TrySetResult(ct); return nextFinish.Task; };
        Hook.Press(Keys.Char('x')); Focus.Append("x");
        var nextToken = await nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        application.Apply(copied, ("https://two.example", "model", 0), "local-B");
        await RunOnUiAsync(() => { }); Assert.True(nextToken.IsCancellationRequested);
        nextFinish.TrySetResult(" stale"); await StaysHidden();
    }
    [Fact]
    public async Task EquivalentSettingsKeepLocalAdmissionButChangedSelectionWithdrawsIt()
    {
        await using var local = new CompletionModelAdmission(Engine);
        var application = new SuggestionSettingsApplication(Coordinator, local);
        var settings = Coordinator.Settings with { UsesEndpoint = false, Enabled = true };
        application.Apply(settings, "endpoint", "local-A");
        var ticket = local.Select("A", true);
        Assert.True(await local.PrepareAsync(ticket, _ => Task.CompletedTask, () => Task.CompletedTask, () => true, CancellationToken.None));
        Assert.False(application.Apply(settings, "endpoint", "local-A").CoordinatorChanged);
        Assert.True(local.IsReady);
        Assert.True(application.Apply(settings, "endpoint", "local-B").CoordinatorChanged);
        Assert.False(local.IsReady);
    }
}
