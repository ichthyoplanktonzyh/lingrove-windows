namespace Cotabby.Core.LanguageLearning;

/// <summary>Mac 3b LanguageLearningSettings uses the explicit explanation language, otherwise
/// the launch bundle's first localization as an exact LearningLanguage raw value, then English.
/// This does not normalize a region tag or inspect the system/translation destination.</summary>
public static class LearningExplanationLanguagePolicy
{
    public static LearningLanguage Resolve(LearningLanguage? selected, string? interfaceLocale) =>
        selected is { } value && Enum.IsDefined(value) ? value
        : interfaceLocale is not null && LearningLanguages.FromCode(interfaceLocale) is { } language ? language
        : LearningLanguage.English;
}
