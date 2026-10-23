using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Mac notebook beginExport/selections over a single offered browse snapshot. The window supplies
/// the full acknowledged graph, its visible projection and transient batch IDs; the request freezes
/// all three lists for the sheet. This owns no filtering, alias resolution, archive read or save.
/// </summary>
public static class VocabularyExportOfferPolicy
{
    public static VocabularyExportRequest Request(IReadOnlyList<VocabularyEntry> entries, IReadOnlyList<VocabularyEntry> visible,
        IReadOnlySet<VocabularySelection> batchSelection, bool newestFirst = true, string query = "", LearningLanguage? language = null,
        string selectedTag = "", VocabularyExportScope? scope = null, VocabularySelection? single = null)
    {
        var ordered = newestFirst ? entries.OrderByDescending(entry => entry.UpdatedAt) : entries.OrderBy(entry => entry.UpdatedAt);
        var all = Selections(ordered);
        var selected = single is { } receipt ? new[] { receipt } : all.Where(batchSelection.Contains).ToArray();
        var initial = single is not null ? VocabularyExportScope.Selected : scope ?? (selected.Length > 0 ? VocabularyExportScope.Selected :
            query.Length == 0 && language is null && selectedTag.Length == 0 ? VocabularyExportScope.All : VocabularyExportScope.Filtered);
        return new(all, Selections(visible), selected, initial);
    }
    private static VocabularySelection[] Selections(IEnumerable<VocabularyEntry> entries) => entries
        .SelectMany(entry => entry.Senses.Select(sense => new VocabularySelection(entry.Id, sense.Id))).ToArray();
}
