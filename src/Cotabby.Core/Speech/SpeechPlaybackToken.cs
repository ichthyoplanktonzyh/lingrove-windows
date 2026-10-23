namespace Cotabby.Core.Speech;

/// <summary>One immutable provider operation identity. Delayed callbacks carry their original
/// token so the shared controller can reject them after another utterance replaces the target.</summary>
public readonly record struct SpeechPlaybackToken(Guid RawValue)
{
    public SpeechPlaybackToken() : this(Guid.NewGuid()) { }
}
