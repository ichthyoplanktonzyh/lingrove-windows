using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>3b automatic previews require source-script evidence. The platform supplies ICU
/// script properties; absent or invalid evidence rejects rather than guessing a language.</summary>
public static class WritingAutomaticLanguagePolicy
{
    public static bool Accepts(string text, LearningLanguage language, Func<int, int?> script)
    {
        ArgumentNullException.ThrowIfNull(script); if (text is null || !Enum.IsDefined(language)) return false;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int? code = script(rune.Value); if (code is null) return false;
            bool match = language switch
            {
                LearningLanguage.Chinese or LearningLanguage.TraditionalChinese => code == 17,
                LearningLanguage.Japanese => code is 17 or 20 or 22,
                LearningLanguage.Korean => code == 18,
                LearningLanguage.Russian => code == 8,
                LearningLanguage.Hindi => code == 10,
                LearningLanguage.Arabic => code == 2,
                _ => code == 25,
            };
            if (match) return true;
        }
        return false;
    }
}
