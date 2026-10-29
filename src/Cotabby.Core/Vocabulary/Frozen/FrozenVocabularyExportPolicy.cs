using System.Collections.Frozen;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>
/// Mac's pure export selection/projection over an immutable notebook snapshot. The archive
/// calls selection before collecting media; formatters consume only the reduced document. The
/// package factory composes those pure encoders. No file reads, review mutation or UI publication
/// belongs to this boundary.
/// </summary>
public static class FrozenVocabularyExportPolicy
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
    public static FrozenVocabularyExportDocument Document(IReadOnlyList<VocabularyEntry> entries, VocabularyExportConfiguration options,
        VocabularySemanticIndex? semantics = null, VocabularyClassificationIndex? classification = null)
    {
        int version = options.Examples && entries.SelectMany(e => e.Examples).Any(x => x.Completeness == Resources.VocabularyExampleCompleteness.Unknown)
            ? 3 : entries.Any(e => e.Language is null) ? 2 : 1;
        VocabularySemanticIndex? selected = null;
        VocabularyClassificationIndex? directory = null;
        if (options.Meanings && !options.IsExpressionList && semantics is not null && (semantics.Nodes.Count != 0 || semantics.Patterns.Count != 0))
        {
            selected = SelectedSemantics(semantics, entries, options.Examples); version = 4;
            if (classification is not null)
            {
                directory = SelectedClassification(classification, selected.Nodes);
                if (directory.Memberships.Count != 0) version = 5; else directory = null;
            }
        }
        return new(entries.Select(entry => new FrozenVocabularyExportDocument.Entry(entry.Id, entry.Expression, entry.DictionaryForm, entry.Language,
            entry.Kind, entry.Senses.Select(sense => ProjectSense(sense, options)))))
            { Version = version, Semantics = selected, Classification = directory };
    }
    private static VocabularySemanticIndex SelectedSemantics(VocabularySemanticIndex source, IReadOnlyList<VocabularyEntry> entries, bool examples)
    {
        var senses = entries.SelectMany(e => e.Senses).Select(s => s.Id).ToHashSet();
        var evidence = examples ? entries.SelectMany(e => e.Examples).Select(x => x.Id).ToHashSet() : [];
        var mappings = source.Mappings.Where(x => senses.Contains(x.SenseId)).Select(x => x with { EvidenceIds = x.EvidenceIds.Where(evidence.Contains).ToArray() }).ToArray();
        var nodes = mappings.Select(x => x.NodeId).ToHashSet();
        var targets = source.CueTargets.Where(x => nodes.Contains(x.NodeId)).ToArray();
        var cues = targets.Select(x => x.CueId).ToHashSet();
        return source with { Patterns = source.Patterns.Where(x => senses.Contains(x.SenseId)).ToArray(), Mappings = mappings,
            Nodes = source.Nodes.Where(x => nodes.Contains(x.Id)).ToArray(), Relations = source.Relations.Where(x => nodes.Contains(x.SourceId) && nodes.Contains(x.TargetId)).ToArray(),
            CueTargets = targets, Cues = source.Cues.Where(x => cues.Contains(x.Id)).ToArray() };
    }
    private static VocabularyClassificationIndex SelectedClassification(VocabularyClassificationIndex index, IReadOnlyList<VocabularyMeaningNode> nodes)
    {
        var nodeIds = nodes.Select(x => x.Id).ToHashSet(); var memberships = index.Memberships.Where(x => nodeIds.Contains(x.NodeId)).ToArray();
        var parents = index.Parents.ToLookup(x => x.CategoryId); var ids = memberships.Select(x => x.CategoryId).ToHashSet(); var queue = new Queue<Guid>(ids);
        while (queue.TryDequeue(out var id)) foreach (var edge in parents[id]) if (ids.Add(edge.ParentId)) queue.Enqueue(edge.ParentId);
        var categories = index.Categories.Where(x => ids.Contains(x.Id)).ToArray(); var frameworks = categories.Where(x => x.FrameworkId.HasValue).Select(x => x.FrameworkId!.Value).ToHashSet();
        return index with { Frameworks = index.Frameworks.Where(x => frameworks.Contains(x.Id)).ToArray(), Categories = categories,
            Parents = index.Parents.Where(x => ids.Contains(x.CategoryId) && ids.Contains(x.ParentId)).ToArray(), Memberships = memberships,
            Overrides = index.Overrides.Where(x => ids.Contains(x.CategoryId)).ToArray() };
    }
    public static IReadOnlySet<VocabularyImageReference> Images(IReadOnlyList<VocabularyEntry> entries, VocabularyExportConfiguration options) =>
        (options.IncludesImages ? entries.SelectMany(entry => entry.Senses).SelectMany(sense => VocabularyContentPolicy.Images(sense.Content ?? []))
            : Enumerable.Empty<VocabularyImageReference>()).ToFrozenSet();

    public static VocabularyExportPackage Package(IReadOnlyList<VocabularyEntry> entries, VocabularyExportConfiguration options,
        VocabularySemanticIndex? semantics, VocabularyClassificationIndex? classification, VocabularyExportTextLabels? labels = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = CollectionValues.List(entries); var document = Document(captured, options, semantics, classification);
        var data = FrozenVocabularyExportFormatter.Render(document, options, labels, cancellationToken);
        if (data.Length > MaximumDataBytes) throw new VocabularyExportException(VocabularyExportFailure.TooLarge);
        return new(options, data, VocabularyExportFormatter.Preview(data, options.Format), captured.Count,
            captured.Sum(entry => (long)entry.Senses.Count), options.Examples && !options.IsExpressionList
                ? captured.Sum(entry => entry.Senses.Sum(sense => (long)sense.Examples.Count)) : 0);
    }

    private static FrozenVocabularyExportDocument.Sense ProjectSense(VocabularySense sense, VocabularyExportConfiguration options) =>
        new(sense.Id, sense.CreatedAt,
            options.Meanings ? sense.Meaning : null, options.Meanings ? sense.MeaningLanguage : null,
            options.Meanings ? OtherValues(sense.Meaning, sense.MeaningLanguage, sense.StandaloneMeanings.Concat(sense.AlternateMeanings)) : null,
            options.Notes ? sense.Note : null, options.Notes ? sense.Tags : null,
            options.Examples ? sense.Examples.Select(usage => ProjectUsage(usage, options)) : null,
            options.Explanation ? Explanations(sense.Explanations) : null,
            options.Content ? (sense.Content ?? []).Select(block => ProjectBlock(block, options)) : null);
    private static FrozenVocabularyExportDocument.Usage ProjectUsage(VocabularyExample usage, VocabularyExportConfiguration options) =>
        new(usage.Id, usage.CreatedAt, usage.Text,
            usage.Completeness, usage.Ranges, usage.Translation, usage.TranslationLanguage,
            OtherValues(usage.Translation, usage.TranslationLanguage, usage.TranslationVariants),
            options.Meanings ? usage.Meaning : null, options.Meanings ? usage.MeaningLanguage : null,
            options.Explanation ? Explanations(usage.Explanations) : null);
    private static FrozenVocabularyExportDocument.Block ProjectBlock(VocabularyContentBlock block, VocabularyExportConfiguration options) =>
        block.Content switch
        {
            VocabularyBlockContent.Text text => new FrozenVocabularyExportDocument.Block(block.Id, text.Value, null, null, null),
            VocabularyBlockContent.Image image => new FrozenVocabularyExportDocument.Block(block.Id, null, image.Reference,
                // This is a portable export-relative name, independent of OS path separators.
                (options.IncludesImages ? "images/" : "") + image.Reference.Filename, image.Caption),
            _ => throw new ArgumentException("Unknown content block.")
        };
    private static IEnumerable<FrozenVocabularyExportDocument.Explanation> Explanations(IEnumerable<LearningExplanationRecord> records) =>
        records.Select(record => new FrozenVocabularyExportDocument.Explanation(record.Language, record.Result.Meaning, record.Result.Explanation));
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
