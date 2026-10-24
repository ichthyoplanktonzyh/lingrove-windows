using System.Buffers.Binary;
using System.Collections.Concurrent;
using Cotabby.App.Speech;
using Cotabby.Core.Speech;
using Cotabby.Win32.Speech;

namespace Cotabby.App.Tests;

/// <summary>Actual DSP, facade and renderer cooperate over a detached output-device seam. The
/// fake reports controlled played output, never a speaker. Largest legal helper chunks exercise
/// slow-rate lookahead/backpressure; timeline cases cover gaps instead of assuming wall time is
/// audio progress. These are component/lifetime checks, not physical Windows acceptance.</summary>
public sealed class WindowsSpeechDeviceTests
{
    [Fact]
    public async Task LargestHelperBlocksDrainThroughRealTempoAndFacadeAtBothRateLimits()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            foreach (int sampleRate in new[] { 8000, 24000 })
            foreach (double rate in new[] { .5, 1, 1.5 })
            {
                var factory = new DeviceFactory(); await using var renderer = new WindowsSpeechPCMRenderer(factory);
                var facade = new SpeechPCMPlayer(renderer); var input = new WindowsSpeechPCMInputAdapter(facade);
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var failures = new List<SpeechProviderEvent.Failed>();
                input.EventHandler = e => { if (e is SpeechProviderEvent.Finished) finished.TrySetResult(); if (e is SpeechProviderEvent.Failed failure) failures.Add(failure); };
                var token = new SpeechPlaybackToken(); byte[] bytes = Pcm(65536);
                try
                {
                    input.Begin(sampleRate, rate, token);
                    // If the processor's real lookahead prevents the first acknowledgement
                    // before two audible seconds, this awaits until the owned test deadline.
                    await input.EnqueueAsync(new SpeechPCMChunk(bytes, sampleRate), token, deadline);
                    await input.EndInputAsync(token, deadline); await finished.Task.WaitAsync(deadline);
                    Assert.Empty(failures);
                    byte[] actual = Assert.Single(factory.Devices).CapturedPackets.SelectMany(packet => packet).ToArray();
                    if (rate == 1) Assert.Equal(bytes, actual);
                    else
                    {
                        var reference = new WindowsSpeechTempoProcessor(sampleRate, rate);
                        var packets = reference.Process(new SpeechPCMChunk(bytes, sampleRate)).ToList(); packets.AddRange(reference.CompleteInput());
                        Assert.Equal(packets.SelectMany(packet => packet).ToArray(), actual);
                    }
                }
                finally { input.Shutdown(); await facade.DrainAsync(); }
            }
        });
    }

    [Fact]
    public async Task OldFragmentCompletionCannotClearANewEpochsProducerGuard()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var inner = new HeldInput(); var input = new WindowsSpeechPCMInputAdapter(inner);
            var old = new SpeechPlaybackToken(); var current = new SpeechPlaybackToken(); byte[] bytes = Pcm(65536);
            Task? oldTask = null, currentTask = null;
            try
            {
                input.Begin(8000, 1, old); oldTask = input.EnqueueAsync(new SpeechPCMChunk(bytes, 8000), old, deadline);
                await inner.FirstEntered.Task.WaitAsync(deadline);
                input.Stop(); input.Begin(8000, 1, current); currentTask = input.EnqueueAsync(new SpeechPCMChunk(bytes, 8000), current, deadline);
                await inner.SecondEntered.Task.WaitAsync(deadline);
                inner.FirstRelease.TrySetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldTask);
                await Assert.ThrowsAsync<SpeechException>(() => input.EnqueueAsync(new SpeechPCMChunk(new byte[4], 8000), current, deadline));
                inner.SecondRelease.TrySetResult(); await currentTask;
                Assert.Single(inner.Received, pair => pair.Token == old);
                var fragments = inner.Received.Where(pair => pair.Token == current).Select(pair => pair.Chunk).ToArray();
                Assert.All(fragments, chunk => Assert.InRange(chunk.SampleCount, 1, 2000));
                Assert.Equal(bytes, fragments.SelectMany(chunk => chunk.CopyBytes()).ToArray());
                await input.EndInputAsync(current, deadline); Assert.Equal(1, inner.Ends);
            }
            finally
            {
                input.Shutdown(); inner.FirstRelease.TrySetResult(); inner.SecondRelease.TrySetResult();
                if (oldTask is not null) { try { await oldTask; } catch (OperationCanceledException) { } }
                if (currentTask is not null) { try { await currentTask; } catch (OperationCanceledException) { } }
            }
        });
    }

    [Fact]
    public void DeviceClockGapsAndUnderrunsDoNotPlayFutureAuthoredFrames()
    {
        var timeline = new WindowsSpeechOutputTimeline();
        timeline.Audio(100); timeline.Silence(300); timeline.Audio(200);
        Assert.Equal(100, timeline.Advance(350)); Assert.Equal(100, timeline.Advance(400));
        Assert.Equal(150, timeline.Advance(450)); Assert.Equal(300, timeline.Advance(1000));
        timeline.AnchorNextWrite(1200); timeline.Audio(80);
        Assert.Equal(300, timeline.Advance(1199)); Assert.Equal(300, timeline.Advance(1200));
        Assert.Equal(340, timeline.Advance(1240)); Assert.Equal(380, timeline.Advance(1280));
        Assert.Throws<InvalidOperationException>(() => timeline.Advance(1200));
    }

    [Fact]
    public async Task RendererRejectsOldEpochProgressAndJoinsDeviceRelease()
    {
        var factory = new DeviceFactory { InstantPlayback = false }; var renderer = new WindowsSpeechPCMRenderer(factory);
        var played = new ConcurrentQueue<long>(); var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await renderer.PrepareAsync(8000, 1, 2, id => { played.Enqueue(id); return Task.CompletedTask; }, () => { invalidated.TrySetResult(); return Task.CompletedTask; }, deadline.Token);
            await renderer.EnqueueAsync(new SpeechPCMChunk(Pcm(4000), 8000), 0, 2, deadline.Token);
            await renderer.PlayAsync(2, deadline.Token);
            await renderer.StopAsync(3, false);
            await renderer.PrepareAsync(8000, 1, 5, id => { played.Enqueue(id); return Task.CompletedTask; }, () => { invalidated.TrySetResult(); return Task.CompletedTask; }, deadline.Token);
            await renderer.StopAsync(3, true); // An older queued stop cannot release the new graph.
            var device = Assert.Single(factory.Devices); Assert.False(device.Disposed);
            device.Progress(2, 4000);
            await renderer.EnqueueAsync(new SpeechPCMChunk(Pcm(4000), 8000), 10, 5, deadline.Token);
            await renderer.PlayAsync(5, deadline.Token); await renderer.CompleteInputAsync(5, deadline.Token);
            Task drain = renderer.OutputDrainedAsync(5); Assert.False(drain.IsCompleted);
            device.Progress(5, 4000); device.Progress(5, 2000); // Delayed actor work may observe older monotone samples last.
            await drain.WaitAsync(deadline.Token);
            device.ReleaseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task stop = renderer.StopAsync(6, true);
            await device.DisposeEntered.Task.WaitAsync(deadline.Token); Assert.False(stop.IsCompleted);
            device.ReleaseGate.TrySetResult(); await stop;
            await renderer.DisposeAsync();
            Assert.Equal(new long[] { 10 }, played.ToArray()); Assert.False(invalidated.Task.IsCompleted);
        }
        finally
        {
            foreach (var device in factory.Devices) device.ReleaseGate?.TrySetResult();
            await renderer.DisposeAsync();
        }
    }

    [Fact]
    public async Task DeviceLossFaultsTheActiveDrainAndOldGraphNotificationsCannotRetireRecovery()
    {
        var factory = new DeviceFactory { InstantPlayback = false }; await using var renderer = new WindowsSpeechPCMRenderer(factory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await renderer.PrepareAsync(8000, 1.25, 2, _ => Task.CompletedTask, () => { failed.TrySetResult(); return Task.CompletedTask; }, deadline.Token);
        await renderer.EnqueueAsync(new SpeechPCMChunk(Pcm(4000), 8000), 0, 2, deadline.Token);
        await renderer.CompleteInputAsync(2, deadline.Token); Task drain = renderer.OutputDrainedAsync(2);
        var oldDevice = Assert.Single(factory.Devices); oldDevice.Invalidate();
        var failure = await Assert.ThrowsAsync<SpeechException>(() => drain); Assert.Equal(SpeechFailure.InvalidAudio, failure.Failure);
        await failed.Task.WaitAsync(deadline.Token);
        var recoveredInvalidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await renderer.PrepareAsync(8000, 1, 5, _ => Task.CompletedTask, () => { recoveredInvalidation.TrySetResult(); return Task.CompletedTask; }, deadline.Token);
        oldDevice.Invalidate(); oldDevice.Progress(2, 3200);
        await renderer.DisposeAsync();
        Assert.Equal(2, factory.Devices.Count); Assert.False(recoveredInvalidation.Task.IsCompleted);
    }

    private static byte[] Pcm(int samples)
    {
        var bytes = new byte[samples * 4];
        for (int index = 0; index < samples; index++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4), (index % 97 - 48) / 96f);
        return bytes;
    }
    private sealed class DeviceFactory : IWindowsSpeechOutputDeviceFactory
    {
        internal bool InstantPlayback = true;
        internal List<Device> Devices { get; } = [];
        public Task<IWindowsSpeechOutputDevice> CreateAsync(int sampleRate, Action<ulong, long> progressed, Action invalidated, CancellationToken cancellationToken)
        {
            var device = new Device(progressed, invalidated, InstantPlayback); Devices.Add(device); return Task.FromResult<IWindowsSpeechOutputDevice>(device);
        }
    }
    private sealed class Device(Action<ulong, long> progressed, Action invalidated, bool instant) : IWindowsSpeechOutputDevice
    {
        private int _invalid;
        private ulong _epoch;
        private long _frames;
        private bool _playing;
        internal bool Disposed;
        internal List<byte[]> CapturedPackets { get; } = [];
        internal TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? ReleaseGate;
        public bool IsValid => Volatile.Read(ref _invalid) == 0;
        public Task ResetAsync(ulong epoch) { _epoch = epoch; _frames = 0; _playing = false; return Task.CompletedTask; }
        public Task EnqueueAsync(IReadOnlyList<byte[]> packets, ulong epoch)
        {
            Assert.Equal(_epoch, epoch); CapturedPackets.AddRange(packets); _frames += packets.Sum(packet => (long)packet.Length / 4);
            if (instant && _playing) Progress(epoch, _frames); return Task.CompletedTask;
        }
        public Task StartAsync(ulong epoch) { Assert.Equal(_epoch, epoch); _playing = true; if (instant) Progress(epoch, _frames); return Task.CompletedTask; }
        internal void Progress(ulong epoch, long frames) => progressed(epoch, frames);
        internal void Invalidate() { Interlocked.Exchange(ref _invalid, 1); invalidated(); }
        public async ValueTask DisposeAsync() { Disposed = true; DisposeEntered.TrySetResult(); if (ReleaseGate is { } gate) await gate.Task; }
    }
    private sealed class HeldInput : ISpeechPCMPlaying
    {
        internal TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource FirstRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SecondEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SecondRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<(SpeechPlaybackToken Token, SpeechPCMChunk Chunk)> Received { get; } = [];
        internal int Ends;
        public Action<SpeechProviderEvent>? EventHandler { get; set; }
        public void Begin(int sampleRate, double rate, SpeechPlaybackToken token) { }
        public Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default)
        {
            Received.Add((token, chunk));
            if (Received.Count == 1) { FirstEntered.TrySetResult(); return FirstRelease.Task; }
            if (Received.Count == 2) { SecondEntered.TrySetResult(); return SecondRelease.Task; }
            return Task.CompletedTask;
        }
        public Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default) { Ends++; return Task.CompletedTask; }
        public void Stop() { }
        public void Shutdown() { }
    }
}
