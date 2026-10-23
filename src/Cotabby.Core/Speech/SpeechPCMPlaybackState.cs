namespace Cotabby.Core.Speech;

/// <summary>One serial playback owner keeps this queue ledger independently of the audio device.
/// Producer EOF and device completion are separate: an empty queue during generation is not the
/// end of an utterance. Buffer identities make duplicate/stale device acknowledgements harmless.
/// Like the original value, this mutable ledger is not thread-safe; the playback boundary must
/// serialize callbacks. It retains sample counts only, never audio/text/credentials.</summary>
public sealed class SpeechPCMPlaybackState
{
    private readonly Dictionary<long, long> _buffers = [];
    private long _nextId;
    public long QueuedSamples { get; private set; }
    public long TotalSamples { get; private set; }
    public bool InputEnded { get; private set; }
    public bool IsFinished => InputEnded && QueuedSamples == 0;
    public long Enqueue(long samples)
    {
        // Count the lifetime budget, not just the queue: draining cannot admit unlimited audio.
        // Subtract only after positivity admission so extreme inputs cannot wrap this bound.
        if (InputEnded || samples <= 0 || TotalSamples > 11_520_000 - samples) throw SpeechPCMChunk.Invalid();
        long id = _nextId++; _buffers[id] = samples; QueuedSamples += samples; TotalSamples += samples;
        return id;
    }
    public void Played(long id)
    { if (_buffers.Remove(id, out long count)) QueuedSamples -= count; }
    public void EndInput()
    {
        if (TotalSamples == 0 || InputEnded) throw SpeechPCMChunk.Invalid();
        InputEnded = true;
    }
    public bool CanStart(long sampleRate, double rate)
    {
        if (QueuedSamples <= 0 || sampleRate <= 0 || !(rate > 0)) return false;
        // EOF releases a short utterance; otherwise half a second of audible buffered audio
        // absorbs jitter. Faster playback therefore requires proportionally more queued samples.
        return InputEnded || (double)QueuedSamples / sampleRate / rate >= 0.5;
    }
}
