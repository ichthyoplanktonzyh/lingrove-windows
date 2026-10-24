namespace Cotabby.Win32.Speech;

/// <summary>The WASAPI owner records where authored tempo output was submitted on its device
/// stream. Generation gaps insert silence: hardware time then advances while authored sample
/// count does not. Keeping those coordinates separate prevents a later chunk being acknowledged
/// merely because enough wall-clock/device time elapsed during a gap. One native owner mutates
/// this ledger; it stores counts only, never audio. Reset belongs to a new playback epoch.</summary>
internal sealed class WindowsSpeechOutputTimeline
{
    private sealed record Segment(long AudioStart, long DeviceStart, int Frames);
    private readonly Queue<Segment> _segments = [];
    private long _lastDeviceCursor;
    internal long WrittenDeviceFrames { get; private set; }
    internal long WrittenAudioFrames { get; private set; }
    internal long PlayedAudioFrames { get; private set; }

    internal void Silence(long frames)
    {
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        WrittenDeviceFrames = checked(WrittenDeviceFrames + frames);
    }
    internal void Audio(int frames)
    {
        if (frames <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
        _segments.Enqueue(new(WrittenAudioFrames, WrittenDeviceFrames, frames));
        WrittenAudioFrames = checked(WrittenAudioFrames + frames);
        WrittenDeviceFrames = checked(WrittenDeviceFrames + frames);
    }
    internal void AnchorNextWrite(long conservativeDeviceFrame)
    {
        if (conservativeDeviceFrame < 0) throw new ArgumentOutOfRangeException(nameof(conservativeDeviceFrame));
        // A driver underrun can advance its clock beyond the last submitted frame. Re-anchor
        // future writes after that position; an old empty queue must not assign new PCM to it.
        if (conservativeDeviceFrame > WrittenDeviceFrames) Silence(conservativeDeviceFrame - WrittenDeviceFrames);
    }
    internal long Advance(long deviceFrame)
    {
        if (deviceFrame < _lastDeviceCursor) throw new InvalidOperationException("Audio device cursor moved backwards.");
        _lastDeviceCursor = deviceFrame;
        while (_segments.TryPeek(out var segment))
        {
            if (deviceFrame <= segment.DeviceStart) break;
            long played = Math.Min(segment.Frames, deviceFrame - segment.DeviceStart);
            PlayedAudioFrames = Math.Max(PlayedAudioFrames, segment.AudioStart + played);
            if (played < segment.Frames) break;
            _segments.Dequeue();
        }
        return PlayedAudioFrames;
    }
}
