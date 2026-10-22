namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Immutable settings snapshot for one translation operation. The preview coordinator owns the
/// durable settings; it passes this value to the factory, which freezes the rendered prompt and
/// output reserve before any asynchronous engine work begins.
/// </summary>
public sealed record LearningTranslationOptions
{
    /// <summary>Configured source direction used when no separate language detector is available.</summary>
    public LearningLanguage SourceLanguage { get; init; } = LearningLanguage.Chinese;

    /// <summary>The language requested from the translator.</summary>
    public LearningLanguage TargetLanguage { get; init; } = LearningLanguage.English;

    /// <summary>Expression preference captured for this operation.</summary>
    public LearningTranslationStyle Style { get; init; } = new();

    /// <summary>Selected local or endpoint model name; recognized specialists choose their own template.</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>Configured context window (8,192-token Mac fallback), validated against the endpoint ceiling.</summary>
    public int ContextTokens { get; init; } = 8_192;
}
