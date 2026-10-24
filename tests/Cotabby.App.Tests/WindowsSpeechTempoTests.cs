using System.Buffers.Binary;
using Cotabby.App.Speech;
using Cotabby.Core.Speech;
using Cotabby.Win32.Speech;

namespace Cotabby.App.Tests;

/// <summary>Exercise the actual Windows DSP with generated PCM, independently of a speaker or
/// speech model. Frequency/duration detect a sample-rate shortcut; packet invariance and EOF
/// cover streaming seams. These measurements do not establish perceptual or device quality.</summary>
public sealed class WindowsSpeechTempoTests
{
    [Theory, InlineData(.5), InlineData(1), InlineData(1.5)]
    public void TempoChangesDurationAndKeepsToneFrequency(double rate)
    {
        byte[] input = Tone(48000 * 2, 48000); var processor = new WindowsSpeechTempoProcessor(48000, rate);
        var packets = Process(processor, input, 48000, 12000);
        if (rate != 1) Assert.True(processor.OutputSamples < processor.ExpectedOutputSamples);
        packets.AddRange(processor.CompleteInput());
        Assert.All(packets, packet => Assert.InRange(packet.Length, 4, 8192 * 4));
        var audio = packets.SelectMany(packet => Samples(packet)).ToArray();
        Assert.InRange(Math.Abs(audio.Length - input.Length / 4 / rate), 0, 1);
        Assert.All(audio, sample => Assert.True(float.IsFinite(sample) && Math.Abs(sample) <= 1));
        // Count complete positive crossings away from the processor's start/tail windows.
        int start = 4800, end = audio.Length - 4800;
        var crossings = Enumerable.Range(start + 1, end - start - 1).Where(i => audio[i - 1] <= 0 && audio[i] > 0).ToArray();
        Assert.True(crossings.Length > 100);
        double frequency = (crossings.Length - 1) * 48000d / (crossings[^1] - crossings[0]);
        Assert.InRange(frequency, 438, 442);
        if (rate == 1) Assert.Equal(input, packets.SelectMany(packet => packet).ToArray());
    }

    [Fact]
    public void HelperPacketBoundariesDoNotFlushOrChangeTheContinuousOutput()
    {
        byte[] input = Tone(48000, 48000);
        var large = new WindowsSpeechTempoProcessor(48000, 1.25); var small = new WindowsSpeechTempoProcessor(48000, 1.25);
        var a = Process(large, input, 48000, 48000); a.AddRange(large.CompleteInput());
        var b = Process(small, input, 48000, 137); b.AddRange(small.CompleteInput());
        Assert.Equal(a.SelectMany(packet => packet).ToArray(), b.SelectMany(packet => packet).ToArray());
        Assert.Equal(38400, large.OutputSamples); Assert.Equal(large.OutputSamples, small.OutputSamples);
        Assert.Throws<SpeechException>(() => large.CompleteInput());
        Assert.Throws<SpeechException>(() => large.Process(new SpeechPCMChunk(new byte[4], 48000)));
        small.Retire(); Assert.ThrowsAny<OperationCanceledException>(() => small.CompleteInput());
    }

