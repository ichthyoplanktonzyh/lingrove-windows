using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Vocabulary.Notebook;

/// <summary>Display-only selection of existing labels. No language is inferred for authored text,
/// and label identity remains available to an editor. Study-language filtering is independent.</summary>
public static class VocabularyMeaningLabelPolicy
{
    public static IReadOnlyList<LearningLanguage> PreferredLanguages(LearningLanguage? explanation, IEnumerable<string> interfaceIdentifiers)
    {
        var result = new List<LearningLanguage>();
        if (explanation is { } preferred) result.Add(preferred);
        foreach (string identifier in interfaceIdentifiers)
            if (Language(identifier) is { } value && !result.Contains(value)) result.Add(value);
        return result.AsReadOnly();
    }

    public static LearningLanguage? Language(string identifier)
    {
        string[] parts = identifier.Replace('_', '-').ToLowerInvariant().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        if (parts[0] == "zh")
        {
            if (parts.Contains("hans")) return LearningLanguage.Chinese;
            if (parts.Contains("hant")) return LearningLanguage.TraditionalChinese;
            if (parts.Any(p => p is "cn" or "sg" or "my")) return LearningLanguage.Chinese;
            if (parts.Any(p => p is "tw" or "hk" or "mo")) return LearningLanguage.TraditionalChinese;
            return null;
        }
        return LearningLanguages.FromCode(parts[0]);
    }

    public static VocabularyMeaningLabel? Label(IReadOnlyList<VocabularyMeaningLabel> labels, IReadOnlyList<LearningLanguage> languages)
    {
        foreach (var language in languages)
            if (Preferred(labels.Where(label => label.Language == language)) is { } selected) return selected;
        return Preferred(labels.Where(label => label.Language is null))
            ?? Preferred(labels.Where(label => label.Language == LearningLanguage.English)) ?? Preferred(labels);
    }

    public static string Title(F.VocabularyMeaningNode node, IReadOnlyList<LearningLanguage> languages) => Label(node.Labels, languages)?.Text ?? "";

    public static bool HasMultipleLanguages(IReadOnlyList<F.VocabularyEntry> entries) => entries.Select(entry => entry.Language).Distinct().Skip(1).Any();

    /// <summary>The presentation adapter supplies localized compact language names, including unspecified.</summary>
    public static string ExpressionTitle(F.VocabularyEntry entry, bool includeLanguage, Func<LearningLanguage?, string> compactLabel) =>
        includeLanguage ? entry.Expression + " · " + compactLabel(entry.Language) : entry.Expression;

    private static VocabularyMeaningLabel? Preferred(IEnumerable<VocabularyMeaningLabel> labels)
    {
        var values = labels.ToArray();
        return values.FirstOrDefault(label => label.Preferred) ?? values.FirstOrDefault();
    }
}
