using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Pure target projections from Mac VocabularyNotebookSession. A window-owned session controls
/// lifetime; these values preserve per-usage keys, alias/fallback selection and a headword-only
/// dictionary unit. Native word segmentation is supplied separately, never guessed or normalized.
/// </summary>
public static class VocabularyNotebookTargetPolicy
{
    public static string Key(VocabularyExample? example) => example?.Id.ToString("D").ToUpperInvariant() ?? "standalone";
    public static string DocumentId(VocabularyEntry entry) => entry.Id.ToString("D").ToUpperInvariant();
    public static VocabularySense? SelectedSense(VocabularyEntry? entry, Guid? senseId) => entry is null ? null
        : senseId is { } id && VocabularySensePolicy.Index(id, entry) is { } index ? entry.Senses[index] : entry.Senses.FirstOrDefault();
    public static string DictionaryQuery(VocabularyEntry entry) => entry.DictionaryForm.Length == 0 ? entry.Expression : entry.DictionaryForm;
    public static bool SameQuery(string first, string second) => CanonicalStringComparer.Instance.Equals(first, second);
    public static bool DictionaryChanged(VocabularyEntry remembered, VocabularyEntry current) => remembered.Language != current.Language
        || !SameQuery(remembered.DictionaryForm, current.DictionaryForm);
    public static LearningUnit DictionaryUnit(VocabularyEntry entry, IReadOnlyList<LearningDictionaryWord> words)
    {
        string query = DictionaryQuery(entry);
        return new(0, query, query, entry.Kind == VocabularyEntryKind.Word ? LearningUnitKind.Word : LearningUnitKind.Expression,
            [new(0, query.Length)], words);
    }
}
