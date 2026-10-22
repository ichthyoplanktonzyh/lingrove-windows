namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// The exact committed sentence or explicit selection sent to translation. A current-sentence
/// capture carries no surrounding draft; the model receives no live editor object and cannot derive
/// a write range from its output. The preview coordinator owns this request-local value.
/// </summary>
public sealed record LearningTranslationInput
{
    public LearningTranslationInput(string source, LearningTranslationStyle? style = null)
    {
        Source = source;
        Style = style ?? new LearningTranslationStyle();
    }

    public string Source { get; init; }

    public LearningTranslationStyle Style { get; init; }
}
