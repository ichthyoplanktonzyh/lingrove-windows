using LearningExplanationRecord = Cotabby.Core.LanguageLearning.LearningExplanationRecord;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using F = Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.Vocabulary;

/// <summary>Transient projection of canonical evidence; dates and durable IDs stay in the Store.</summary>
internal static class CanonicalNotebookTargetPolicy
{
    internal static string Key(F.VocabularyExample? value) => value?.Id.ToString("D").ToUpperInvariant() ?? "standalone";
    internal static string DocumentId(F.VocabularyEntry entry) => entry.Id.ToString("D").ToUpperInvariant();
    internal static F.VocabularySense? SelectedSense(F.VocabularyEntry? entry, Guid? id) => entry is null ? null :
        entry.Senses.FirstOrDefault(s => s.Id == id || id is { } value && s.MergedFrom.Contains(value)) ?? entry.Senses.FirstOrDefault();
    internal static string DictionaryQuery(F.VocabularyEntry entry) => entry.DictionaryForm.Length == 0 ? entry.Expression : entry.DictionaryForm;
    internal static bool SameQuery(string a, string b) => CanonicalStringComparer.Instance.Equals(a, b);
    internal static bool DictionaryChanged(F.VocabularyEntry a, F.VocabularyEntry b) => a.Language != b.Language || !SameQuery(a.DictionaryForm, b.DictionaryForm);
    internal static LearningUnit DictionaryUnit(F.VocabularyEntry entry, IReadOnlyList<LearningDictionaryWord> words)
    {
        var q = DictionaryQuery(entry); return new(0, q, q, entry.Kind == VocabularyEntryKind.Word ? LearningUnitKind.Word : LearningUnitKind.Expression, [new(0, q.Length)], words);
    }
    internal static LearningExplanationInput? Input(F.VocabularyEntry entry, F.VocabularySense sense, F.VocabularyExample? example)
    {
        var language = entry.Language;
        string? identity = example is not null ? ExampleKey(example) : sense.CreatedWithoutContext == true && !sense.IsSavedWithoutContext ? "sense:" + sense.Id.ToString("D").ToUpperInvariant() : null;
        return new(entry.Expression, entry.DictionaryForm, language, entry.Kind, example?.Text ?? entry.Expression,
            example?.Ranges ?? [new VocabularyTextRange(0, entry.Expression.Length)], example is null ? LearningExplanationContext.None :
            example.Completeness switch { R.VocabularyExampleCompleteness.Sentence => LearningExplanationContext.Sentence, R.VocabularyExampleCompleteness.Fragment => LearningExplanationContext.Fragment, _ => LearningExplanationContext.Unknown }, identity);
    }
    private static string ExampleKey(F.VocabularyExample example)
    {
        string ranges = string.Join(",", example.Ranges.Select(x => x.Location.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + x.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        string kind = example.Completeness switch { R.VocabularyExampleCompleteness.Sentence => "sentence", R.VocabularyExampleCompleteness.Fragment => "fragment", _ => "unknown" };
        var parts = new[] { example.Text, ranges, kind };
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Concat(parts.Select(x => System.Text.Encoding.UTF8.GetByteCount(x).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + x)));
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }
    internal static LearningExplanationRecord? ToLive(F.LearningExplanationRecord value)
    {
        try { return CanonicalWritingPolicy.ToLive(value); } catch (Exception) { return null; }
    }
}
