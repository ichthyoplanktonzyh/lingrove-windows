using System.Runtime.Versioning;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;

namespace Cotabby.Win32.Speech;

/// <summary>Composition borrows this lazy in-memory Windows codec for export validation and
/// encoded playback. Each explicit operation freezes bounded bytes, decodes on its own joined MTA
/// and returns detached Float32 packets. No output device, temporary file, URL, model or voice is
/// opened. The native owner counts actual decoded frames through EOF instead of trusting headers.
/// Playback and export retain their existing at-most90-second admission.</summary>
[SupportedOSPlatform("windows10.0")]
public sealed partial class WindowsSpeechAudioCodec : ISpeechAudioDuration
{
    public async Task<double> DurationAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
        => (await DecodeBoundedAsync(audio, cancellationToken, materialize: false).ConfigureAwait(false)).Duration;

    /// <summary>The legacy synchronous seam is usable only off UI. Application export uses
    /// DurationAsync so cancellation and native cleanup complete before its export scope ends.</summary>
    public double Duration(ReadOnlySpan<byte> audio)
    {
        if (SynchronizationContext.Current is not null) throw new InvalidOperationException("Use asynchronous audio decoding on UI.");
        if (audio.IsEmpty || audio.Length > SpeechExportPolicy.MaximumBytes) throw Invalid();
        return DurationAsync(audio.ToArray(), CancellationToken.None).GetAwaiter().GetResult();
    }

    internal Task<WindowsSpeechDecodedAudio> DecodeAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default)
        => DecodeBoundedAsync(audio, cancellationToken, materialize: true);

    private Task<WindowsSpeechDecodedAudio> DecodeBoundedAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken, bool materialize)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("Speech decoding requires Windows 10 or later.");
        if (audio.IsEmpty || audio.Length > SpeechExportPolicy.MaximumBytes) throw Invalid();
        byte[] owned = audio.ToArray();
        // Product encoded exports are WAV or MP3. Refuse playlists/containers which could name
        // an external media resource; the native resolver receives only this owned memory stream.
        bool wave = owned.Length >= 12 && owned.AsSpan(0, 4).SequenceEqual("RIFF"u8) && owned.AsSpan(8, 4).SequenceEqual("WAVE"u8);
        bool mp3 = owned.AsSpan().StartsWith("ID3"u8) || owned.Length > 2 && owned[0] == 0xff
            && (owned[1] & 0xe0) == 0xe0 && (owned[1] & 0x06) != 0 && (owned[1] & 0x18) != 0x08;
        if (!wave && !mp3) throw Invalid();
        return WindowsSpeechMta.RunAsync(() =>
        {
            try { return DecodeOwned(owned, cancellationToken, materialize); }
            catch (OperationCanceledException) { throw; }
            catch { throw Invalid(); }
        }, cancellationToken);
    }
    private static SpeechException Invalid() => new(SpeechFailure.InvalidAudio);
}
