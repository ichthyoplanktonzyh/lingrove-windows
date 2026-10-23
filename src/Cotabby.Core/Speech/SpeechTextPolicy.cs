using System.Globalization;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>One recognized unit becomes bounded spoken text independently of playback or a
/// provider. Learning cards and review freshness share these original rules, so display ellipses,
/// dictionary lemmas and transport formatting cannot change what a clicked unit would pronounce.</summary>
public static class SpeechTextPolicy
{
    public const int MaximumLength = 1000;
    public static string? SpokenText(LearningUnit unit)
    {
        string candidate;
        switch (unit.Kind)
        {
            case LearningUnitKind.Word: case LearningUnitKind.Expression: candidate = unit.Surface; break;
            case LearningUnitKind.Pattern:
                if (unit.Words.Count == 0 || unit.Words.Any(word => word is null || word.Text is null || LearningExplanationText.Trim(word.Text).Length == 0)) return null;
                candidate = string.Join(" ", unit.Words.Select(word => word.Text)); break;
            default: return null;
        }
        return SanitizedText(candidate);
    }
    public static string? SanitizedText(string? text)
    {
        if (!LearningExplanationText.Fits(text, 8192)) return null;
        var safe = new StringBuilder(text!.Length);
        foreach (var scalar in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(scalar);
            // Foundation controlCharacters includes format controls. Its whitespace set also
            // includes historical U+200B, which survives this filter and is only edge-trimmed.
            if (category is not (UnicodeCategory.Control or UnicodeCategory.Format)
                || ScalarSets.IsWhitespaceOrNewline(scalar) || scalar.Value == 0x200B) safe.Append(scalar.ToString());
        }
        string collapsed = LearningExplanationText.Trim(string.Join(" ", GraphemeString.Words(safe.ToString())));
        return collapsed.Length == 0 ? null : new GraphemeString(collapsed).Prefix(MaximumLength);
    }
}
