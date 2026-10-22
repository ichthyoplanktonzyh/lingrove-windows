using System.Globalization;
using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Resolves the source direction selected by the user before a translation request is built. A
/// single eligible preference is explicit and does not need recognition; multiple choices require
/// confident scores from the platform detector. The resolver owns the stable ranking rule, not a
/// language model or heuristic classifier.
/// </summary>
public static class LearningSourceLanguageResolver
{
    public const int MaximumSampleGraphemeClusters = 600;
    public const double MinimumConfidence = 0.55;
    public const double MinimumConfidenceMargin = 0.15;

    /// <summary>
    /// Returns the explicit single source or the confidently recognized source among enabled
    /// choices. Empty text, a target-only list, uncertain scores, and detector failures resolve to
    /// null so callers do not guess a translation direction.
    /// </summary>
    public static LearningLanguage? Resolve(
        string text,
        IReadOnlyList<LearningLanguage> enabledSourceLanguages,
        LearningLanguage targetLanguage,
        ILearningLanguageDetector detector)
    {
        if (string.IsNullOrWhiteSpace(text) || enabledSourceLanguages is null ||
            !Enum.IsDefined(targetLanguage))
        {
            return null;
        }

        var trimmed = text.Trim();
        // Swift String is always valid Unicode. System.String can contain lone surrogates, so
        // reject those values instead of letting Rune/UTF-8 conversion replace source characters.
        if (!IsWellFormedUtf16(trimmed) || !ContainsLetter(trimmed)) return null;

        // Preserve the user’s configured order while dropping stale enum values, the target
        // itself, and duplicate values that can survive settings migration.
        var seen = new HashSet<LearningLanguage>();
        var candidates = new List<LearningLanguage>();
        foreach (var language in enabledSourceLanguages)
        {
            if (!Enum.IsDefined(language) || language == targetLanguage || !seen.Add(language)) continue;
            candidates.Add(language);
        }

        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        if (detector is null) return null;

        var sample = SuffixByTextElements(trimmed, MaximumSampleGraphemeClusters);
        var orderedCandidates = Array.AsReadOnly(candidates.ToArray());
        IReadOnlyDictionary<LearningLanguage, double> detectorScores;
        try
        {
            detectorScores = detector.Scores(sample, orderedCandidates);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // OS recognition can fail independently of capture. Preserve that uncertainty as no
            // result and never substitute a script or keyboard-layout heuristic.
            return null;
        }

        if (detectorScores is null) return null;

        // Missing hypotheses are zero-confidence alternatives, matching the Mac resolver's
        // constrained candidate ranking rather than allowing an absent language to disappear.
        var scores = new Dictionary<LearningLanguage, double>(orderedCandidates.Count);
        foreach (var candidate in orderedCandidates)
            scores[candidate] = detectorScores.TryGetValue(candidate, out var score) ? score : 0;

        return ConfidentLanguage(scores);
    }

    /// <summary>Applies the shared confidence floor and lead over the runner-up.</summary>
    public static LearningLanguage? ConfidentLanguage(
        IReadOnlyDictionary<LearningLanguage, double> scores)
    {
        if (scores is null) return null;

        var ranked = scores
            .Where(pair => double.IsFinite(pair.Value))
            .OrderByDescending(pair => pair.Value)
            .ToArray();
        if (ranked.Length == 0 || ranked[0].Value < MinimumConfidence) return null;
        if (ranked.Length > 1 && ranked[0].Value - ranked[1].Value < MinimumConfidenceMargin) return null;
        return ranked[0].Key;
    }

    private static bool ContainsLetter(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsLetter(rune)) return true;
        }

        return false;
    }

    private static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return false;
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string SuffixByTextElements(string value, int maximumTextElements)
    {
        var starts = StringInfo.ParseCombiningCharacters(value);
        if (starts.Length <= maximumTextElements) return value;
        return value[starts[starts.Length - maximumTextElements]..];
    }
}
