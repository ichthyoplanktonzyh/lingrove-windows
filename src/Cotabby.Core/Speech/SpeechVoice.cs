using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>The system catalog's relative quality, after language and explicit preference.
/// Native voice adapters map their available quality metadata into these original ranks.</summary>
public enum SpeechVoiceQuality { Standard = 0, Enhanced = 1, Premium = 2 }

/// <summary>Immutable installed-voice snapshot, independent of a synthesizer or UI object.
/// The platform catalog owns discovery; selection retains the exact script/region tag and ID.
/// Canonical string equality preserves Swift value semantics without rewriting display text.</summary>
public sealed record SpeechVoice(string Id, string Name, string Language, SpeechVoiceQuality Quality,
    bool IsSystemPreferred = false, bool IsNovelty = false)
{
    public bool Equals(SpeechVoice? other) => other is not null && Same(Id, other.Id) && Same(Name, other.Name)
        && Same(Language, other.Language) && Quality == other.Quality
        && IsSystemPreferred == other.IsSystemPreferred && IsNovelty == other.IsNovelty;
    public override int GetHashCode() => HashCode.Combine(CanonicalStringComparer.Instance.GetHashCode(Id),
        CanonicalStringComparer.Instance.GetHashCode(Name), CanonicalStringComparer.Instance.GetHashCode(Language), Quality, IsSystemPreferred, IsNovelty);
    private static bool Same(string a, string b) => CanonicalStringComparer.Instance.Equals(a, b);
}
