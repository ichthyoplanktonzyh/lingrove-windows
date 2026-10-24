using System.Diagnostics;
using Cotabby.Core.Speech;
using Cotabby.Tests.Speech;
using Cotabby.Win32.Speech;
namespace Cotabby.Inference.Tests;

public sealed class WindowsSpeechProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedUncancelledBarrierAllowsEofThenReapsAnUnresponsiveChild(bool busy)
    {
        await using var fixture = new SpeechRuntimeFixture();
        var factory = new WindowsSpeechProcessFactory();
        var child = await factory.StartAsync(fixture.Executable, fixture.Configuration, CancellationToken.None);
        try
        {
            if (busy)
            {
                byte[] request = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"fixture\",\"text\":\"ignore-term\"}\n");
                await child.Input.WriteAsync(request); await child.Input.FlushAsync(); await fixture.WaitForRequestAsync("ignore-term");
            }
            else await SpeechRuntimeFixture.WaitAsync(() => fixture.Lines("starts").Length == 1);
            Task<bool> first = child.StopAndReapAsync(), second = child.StopAndReapAsync();
            Assert.Same(first, second); Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(10))); Assert.True(child.HasExited);
            Assert.Equal(!busy, File.Exists(Path.Combine(fixture.Root, "eof")));
            await child.DisposeAsync(); await child.DisposeAsync(); Assert.True(await child.StopAndReapAsync());
        }
        finally { Assert.True(await child.StopAndReapAsync()); await child.DisposeAsync(); }
    }

    [Fact]
    public async Task ClosingTheOwnedJobReapsAHelpersDescendant()
    {
        await using var fixture = new SpeechRuntimeFixture(_ => new WindowsSpeechProcessFactory());
        Process? descendant = null;
        try
        {
            await fixture.CollectAsync(fixture.Request("descendant"));
            await SpeechRuntimeFixture.WaitAsync(() => File.Exists(Path.Combine(fixture.Root, "descendant")));
            descendant = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(fixture.Root, "descendant"))));
            _ = descendant.Handle; Assert.False(descendant.HasExited);
            await fixture.Runtime.ShutdownAsync();
            await descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(descendant.HasExited);
        }
        finally
        {
            if (descendant is not null)
            {
                if (!descendant.HasExited) { descendant.Kill(entireProcessTree: true); await descendant.WaitForExitAsync(); }
                descendant.Dispose();
            }
        }
    }

    [Fact]
    public async Task CancelledStartupAndMissingExecutableCannotLeaveAChildOrExposeItsPath()
    {
        await using var fixture = new SpeechRuntimeFixture(); var factory = new WindowsSpeechProcessFactory();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.StartAsync(fixture.Executable, fixture.Configuration, cancellation.Token));
        string missing = Path.Combine(fixture.Root, "private missing executable.exe");
        var error = await Assert.ThrowsAsync<SpeechException>(() => factory.StartAsync(missing, fixture.Configuration, CancellationToken.None));
        Assert.Equal(SpeechFailure.RuntimeUnavailable, error.Failure); Assert.DoesNotContain(fixture.Root, error.ToString()); Assert.Empty(fixture.Lines("starts"));
    }

    [Fact]
    public async Task NativePressureBindingCanBeRepeatedlyCancelledAndJoinedWithoutForcingLowMemory()
    {
        var source = new WindowsSpeechMemoryPressure();
        for (int i = 0; i < 3; i++)
        {
            var binding = source.Observe(() => { });
            await Task.Run(binding.Dispose).WaitAsync(TimeSpan.FromSeconds(5)); binding.Dispose();
        }
    }
}
