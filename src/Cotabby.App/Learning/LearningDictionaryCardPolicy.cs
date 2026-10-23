using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Learning;

/// <summary>
/// Non-WPF presentation of Mac LearningDictionaryCard/contextual example. The view borrows
/// literal slices and labels; target/session/editor authority stays with its existing owners.
/// No dictionary definition, generated meaning or normalized spelling replaces captured text.
/// </summary>
internal static class LearningDictionaryCardPolicy
{
    public static string ContextualLabel(LearningExplanationInput? input) =>
        input?.ContextKind == LearningExplanationContext.None ? "Meaning" : "Contextual meaning";

    public static IReadOnlyList<LearningContextTextRun> ContextRuns(LearningExplanationInput? input)
    {
        if (input is null || input.ContextKind == LearningExplanationContext.None || !LearningExplanationPolicy.IsValid(input)) return [];
        string text = input.Context;
        var points = new SortedSet<int> { 0, text.Length };
        foreach (var range in input.Ranges) { points.Add(range.Location); points.Add(range.Location + range.Length); }
        var cuts = points.ToArray(); var runs = new List<LearningContextTextRun>();
        for (int i = 1; i < cuts.Length; ++i)
        {
            int start = cuts[i - 1], end = cuts[i];
            bool target = input.Ranges.Any(range => range.Location <= start && range.Location + range.Length >= end);
            runs.Add(new(text.Substring(start, end - start), target));
        }
        return runs.AsReadOnly();
    }

    /// <summary>
    /// Mac shows the reverse translation only when the captured example is the whole translated
    /// sentence. This UI-only projection never places that original source in explanation input.
    /// </summary>
    public static LearningContextExampleTranslation? ExampleTranslation(LearningExplanationInput? input, LearningPreviewState? translation)
    {
        if (input is null || input.ContextKind == LearningExplanationContext.None || !LearningExplanationPolicy.IsValid(input)
            || !LearningExplanationCardPolicy.SameDocument(translation, translation)
            || input.Language != translation!.Options!.TargetLanguage
            || !string.Equals(input.Context, translation.Result, StringComparison.Ordinal)) return null;
        return new(translation.Source, translation.Options.SourceLanguage);
    }
}

/// <summary>A plain immutable context slice; its original UTF-16 text is never normalized.</summary>
internal sealed record LearningContextTextRun(string Text, bool IsTarget);

/// <summary>UI-only reciprocal sentence text and direction, borrowed from the frozen preview.</summary>
internal sealed record LearningContextExampleTranslation(string Text, LearningLanguage Language);
