using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>A card owns this stable target identity; the shared controller borrows its value,
/// never the view. Equal text on two cards still has independent toggle/error/dismissal scope.</summary>
public sealed record SpeechTarget(Guid OwnerId, string ItemId)
{
    public bool Equals(SpeechTarget? other) => other is not null && OwnerId == other.OwnerId && CanonicalStringComparer.Instance.Equals(ItemId, other.ItemId);
    public override int GetHashCode() => HashCode.Combine(OwnerId, CanonicalStringComparer.Instance.GetHashCode(ItemId));
}
/// <summary>Normal card clicks detect language locally; settings auditions keep an explicit
/// selection. This per-action intent does not create another durable language preference.</summary>
public enum SpeechLanguageMode { Automatic, Fixed }
/// <summary>One explicit click captures its text/hint and optional same-side recognition context.
/// The controller sanitizes/resolves it once, then creates a fixed request without Context before
/// any provider sees it. Context is never spoken or sent to an endpoint.</summary>
public sealed record SpeechRequest(SpeechTarget Target, string Text, LearningLanguage? Language,
    string? Context = null, SpeechLanguageMode LanguageMode = SpeechLanguageMode.Automatic)
{
    public bool Equals(SpeechRequest? other) => other is not null && Target == other.Target && Language == other.Language && LanguageMode == other.LanguageMode
        && CanonicalStringComparer.Instance.Equals(Text, other.Text) && CanonicalStringComparer.Instance.Equals(Context, other.Context);
    public override int GetHashCode() => HashCode.Combine(Target, Language, LanguageMode, CanonicalStringComparer.Instance.GetHashCode(Text), Context is null ? 0 : CanonicalStringComparer.Instance.GetHashCode(Context));
}

/// <summary>One UI-owned system provider enumerates voices only on explicit request. Utterance
/// callbacks already run on that UI context and retain their original token. Stop must return
/// promptly without throwing; it can deliver a cancelled event for the retired token.</summary>
public interface ISpeechProviding
{
    Action<SpeechProviderEvent>? EventHandler { get; set; }
    IReadOnlyList<SpeechVoice> RefreshVoices();
    void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token);
    void Stop(SpeechPlaybackToken token);
}
/// <summary>Encoded endpoint/review playback shares one controller-owned device facade. Like the
/// source AVAudioPlayer boundary, Play validates decoding/duration and emits Started or throws;
/// Stop promptly detaches playback without throwing or emitting a synchronous completion. Native completion
/// callbacks hop to UI with their captured token. No synthesizer or transport is owned here.</summary>
public interface ISpeechAudioPlaying
{
    Action<SpeechProviderEvent>? EventHandler { get; set; }
    void Play(ReadOnlySpan<byte> data, double rate, SpeechPlaybackToken token);
    void Stop();
}
