using System.Text.RegularExpressions;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Category translations use interface locale tags, independent of study language.</summary>
public static partial class VocabularyCategoryLocalizationPolicy
{
    public sealed record Presentation(string Name, string Definition, string ScopeNote);
    public static string Normalize(string identifier) => identifier.Trim().Replace('_', '-').ToLowerInvariant();
    [GeneratedRegex("^[a-z]{2,8}(-[a-z0-9]{1,8})*$", RegexOptions.CultureInvariant)] private static partial Regex TagPattern();
    public static bool ValidLanguageTag(string identifier) { var tag = Normalize(identifier); return tag.Length > 0 && tag.Length <= 100 && TagPattern().IsMatch(tag); }
    public static IReadOnlyList<string> Candidates(IReadOnlyList<string> identifiers)
    {
        var result = new List<string>(); if (identifiers.Count > 0) { var tag = Normalize(identifiers[0]); if (tag.Length > 0) { result.Add(tag); var parts = tag.Split('-'); if (parts[0] == "zh") { var normalized = Chinese(parts); if (normalized is not null) result.Add(normalized); } else result.Add(parts[0]); } } result.Add("en"); return result.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static string? Chinese(string[] parts) => parts.Contains("hans") ? "zh-hans" : parts.Contains("hant") ? "zh-hant" : parts.Contains("cn") || parts.Contains("sg") || parts.Contains("my") ? "zh-hans" : parts.Contains("tw") || parts.Contains("hk") || parts.Contains("mo") ? "zh-hant" : null;
    private static LearningLanguage? StudyLanguage(string identifier) { var parts = Normalize(identifier).Split('-'); if (parts[0] == "zh") return Chinese(parts) switch { "zh-hans" => LearningLanguage.Chinese, "zh-hant" => LearningLanguage.TraditionalChinese, _ => null }; return LearningLanguages.FromCode(parts[0]); }
    public static string EditingLanguage(string identifier)
    {
        var tag = Normalize(identifier); if (tag.Length == 0) return "en"; var parts = tag.Split('-');
        if (parts[0] == "zh") return Chinese(parts) switch { "zh-hans" => "zh-Hans", "zh-hant" => "zh-Hant", _ => identifier.Replace('_', '-') };
        if (parts[0] == "pt" && parts.Contains("br")) return "pt-BR";
        string[] supported = ["en", "de", "es", "fr", "it", "ja", "ko", "ru", "ar", "hi", "id", "tr"]; return supported.Contains(parts[0]) ? parts[0] : identifier.Replace('_', '-');
    }
    public static Presentation Resolve(VocabularyCategory category, IReadOnlyList<string> identifiers)
    {
        var candidates = Candidates(identifiers); var rows = category.Localizations.ToLookup(x => Normalize(x.Language), StringComparer.Ordinal);
        string ResolveField(Func<VocabularyCategoryLocalization, string?> field, string fallback) { foreach (var tag in candidates) { var row = rows[tag].FirstOrDefault(); if (row is not null && field(row) is { } value) return value; } return fallback; }
        var labels = category.Labels; VocabularyMeaningLabel? fallback = null; foreach (var tag in candidates) { var language = StudyLanguage(tag); if (language is not null) { fallback = labels.FirstOrDefault(x => x.Language == language && x.Preferred) ?? labels.FirstOrDefault(x => x.Language == language); if (fallback is not null) break; } } fallback ??= labels.FirstOrDefault(x => x.Language is null && x.Preferred) ?? labels.FirstOrDefault(x => x.Language is null) ?? labels.FirstOrDefault(x => x.Language == LearningLanguage.English && x.Preferred) ?? labels.FirstOrDefault(x => x.Language == LearningLanguage.English) ?? labels.FirstOrDefault(x => x.Preferred) ?? labels.FirstOrDefault();
        return new(ResolveField(x => x.Name, fallback?.Text ?? ""), ResolveField(x => x.Definition, category.Definition), ResolveField(x => x.ScopeNote, category.ScopeNote));
    }
    public static IReadOnlyList<VocabularyCategoryLocalization> Overlay(VocabularyCategory category, VocabularyCategoryOverride overlay)
    {
        var rows = category.Localizations.Select(x => x with { Definition = overlay.Definition is null ? x.Definition : null, ScopeNote = overlay.ScopeNote is null ? x.ScopeNote : null, Name = overlay.Labels?.Any(l => l.Language is null) == true ? null : x.Name }).ToList();
        if (overlay.Labels is { } labels) foreach (var group in labels.Where(x => x.Language is not null).GroupBy(x => x.Language)) { var selected = group.FirstOrDefault(x => x.Preferred) ?? group.First(); Merge(rows, new(group.Key!.Value.Code(), Name: selected.Text)); }
        foreach (var value in overlay.Localizations ?? []) Merge(rows, value); return rows;
    }
    public static void Merge(List<VocabularyCategoryLocalization> rows, VocabularyCategoryLocalization value)
    {
        var index = rows.FindIndex(x => Normalize(x.Language) == Normalize(value.Language)); if (index < 0) rows.Add(value); else { var old = rows[index]; rows[index] = old with { Name = value.Name ?? old.Name, Definition = value.Definition ?? old.Definition, ScopeNote = value.ScopeNote ?? old.ScopeNote }; }
    }
    public static (VocabularyCategory Category, VocabularyCategoryOverride? Overlay) Edit(VocabularyCategory category, VocabularyClassificationIndex index, string language, string name, string definition, bool hidden, bool restoringDefaults = false)
    {
        name = name.Trim();
        if (!index.Categories.Any(x => x.Id == category.Id))
        {
            var labels = category.Labels.ToArray(); labels[0] = labels[0] with { Text = name, Language = StudyLanguage(language) };
            return (category with { Labels = labels, Definition = definition, Localizations = [new(language, name, definition)] }, hidden ? new(category.Id) { IsHidden = true } : null);
        }
        var overlay = restoringDefaults ? new VocabularyCategoryOverride(category.Id) : index.Overrides.FirstOrDefault(x => x.CategoryId == category.Id) ?? new(category.Id);
        var effective = restoringDefaults ? category : VocabularyClassificationPolicy.Effective(category, index); var before = Resolve(effective, [language]);
        var changed = new VocabularyCategoryLocalization(language, CanonicalStringComparer.Instance.Equals(name, before.Name) ? null : name, CanonicalStringComparer.Instance.Equals(definition, before.Definition) ? null : definition);
        if (changed.Name is not null || changed.Definition is not null) { var rows = (overlay.Localizations ?? []).ToList(); Merge(rows, changed); overlay = overlay with { Localizations = rows }; }
        overlay = overlay with { IsHidden = hidden };
        return (category, overlay.IsHidden || overlay.Labels is not null || overlay.Definition is not null || overlay.Examples is not null || overlay.ScopeNote is not null || overlay.References is not null || overlay.Localizations?.Count > 0 ? overlay : null);
    }
    public static void Validate(IReadOnlyList<VocabularyCategoryLocalization> values)
    {
        if (values.Count > 64) throw VocabularyClassificationPolicy.Invalid(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values) if (value is null || !ValidLanguageTag(value.Language) || !seen.Add(Normalize(value.Language)) || value.Name is null && value.Definition is null && value.ScopeNote is null || value.Name is { } name && (string.IsNullOrWhiteSpace(name) || !VocabularyClassificationPolicy.Fits(name, 4096)) || value.Definition is { } definition && !VocabularyClassificationPolicy.Fits(definition, 16384) || value.ScopeNote is { } note && !VocabularyClassificationPolicy.Fits(note, 16384)) throw VocabularyClassificationPolicy.Invalid();
    }
}
