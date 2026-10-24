namespace Cotabby.Core.Speech;

/// <summary>The shared speech controller publishes these two original per-target phases.</summary>
public enum SpeechPlaybackPhase { Preparing, Playing }

/// <summary>Detached provider callbacks name the utterance that produced them. The controller
/// rejects an old token before changing its current target; no provider/native object is retained.</summary>
public abstract record SpeechProviderEvent(SpeechPlaybackToken Token)
{
    public sealed record Prepared(SpeechPlaybackToken Token, long Milliseconds) : SpeechProviderEvent(Token);
    public sealed record Started(SpeechPlaybackToken Token) : SpeechProviderEvent(Token);
    public sealed record Finished(SpeechPlaybackToken Token) : SpeechProviderEvent(Token);
    public sealed record Cancelled(SpeechPlaybackToken Token) : SpeechProviderEvent(Token);
    public sealed record Failed(SpeechPlaybackToken Token, string Message) : SpeechProviderEvent(Token);
}

/// <summary>One controller borrows one UI-context PCM facade. Begin overlaps device preparation
/// with model startup; Enqueue awaits that same barrier. EOF and device drain remain distinct.
/// Callers own and join their enqueue/input tasks before releasing the player.</summary>
public interface ISpeechPCMPlaying
{
    Action<SpeechProviderEvent>? EventHandler { get; set; }
    void Begin(int sampleRate, double rate, SpeechPlaybackToken token);
    Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default);
    Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default);
    void Stop();
    void Shutdown();
}

/// <summary>Only immutable PCM and identities cross from the UI facade to a serialized device
/// owner. Implementations return promptly and perform native work away from the UI. Prepare must
/// accept only a strictly newer revision; enqueue/play require the current active revision; stop
/// ignores an older revision. An old queued stop must never pause a newer utterance. Device loss
/// invalidates the current graph before discarded-buffer callbacks can report completion.
/// Stop is a nonthrowing retirement operation; release additionally relinquishes device resources.
/// This contract supplies no device or synthetic production audio.</summary>
public interface ISpeechPCMRendering
{
    Task PrepareAsync(int sampleRate, double rate, ulong revision, Func<long, Task> played,
        Func<Task> invalidated, CancellationToken cancellationToken);
    Task EnqueueAsync(SpeechPCMChunk chunk, long bufferId, ulong revision, CancellationToken cancellationToken);
    Task PlayAsync(ulong revision, CancellationToken cancellationToken);
    Task StopAsync(ulong revision, bool release);
}
