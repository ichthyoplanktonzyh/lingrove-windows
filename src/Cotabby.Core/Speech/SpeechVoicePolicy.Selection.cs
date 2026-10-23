using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>Language-compatible ranking and selection from a frozen catalog. The catalog owner
/// supplies the platform's localized natural ID comparison, corresponding to the Mac call to
/// localizedStandardCompare. Requiring that boundary avoids silently substituting ordinal order.
/// This policy does not enumerate voices, retain a service, or synthesize on a view's behalf.</summary>
public static partial class SpeechVoicePolicy
{
    public static IReadOnlyList<SpeechVoice> Voices(IReadOnlyList<SpeechVoice> catalog,
        LearningLanguage language, IComparer<string> localizedIdComparer)
    {
        ArgumentNullException.ThrowIfNull(localizedIdComparer);
        return Array.AsReadOnly(catalog.Select(voice => (Voice: voice, Match: MatchLanguage(voice.Language, language)))
            .Where(pair => pair.Match is not null)
            .OrderBy(pair => pair.Match!.Value)
            .ThenBy(pair => pair.Voice.IsNovelty)
            .ThenByDescending(pair => pair.Voice.IsSystemPreferred)
            .ThenByDescending(pair => pair.Voice.Quality)
            .ThenBy(pair => pair.Voice.Id, localizedIdComparer)
            .Select(pair => pair.Voice).ToArray());
    }

    public static SpeechVoice? Select(IReadOnlyList<SpeechVoice> catalog, LearningLanguage language,
        string? preferredVoiceId, IComparer<string> localizedIdComparer)
    {
        var compatible = Voices(catalog, language, localizedIdComparer);
        if (preferredVoiceId is not null && compatible.FirstOrDefault(v => CanonicalStringComparer.Instance.Equals(v.Id, preferredVoiceId)) is { } preferred)
            return preferred;
        return compatible.FirstOrDefault(v => !v.IsNovelty);
    }
}
