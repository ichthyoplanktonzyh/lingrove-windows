using System.Globalization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>Resolves an explicit speaker action from bounded phrase/context evidence, then its
/// card hint. The speech controller supplies a fresh unconstrained recognizer for each action;
/// views never recognize while rendering. Unlike translation's constrained detector, this seam
/// retains unsupported language codes so confident unsupported text resolves to null.</summary>
public static class SpeechLanguageResolver
{
    public const int MaximumSampleLength = 600;

    public static LearningLanguage? Resolve(string text, LearningLanguage? hint,
        Func<string, IReadOnlyDictionary<string, double>> recognize, string? context = null)
    {
        if (ConfidentLanguage(text, recognize) is { } language) return LearningLanguages.FromCode(language);
        if (context is not null && ConfidentLanguage(context, recognize) is { } contextual) return LearningLanguages.FromCode(contextual);
        return hint;
    }

    private static string? ConfidentLanguage(string text, Func<string, IReadOnlyDictionary<string, double>> recognize)
    {
        // Swift prefix counts extended graphemes. Stop scanning at the bound instead of
        // allocating a segmentation index for an otherwise unbounded context string.
        int end = 0;
        for (int count = 0; count < MaximumSampleLength && end < text.Length; count++)
            end += StringInfo.GetNextTextElementLength(text.AsSpan(end));
        string sample = text[..end];
        if (!HasEnoughEvidence(sample)) return null;
        var ranked = recognize(sample).Where(p => double.IsFinite(p.Value) && p.Value >= 0 && p.Value <= 1)
            .OrderByDescending(p => p.Value).ToArray();
        if (ranked.Length == 0 || ranked[0].Value < 0.75
            || ranked.Length > 1 && ranked[0].Value - ranked[1].Value < 0.20) return null;
        return ranked[0].Key;
    }

    private static bool HasEnoughEvidence(string sample)
    {
        int letters = 0; bool hasNonLatinLetters = false;
        foreach (var scalar in sample.EnumerateRunes())
        {
            // Foundation's letters and Swift's Character.isLetter use different Unicode
            // predicates/versions. Keep their measured sets separate; UTF-16 unit counts and
            // current platform categories would both change the source's evidence threshold.
            if (!SpeechLanguageEvidence.IsFoundationLetter(scalar.Value)) continue;
            letters++; hasNonLatinLetters |= scalar.Value > 0x024F;
        }
        if (letters < 4) return false;
        if (letters < 8) return false;
        if (hasNonLatinLetters) return true;
        // Character.isLetter classifies the first scalar of each grapheme, rather than any
        // contained scalar: a Prepend character before A must still split the word here.
        return new GraphemeString(sample).Split(g => !SpeechLanguageEvidence.IsWordLetter(g.Scalar.Value)).Count >= 3;
    }
}
