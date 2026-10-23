using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Speech;

/// <summary>The original fixed Qwen language inventory is independent of installed system
/// voices and interface language. Selection/configuration can check capability without loading
/// weights or relabelling an unsupported detected language as another language.</summary>
public static class LocalSpeechLanguage
{
    public static string? Code(LearningLanguage language) => language switch
    {
        LearningLanguage.English => "en", LearningLanguage.Chinese or LearningLanguage.TraditionalChinese => "zh",
        LearningLanguage.Japanese => "ja", LearningLanguage.Korean => "ko", LearningLanguage.German => "de",
        LearningLanguage.French => "fr", LearningLanguage.Russian => "ru", LearningLanguage.Portuguese => "pt",
        LearningLanguage.Spanish => "es", LearningLanguage.Italian => "it", _ => null,
    };
}
