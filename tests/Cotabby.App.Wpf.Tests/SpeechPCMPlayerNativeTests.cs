using System.Windows.Threading;
using Cotabby.App.Speech;
using Cotabby.Core.Speech;

namespace Cotabby.App.Wpf.Tests;

/// <summary>The actual compiled facade runs on a Windows WPF STA. A worker callback must return
/// to that Dispatcher before emitting failure, and shutdown must join held device preparation.
/// The renderer is an explicit in-memory gate, so this is no audio-device or audible proof.</summary>
[Collection("WPF views")]
public sealed class SpeechPCMPlayerNativeTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task WorkerInvalidationReturnsToDispatcherAndShutdownDrainsPreparation() => dispatcher.RunAsync(async () =>
    {
        var owner = Dispatcher.CurrentDispatcher; var renderer = new HeldRenderer(); var player = new SpeechPCMPlayer(renderer);
        var events = new List<SpeechProviderEvent>(); player.EventHandler = e => { Assert.True(owner.CheckAccess()); events.Add(e); };
        player.Begin(24000, 1, new());
        try
        {
            await renderer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Run(() => renderer.Invalidated!()).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsType<SpeechProviderEvent.Failed>(Assert.Single(events));
            player.Shutdown(); var drain = player.DrainAsync(); Assert.False(drain.IsCompleted);
            renderer.Ready.TrySetResult(); await drain.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(renderer.Released); Assert.Empty(renderer.Enqueues);
        }
        finally { player.Shutdown(); renderer.Ready.TrySetResult(); await player.DrainAsync(); }
    });
    private sealed class HeldRenderer : ISpeechPCMRendering
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Func<Task>? Invalidated { get; private set; }
        internal bool Released { get; private set; }
        internal List<long> Enqueues { get; } = [];
        public Task PrepareAsync(int sampleRate, double rate, ulong revision, Func<long, Task> played, Func<Task> invalidated, CancellationToken cancellationToken)
        { Invalidated = invalidated; Entered.TrySetResult(); return Ready.Task; }
        public Task EnqueueAsync(SpeechPCMChunk chunk, long bufferId, ulong revision, CancellationToken cancellationToken) { Enqueues.Add(bufferId); return Task.CompletedTask; }
        public Task PlayAsync(ulong revision, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(ulong revision, bool release) { Released |= release; return Task.CompletedTask; }
    }
}
