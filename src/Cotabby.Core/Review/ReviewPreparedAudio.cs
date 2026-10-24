using Cotabby.Core.Speech;

namespace Cotabby.Core.Review;

/// <summary>One sheet-owned in-memory recording, distinct from a persisted Anki media receipt.
/// The preparation owner releases its references on discard; encoded bytes stay detached and
/// immutable. Only an acknowledged upload may later turn this fingerprint into a saved filename.</summary>
public sealed class ReviewPreparedAudio(string fingerprint, SpeechExportAudio audio)
{
    public string Fingerprint { get; } = fingerprint;
    public SpeechExportAudio Audio { get; } = audio;
}
