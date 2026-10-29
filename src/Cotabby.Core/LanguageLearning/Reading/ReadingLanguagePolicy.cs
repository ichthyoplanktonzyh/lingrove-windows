using System.Text;
using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning.Reading;

public enum ReadingLanguageEvidence { Selection, Context, Hint, Unavailable }
public sealed record ReadingLanguageResolution(LearningLanguage? Language, ReadingLanguageEvidence Evidence);
public sealed record ReadingLanguageHypothesis(string Code, double Probability);

/// <summary>Platform supplies language hypotheses and script membership; it must not constrain the
/// detector to the supported palette, since unsupported languages retain unknown identity.</summary>
public interface IReadingLanguageEvidence
{
    IReadOnlyList<ReadingLanguageHypothesis> Recognize(string sample);
    bool HasNonLatinLetter(string sample);
    bool IsCompatible(string sample, string languageCode);
}

public static class ReadingLanguagePolicy
{
    public static ReadingLanguageResolution Resolve(string text, string? context, LearningLanguage? hint, IReadingLanguageEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        string Sample(string value) => new GraphemeString(FoundationText.TrimWhitespaceAndNewlines(value)).Prefix(600);
        var sample = Sample(text);
        if (!new GraphemeString(sample).Any(g => g.IsLetter)) return new(null, ReadingLanguageEvidence.Unavailable);
        var selected = Confident(sample, evidence);
        if (Independent(sample, evidence) && selected is not null) return Result(selected, ReadingLanguageEvidence.Selection);
        if (context is not null)
        {
            var surrounding = Sample(context);
            if (!CanonicalStringComparer.Instance.Equals(surrounding, sample) && Confident(surrounding, evidence) is { } detected
                && evidence.IsCompatible(sample, detected)) return Result(detected, ReadingLanguageEvidence.Context);
        }
        if (hint is { } language && evidence.IsCompatible(sample, language.Code())) return new(language, ReadingLanguageEvidence.Hint);
        return selected is null ? new(null, ReadingLanguageEvidence.Unavailable) : Result(selected, ReadingLanguageEvidence.Selection);
    }

    private static ReadingLanguageResolution Result(string code, ReadingLanguageEvidence origin) => new(LearningLanguages.FromCode(code), origin);
    private static string? Confident(string sample, IReadingLanguageEvidence evidence)
    {
        var ranked = evidence.Recognize(sample).Where(h => double.IsFinite(h.Probability) && h.Probability is >= 0 and <= 1)
            .OrderByDescending(h => h.Probability).Take(2).ToArray();
        return ranked.Length == 0 || ranked[0].Probability < 0.55 ||
            (ranked.Length > 1 && ranked[0].Probability - ranked[1].Probability < 0.15) ? null : ranked[0].Code;
    }
    private static bool Independent(string sample, IReadingLanguageEvidence evidence)
    {
        int letters = sample.EnumerateRunes().Count(Rune.IsLetter), words = 0; bool inWord = false;
        foreach (var g in new GraphemeString(sample))
        {
            if (g.IsLetter) { if (!inWord) words++; inWord = true; } else inWord = false;
        }
        return letters >= 8 && (words >= 3 || evidence.HasNonLatinLetter(sample));
    }
}
