using Cotabby.Core.Speech;

namespace Cotabby.Win32.Speech;

/// <summary>Composition borrows one UI-owned PCM facade through this Windows device adapter.
/// A helper block can contain several seconds, whereas the tempo processor needs later input
/// to release its analysis window. Fine-grained facade buffer IDs let played earlier fragments
/// release the original two-second backpressure without waiting for that entire helper block.
/// One inner player and one continuous renderer remain alive across fragments. The host still
/// owns the inner player's shutdown/drain; this adapter owns neither a device nor an extra graph.</summary>
public sealed class WindowsSpeechPCMInputAdapter : ISpeechPCMPlaying
{
    private readonly ISpeechPCMPlaying _inner;
    private readonly int _ownerThread;
    private SpeechPlaybackToken? _token;
    private bool _enqueueActive;
    private bool _ending;
    private ulong _epoch;

    public WindowsSpeechPCMInputAdapter(ISpeechPCMPlaying inner)
    {
        if (SynchronizationContext.Current is null) throw new InvalidOperationException("Speech input requires a serial UI context.");
        _inner = inner; _ownerThread = Environment.CurrentManagedThreadId;
    }
    public Action<SpeechProviderEvent>? EventHandler
    {
        get { RequireOwner(); return _inner.EventHandler; }
        set { RequireOwner(); _inner.EventHandler = value; }
    }
    public void Begin(int sampleRate, double rate, SpeechPlaybackToken token)
    {
        RequireOwner(); _epoch++; _token = token; _ending = false; _enqueueActive = false;
        _inner.Begin(sampleRate, rate, token);
    }
    public async Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default)
    {
        RequireOwner(); RequireInput(token, cancellationToken);
        // The generator normally awaits each receiver call. Reject overlap explicitly instead
        // of interleaving two helper blocks after one of their fragment awaits yields to UI.
        if (_enqueueActive) throw new SpeechException(SpeechFailure.InvalidAudio);
        ulong epoch = _epoch;
        _enqueueActive = true;
        try
        {
            byte[] bytes = chunk.CopyBytes(); int fragmentBytes = chunk.SampleRate / 4 * 4;
            for (int offset = 0; offset < bytes.Length; offset += fragmentBytes)
            {
                RequireInput(token, cancellationToken, epoch);
                var fragment = new SpeechPCMChunk(bytes.AsSpan(offset, Math.Min(fragmentBytes, bytes.Length - offset)), chunk.SampleRate);
                await _inner.EnqueueAsync(fragment, token, cancellationToken);
                RequireInput(token, cancellationToken, epoch);
            }
        }
        finally { if (epoch == _epoch) _enqueueActive = false; }
    }
    public async Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default)
    {
        RequireOwner(); RequireInput(token, cancellationToken);
        if (_enqueueActive) throw new SpeechException(SpeechFailure.InvalidAudio);
        _ending = true;
        await _inner.EndInputAsync(token, cancellationToken);
    }
    public void Stop() { RequireOwner(); _epoch++; _token = null; _ending = false; _enqueueActive = false; _inner.Stop(); }
    public void Shutdown() { RequireOwner(); _epoch++; _token = null; _ending = false; _enqueueActive = false; _inner.Shutdown(); }
    private void RequireInput(SpeechPlaybackToken token, CancellationToken cancellationToken, ulong? epoch = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_token != token || epoch is { } expected && expected != _epoch) throw new OperationCanceledException();
        if (_ending) throw new SpeechException(SpeechFailure.InvalidAudio);
    }
    private void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Speech input belongs to its serial UI context.");
    }
}
