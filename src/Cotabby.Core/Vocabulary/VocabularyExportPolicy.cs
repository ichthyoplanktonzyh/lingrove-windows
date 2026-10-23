using System.Collections.Frozen;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Mac's pure export selection/projection over an immutable notebook snapshot. The archive
/// calls selection before collecting media; formatters consume only the reduced document. The
/// package factory composes those pure encoders. No file reads, review mutation or UI publication
/// belongs to this boundary.
/// </summary>
public static class VocabularyExportPolicy
{
    public const int MaximumDataBytes = 64 * 1024 * 1024;
    public static IReadOnlyList<VocabularyEntry> Selected(IReadOnlyList<VocabularyEntry> entries,
        IReadOnlyList<VocabularySelection> selections, CancellationToken cancellationToken = default)
    {
        if (selections.Count == 0) throw new VocabularyExportException(VocabularyExportFailure.Empty);
        var source = entries.GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.ToArray());
        var seen = new HashSet<VocabularySelection>(); var result = new List<VocabularyEntry>();
        foreach (var selection in selections)
        {
            if (!seen.Add(selection)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            if (!source.TryGetValue(selection.EntryId, out var matches) || matches.Length != 1
                || matches[0].Senses.FirstOrDefault(sense => sense.Id == selection.SenseId) is not { } sense)
                throw new VocabularyExportException(VocabularyExportFailure.Changed);
            int index = result.FindIndex(entry => entry.Id == selection.EntryId);
            // Exact IDs intentionally do not use the notebook's mergedFrom alias resolver: a
            // concurrent merge must make a stale export scope fail, never broaden its meaning.
            if (index >= 0) result[index] = result[index] with { Senses = result[index].Senses.Append(sense).ToArray() };
            else result.Add(matches[0] with { Senses = [sense] });
        }
        return CollectionValues.List(result);
    }
    public static VocabularyExportDocument Document(IReadOnlyList<VocabularyEntry> entries, VocabularyExportConfiguration options) =>
        new(entries.Select(entry => new VocabularyExportDocument.Entry(entry.Id, entry.Expression, entry.DictionaryForm, entry.Language,
            entry.Kind, entry.Senses.Select(sense => ProjectSense(sense, options)))));
    public static IReadOnlySet<VocabularyImageReference> Images(IReadOnlyList<VocabularyEntry> entries, VocabularyExportConfiguration options) =>
        (options.IncludesImages ? entries.SelectMany(entry => entry.Senses).SelectMany(sense => VocabularyContentPolicy.Images(sense.Content ?? []))
            : Enumerable.Empty<VocabularyImageReference>()).ToFrozenSet();

    /// <summary>Prepare one value-owned payload without media or storage effects. This retains the
    /// Mac initial cancellation check separately from each renderer's loop checkpoints.</summary>
    public static VocabularyExportPackage Package(IReadOnlyList<VocabularyEntry> entries, VocabularyExportConfiguration options,
        VocabularyExportTextLabels? labels = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Swift arrays are value snapshots. IReadOnlyList alone may wrap a mutable List, so
        // detach its immutable entry references before deriving both payload and counts.
        var capturedEntries = CollectionValues.List(entries);
        var document = Document(capturedEntries, options);
        byte[] data = VocabularyExportFormatter.Render(document, options, labels, cancellationToken);
        if (data.Length > MaximumDataBytes) throw new VocabularyExportException(VocabularyExportFailure.TooLarge);
        return new(options, data, VocabularyExportFormatter.Preview(data, options.Format), capturedEntries.Count,
            capturedEntries.Sum(entry => (long)entry.Senses.Count), options.Examples && !options.IsExpressionList
                ? capturedEntries.Sum(entry => entry.Senses.Sum(sense => (long)sense.Examples.Count)) : 0);
    }

    private static VocabularyExportDocument.Sense ProjectSense(VocabularySense sense, VocabularyExportConfiguration options) =>
        new(sense.Id, sense.CreatedAt,
            options.Meanings ? sense.Meaning : null, options.Meanings ? sense.MeaningLanguage : null,
            options.Meanings ? OtherValues(sense.Meaning, sense.MeaningLanguage, sense.StandaloneMeanings.Concat(sense.AlternateMeanings)) : null,
            options.Notes ? sense.Note : null, options.Notes ? sense.Tags : null,
            options.Examples ? sense.Examples.Select(usage => ProjectUsage(usage, options)) : null,
            options.Explanation ? Explanations(sense.Explanations) : null,
            options.Content ? (sense.Content ?? []).Select(block => ProjectBlock(block, options)) : null);
    private static VocabularyExportDocument.Usage ProjectUsage(VocabularyExample usage, VocabularyExportConfiguration options) =>
        new(usage.Id, usage.CreatedAt, usage.Text,
            usage.Completeness, usage.Ranges, usage.Translation, usage.TranslationLanguage,
            OtherValues(usage.Translation, usage.TranslationLanguage, usage.TranslationVariants),
            options.Meanings ? usage.Meaning : null, options.Meanings ? usage.MeaningLanguage : null,
            options.Explanation ? Explanations(usage.Explanations) : null);
    private static VocabularyExportDocument.Block ProjectBlock(VocabularyContentBlock block, VocabularyExportConfiguration options) =>
        block.Content switch
        {
            VocabularyBlockContent.Text text => new VocabularyExportDocument.Block(block.Id, text.Value, null, null, null),
            VocabularyBlockContent.Image image => new VocabularyExportDocument.Block(block.Id, null, image.Reference,
                // This is a portable export-relative name, independent of OS path separators.
                (options.IncludesImages ? "images/" : "") + image.Reference.Filename, image.Caption),
            _ => throw new ArgumentException("Unknown content block.")
        };
    private static IEnumerable<VocabularyExportDocument.Explanation> Explanations(IEnumerable<LearningExplanationRecord> records) =>
        records.Select(record => new VocabularyExportDocument.Explanation(record.Language, record.Result.Meaning, record.Result.Explanation));
    private static IReadOnlyList<VocabularyMeaning> OtherValues(string primary, LearningLanguage? language, IEnumerable<VocabularyMeaning> candidates)
    {
        var result = new List<VocabularyMeaning>();
        foreach (var candidate in candidates)
        {
            if (candidate.Text.Length == 0 || (CollectionValues.Same(candidate.Text, primary) && candidate.Language == language)
                || result.Contains(candidate)) continue;
            result.Add(candidate);
        }
        return CollectionValues.List(result);
    }
}
