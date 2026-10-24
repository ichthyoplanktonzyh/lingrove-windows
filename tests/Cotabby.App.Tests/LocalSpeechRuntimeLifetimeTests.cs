using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;
using Cotabby.Tests.Speech;
namespace Cotabby.App.Tests;

public sealed class LocalSpeechRuntimeLifetimeTests
{
    [Fact]
    public async Task FailedReapingRetainsTheOldChildUntilRetryAndPreventsOverlappingModels()
    {
        RetryFactory? effects = null;
        await using var fixture = new SpeechRuntimeFixture(f => effects = new RetryFactory(f.Factory));
        Assert.Equal(9600, (await fixture.CollectAsync(fixture.Request("first"))).Length);
        await fixture.Runtime.ShutdownAsync();
        var error = await Assert.ThrowsAsync<AggregateException>(() => fixture.Runtime.DrainAsync());
        Assert.Contains(error.InnerExceptions, value => value is SpeechException { Failure: SpeechFailure.RuntimeUnavailable });
        Assert.Equal(1, effects!.Starts); Assert.False(fixture.Factory.Processes.Single().HasExited);
        Assert.Equal(9600, (await fixture.CollectAsync(fixture.Request("after"))).Length);
        Assert.Equal(2, effects.Starts); Assert.True(fixture.Factory.Processes.First().HasExited); Assert.False(effects.Overlapped);
    }

    [Fact]
    public async Task PressureIsLazyIdleOnlyAndBindingIsReleasedAtShutdown()
    {
        await using var fixture = new SpeechRuntimeFixture(); Assert.Equal(0, fixture.Pressure.Subscriptions);
        var request = fixture.Request("chunk-then-wait"); using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = fixture.Runtime.StreamAsync(request, fixture.Configuration, _ => { started.TrySetResult(); return Task.CompletedTask; }, cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, fixture.Pressure.Subscriptions);
            fixture.Pressure.Warn(); Assert.False(fixture.Factory.Processes.Single().HasExited);
            cancellation.Cancel(); await Assert.ThrowsAnyAsync<Exception>(() => job);
            await fixture.CollectAsync(fixture.Request("after")); var idle = fixture.Factory.Processes.Last(); fixture.Pressure.Warn();
            await SpeechRuntimeFixture.WaitAsync(() => idle.HasExited);
            await fixture.Runtime.ShutdownAsync(); await fixture.Runtime.DrainAsync(); Assert.Equal(1, fixture.Pressure.Disposals);
        }
        finally { cancellation.Cancel(); try { await job; } catch (Exception) { } }
    }

    [Theory]
    [InlineData("wrong-id")]
    [InlineData("wrong-total")]
    [InlineData("not-ok")]
    [InlineData("nan")]
    [InlineData("bad-rate")]
    [InlineData("exit")]
    public async Task InvalidProtocolFailsThenUsesAFreshSession(string text)
    {
        await using var fixture = new SpeechRuntimeFixture();
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.CollectAsync(fixture.Request(text)));
        Assert.Equal(9600, (await fixture.CollectAsync(fixture.Request("after"))).Length); Assert.Equal(2, fixture.Lines("starts").Length);
    }

    [Fact]
    public async Task DrainWaitsForAnOldBlockedReceiverAfterNativeRetirementAllowsTheNextSession()
    {
        await using var fixture = new SpeechRuntimeFixture(); var gate = new SpeechTestGate();
        var request = fixture.Request("first"); var job = fixture.Runtime.StreamAsync(request, fixture.Configuration, _ => gate.WaitAsync());
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await fixture.Runtime.CancelAsync(request.Token);
            await Assert.ThrowsAnyAsync<Exception>(() => job); Assert.True(fixture.Factory.Processes.First().HasExited);
            Assert.Equal(9600, (await fixture.CollectAsync(fixture.Request("after"))).Length);
            await fixture.Runtime.ShutdownAsync(); var drain = fixture.Runtime.DrainAsync(); Assert.False(drain.IsCompleted);
            gate.Release(); await drain.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { gate.Release(); try { await job; } catch (Exception) { } }
    }

    private sealed class RetryFactory(SpeechTestProcessFactory inner) : ISpeechHelperProcessFactory
    {
        internal int Starts; internal bool Overlapped;
        public async Task<ISpeechHelperProcess> StartAsync(string executable, SpeechLocalConfiguration configuration, CancellationToken cancellationToken)
        {
            if (inner.Processes.Any(process => !process.HasExited)) Overlapped = true;
            var process = await inner.StartAsync(executable, configuration, cancellationToken); Starts++;
            return Starts == 1 ? new RetryProcess(process) : process;
        }
    }
    private sealed class RetryProcess(ISpeechHelperProcess inner) : ISpeechHelperProcess
    {
        private int _attempts;
        public int Id => inner.Id; public bool HasExited => inner.HasExited; public Stream Input => inner.Input; public Stream Output => inner.Output;
        public Task<bool> StopAndReapAsync() => Interlocked.Increment(ref _attempts) == 1 ? Task.FromResult(false) : inner.StopAndReapAsync();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