    [Fact]
    public void CancelledOrWrongFormatInputIsNotAdmitted()
    {
        var processor = new WindowsSpeechTempoProcessor(24000, 1.25);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => processor.Process(new SpeechPCMChunk(new byte[400], 24000), cancelled.Token));
        Assert.Throws<SpeechException>(() => processor.Process(new SpeechPCMChunk(new byte[400], 16000)));
        Assert.Equal(0, processor.InputSamples); Assert.Equal(0, processor.OutputSamples);
        Assert.Throws<SpeechException>(() => new WindowsSpeechTempoProcessor(24000, double.NaN));
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task EofFlushJoinsBeforeCompletionAndRejectsARetiredUtterance(bool retire)
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var renderer = new HeldEofRenderer(); var player = new SpeechPCMPlayer(renderer); var events = new List<SpeechProviderEvent>();
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.EventHandler = e => { events.Add(e); if (e is SpeechProviderEvent.Finished) finished.TrySetResult(); };
            var token = new SpeechPlaybackToken(); Task? end = null;
            try
            {
                player.Begin(8000, 1.25, token);
                await player.EnqueueAsync(new SpeechPCMChunk(new byte[32000], 8000), token, deadline);
                end = player.EndInputAsync(token, deadline); await renderer.Entered.Task.WaitAsync(deadline);
                await Assert.ThrowsAsync<SpeechException>(() => player.EndInputAsync(token, deadline));
                await Assert.ThrowsAsync<SpeechException>(() => player.EnqueueAsync(new SpeechPCMChunk(new byte[4], 8000), token, deadline));
                Assert.Equal(1, renderer.Completions);
                await renderer.Played!(0);
                Assert.Empty(events.OfType<SpeechProviderEvent.Finished>());
                if (retire) { player.Stop(); player.Begin(8000, 1.25, new()); }
                renderer.Release.TrySetResult();
                if (retire)
                {
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => end);
                    Assert.Empty(events.OfType<SpeechProviderEvent.Finished>());
                }
                else
                {
                    await end;
                    Assert.Empty(events.OfType<SpeechProviderEvent.Finished>());
                    renderer.TailDrained.TrySetResult(); await finished.Task.WaitAsync(deadline);
                    Assert.Equal(token, Assert.Single(events.OfType<SpeechProviderEvent.Finished>()).Token);
                }
            }
            finally
            {
                player.Shutdown(); renderer.Release.TrySetResult(); renderer.TailDrained.TrySetResult();
                if (end is not null) { try { await end; } catch (OperationCanceledException) { } }
                await player.DrainAsync();
            }
        });
    }

    [Fact]
    public async Task EofStillFlushesAfterTheLastEarlierBufferCallback()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var renderer = new HeldEofRenderer(); var player = new SpeechPCMPlayer(renderer);
            var events = new List<SpeechProviderEvent>(); var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.EventHandler = e => { events.Add(e); if (e is SpeechProviderEvent.Finished) finished.TrySetResult(); };
            var token = new SpeechPlaybackToken(); Task? end = null;
            try
            {
                player.Begin(8000, 1.25, token);
                await player.EnqueueAsync(new SpeechPCMChunk(new byte[32000], 8000), token, deadline);
                await renderer.Played!(0); // The queue empties before generation reports EOF.
                end = player.EndInputAsync(token, deadline);
                await renderer.Entered.Task.WaitAsync(deadline);
                Assert.False(end.IsCompleted); Assert.Empty(events.OfType<SpeechProviderEvent.Finished>());
                renderer.Release.TrySetResult(); await end;
                Assert.Empty(events.OfType<SpeechProviderEvent.Finished>());
                renderer.TailDrained.TrySetResult(); await finished.Task.WaitAsync(deadline);
                Assert.Equal(token, Assert.Single(events.OfType<SpeechProviderEvent.Finished>()).Token);
            }
            finally
            {
                player.Shutdown(); renderer.Release.TrySetResult(); renderer.TailDrained.TrySetResult();
                if (end is not null) await end;
                await player.DrainAsync();
            }
        });
    }

    private static List<byte[]> Process(WindowsSpeechTempoProcessor processor, byte[] input, int sampleRate, int samplesPerChunk)
    {
        var packets = new List<byte[]>();
        for (int offset = 0; offset < input.Length; offset += samplesPerChunk * 4)
            packets.AddRange(processor.Process(new SpeechPCMChunk(input.AsSpan(offset, Math.Min(samplesPerChunk * 4, input.Length - offset)), sampleRate)));
        return packets;
    }

    [Fact]
    public async Task ShutdownJoinsAnAlreadyRegisteredTailBarrier()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var renderer = new HeldEofRenderer(); var player = new SpeechPCMPlayer(renderer);
            var events = new List<SpeechProviderEvent>(); player.EventHandler = events.Add;
            var token = new SpeechPlaybackToken(); Task? end = null;
            try
            {
                player.Begin(8000, 1.25, token);
                await player.EnqueueAsync(new SpeechPCMChunk(new byte[32000], 8000), token, deadline);
                end = player.EndInputAsync(token, deadline); await renderer.Entered.Task.WaitAsync(deadline);
                renderer.Release.TrySetResult(); await end;
                player.Shutdown(); Task drain = player.DrainAsync(); Assert.False(drain.IsCompleted);
                // The held native seam ignores Stop until this release. Cancellation must not
                // abandon it or let its late successful result finish a retired utterance.
                renderer.TailDrained.TrySetResult(); await drain;
                Assert.Empty(events.OfType<SpeechProviderEvent.Finished>());
            }
            finally
            {
                player.Shutdown(); renderer.Release.TrySetResult(); renderer.TailDrained.TrySetResult();
                if (end is not null) await end;
                await player.DrainAsync();
            }
        });
    }
    private static byte[] Tone(int samples, int sampleRate)
    {
        var bytes = new byte[samples * 4];
        for (int index = 0; index < samples; index++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4), (float)(.5 * Math.Sin(index * 2 * Math.PI * 440 / sampleRate)));
        return bytes;
    }
    private static float[] Samples(byte[] packet)
    {
        var samples = new float[packet.Length / 4];
        for (int index = 0; index < samples.Length; index++) samples[index] = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(index * 4));
        return samples;
    }
    private sealed class HeldEofRenderer : ISpeechPCMRendering
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource TailDrained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Func<long, Task>? Played;
        internal int Completions;
        public Task PrepareAsync(int sampleRate, double rate, ulong revision, Func<long, Task> played, Func<Task> invalidated, CancellationToken cancellationToken) { Played = played; return Task.CompletedTask; }
        public Task EnqueueAsync(SpeechPCMChunk chunk, long bufferId, ulong revision, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteInputAsync(ulong revision, CancellationToken cancellationToken) { Completions++; Entered.TrySetResult(); return Release.Task; }
        public Task OutputDrainedAsync(ulong revision) => TailDrained.Task;
        public Task PlayAsync(ulong revision, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(ulong revision, bool release) => Task.CompletedTask;
    }
}
