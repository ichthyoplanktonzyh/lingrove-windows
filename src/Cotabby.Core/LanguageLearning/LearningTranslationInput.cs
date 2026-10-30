using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

public enum LearningReferenceMode { CurrentSentence, Draft }

/// <summary>
/// The exact committed sentence or explicit selection sent to translation. A current-sentence
/// capture can carry explicitly selected draft references; the model receives no live editor object and cannot derive
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
    public string ReferenceBefore { get; init; } = "";
    public string ReferenceAfter { get; init; } = "";

    /// <summary>Mac 3b extraction keeps the nearest 65,536 graphemes on each side. Reference
    /// changes what the model may read, never the separate editor range that can be replaced.</summary>
    public static LearningTranslationInput Captured(string value, LearningTextRange range, string source,
        LearningReferenceMode mode, LearningTranslationStyle? style = null)
    {
        var input = new LearningTranslationInput(source, style);
        if (mode != LearningReferenceMode.Draft || value is null || range.Location < 0 || range.Length < 0
            || range.Location > value.Length || range.Length > value.Length - range.Location) return input;
        return input with { ReferenceBefore = new GraphemeString(value[..range.Location]).Suffix(65_536),
            ReferenceAfter = new GraphemeString(value[(range.Location + range.Length)..]).Prefix(65_536) };
    }
}
