using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Read-only projections from Mac VocabularyPolicy.matches, sense displayTitle and notebook view.
/// The window owns filters/selection; these helpers never edit the archive. A tag-filtered entry
/// is a browsing projection and must never become an update baseline. Stable IDs resolve aliases.
/// Platform linguistic substring comparison is explicit, independent of durable identity keys.
/// </summary>
public static class VocabularyNotebookPolicy
{
    public static bool Matches(VocabularyEntry entry, string query, IVocabularyTextSearching textSearch)
    {
        var fields = Fields(entry).ToArray();
        return GraphemeString.Words(query).All(term => fields.Any(field => textSearch.Contains(field, term)));
    }
    public static IReadOnlyList<VocabularyEntry> VisibleEntries(VocabularyDocument document, IVocabularyTextSearching textSearch, string query = "", LearningLanguage? language = null,
        string tag = "", bool newestFirst = true, CancellationToken cancellationToken = default)
    {
        var values = document.Entries.Where(entry => { cancellationToken.ThrowIfCancellationRequested(); return language is null || entry.Language == language; })
            .Select(entry => tag.Length == 0 ? entry : entry with { Senses = entry.Senses.Where(sense => sense.Tags.Contains(tag, CanonicalStringComparer.Instance)).ToArray() })
            .Where(entry => entry.Senses.Count > 0 && Matches(entry, query, textSearch));
        return CollectionValues.List(newestFirst ? values.OrderByDescending(entry => entry.UpdatedAt) : values.OrderBy(entry => entry.UpdatedAt));
    }
    public static IReadOnlyList<string> Tags(VocabularyDocument document) => CollectionValues.List(document.Entries.SelectMany(entry => entry.Senses)
        .SelectMany(sense => sense.Tags).Distinct(CanonicalStringComparer.Instance).Order(CanonicalStringComparer.Instance));
    public static string ResolveTag(IReadOnlyList<string> tags, string requested) => tags.FirstOrDefault(tag => CanonicalStringComparer.Instance.Equals(tag, requested)) ?? "";
    /// <summary>Saved examples may exceed model-input limits; highlighting uses their own valid original ranges.</summary>
    public static IReadOnlyList<VocabularyUsageTextRun> UsageRuns(VocabularyExample example)
    {
        var boundaries = FoundationTextRanges.Boundaries(example.Text); var events = new SortedDictionary<int, int> { [0] = 0, [example.Text.Length] = 0 };
        foreach (var range in example.Ranges.Where(range => FoundationTextRanges.IsValid(range.Location, range.Length, example.Text, boundaries)))
        {
            events[range.Location] = events.GetValueOrDefault(range.Location) + 1;
            int end = range.Location + range.Length; events[end] = events.GetValueOrDefault(end) - 1;
        }
        var result = new List<VocabularyUsageTextRun>(); int coverage = 0, previous = 0, start = 0; bool? target = null;
        // Sweep endpoints once. Overlapping/adjacent highlighted ranges coalesce without an
        // O(ranges²) search or repeated copying of a long saved example.
        foreach (var point in events)
        {
            if (point.Key > previous)
            {
                bool next = coverage > 0;
                if (target is { } before && before != next) { result.Add(new(example.Text[start..previous], before)); start = previous; }
                target = next;
            }
            coverage += point.Value; previous = point.Key;
        }
        if (target is { } last) result.Add(new(example.Text[start..], last));
        return CollectionValues.List(result);
    }
    public static VocabularySelection? ResolveSelection(VocabularyDocument document, Guid? senseId)
    {
        if (senseId is not { } id) return null;
        foreach (var entry in document.Entries)
            if (VocabularySensePolicy.Index(id, entry) is { } index) return new(entry.Id, entry.Senses[index].Id);
        return null;
    }
    public static string DisplayTitle(VocabularySense sense)
    {
        if (sense.Meaning.Length > 0) return sense.Meaning;
        foreach (var block in sense.Content ?? [])
            if (block.Content is VocabularyBlockContent.Text text && LearningExplanationText.Trim(text.Value.Text).Length > 0) return text.Value.Text;
        if (sense.Examples.FirstOrDefault(example => example.Meaning.Length > 0) is { } usage) return usage.Meaning;
        return VocabularyContentPolicy.Images(sense.Content ?? []).Count > 0 ? "Image explanation" : "Meaning pending";
    }
    private static IEnumerable<string> Fields(VocabularyEntry entry)
    {
        yield return entry.Expression; yield return entry.DictionaryForm;
        foreach (var sense in entry.Senses)
        {
            yield return sense.Meaning; yield return sense.Note;
            foreach (string tag in sense.Tags) yield return tag;
            foreach (var meaning in sense.StandaloneMeanings.Concat(sense.AlternateMeanings)) yield return meaning.Text;
            foreach (var block in sense.Content ?? [])
                yield return block.Content switch { VocabularyBlockContent.Text text => text.Value.Text, VocabularyBlockContent.Image image => image.Caption, _ => "" };
            foreach (var usage in sense.Examples)
            {
                yield return usage.Text; yield return usage.Translation; yield return usage.Meaning;
                foreach (var variant in usage.TranslationVariants) yield return variant.Text;
            }
        }
    }
}

/// <summary>One transient literal slice for saved-usage rendering; no normalized text or model budget enters it.</summary>
public sealed record VocabularyUsageTextRun(string Text, bool IsTarget);
