using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>The original voice-language compatibility ranking. Catalog/voice selection will
/// consume this portable rule while the platform owns its installed inventory and ID collation.</summary>
public enum SpeechVoiceLanguageMatch { Exact = 0, SameLanguage = 1 }

/// <summary>Pure compatibility between an installed voice's BCP-47 tag and Lingrove's language
/// palette. Chinese requires matching script, explicit or derived from the first two-grapheme
/// region; a bare zh never silently crosses Hans/Hant. This does not enumerate/select voices.</summary>
public static partial class SpeechVoicePolicy
{
    public static SpeechVoiceLanguageMatch? MatchLanguage(string voiceIdentifier, LearningLanguage language)
    {
        string voiceTag = Normalize(voiceIdentifier), languageTag = Normalize(language.Code());
        if (CanonicalStringComparer.Instance.Equals(voiceTag, languageTag)) return SpeechVoiceLanguageMatch.Exact;
        var voice = voiceTag.Split('-', StringSplitOptions.RemoveEmptyEntries); var requested = languageTag.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (voice.Length == 0 || !CanonicalStringComparer.Instance.Equals(voice[0], requested[0])) return null;
        if (requested[0] == "zh")
        {
            string? script = requested.Skip(1).FirstOrDefault(c => c is "hans" or "hant");
            if (script is null || ChineseScript(voice) != script) return null;
        }
        return SpeechVoiceLanguageMatch.SameLanguage;
    }
    private static string Normalize(string identifier) => LearningExplanationText.Trim(identifier).Replace('_', '-').ToLowerInvariant();
    private static string? ChineseScript(string[] components)
    {
        if (components.Skip(1).FirstOrDefault(c => c is "hans" or "hant") is { } script) return script;
        string? region = components.Skip(1).FirstOrDefault(c => GraphemeString.CountOf(c) == 2)?.ToUpperInvariant();
        return region switch { "CN" or "SG" => "hans", "TW" or "HK" or "MO" => "hant", _ => null };
    }
}
