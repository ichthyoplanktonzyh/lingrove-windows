using System.Collections.ObjectModel;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Lossless Mac notebook ↔ relational row mapping. The future SQLite owner supplies decoded typed
/// rows; this pure boundary reconstructs ownership and reprojects it to reject orphan/mismatched
/// rows, smuggled child arrays and inconsistent indexed columns. It is not whole-graph admission,
/// a codec or a persistence transaction; the archive still validates the resulting document.
/// </summary>
public static class VocabularyDatabaseProjection
{
    public static IReadOnlyDictionary<VocabularyDatabaseKey, VocabularyDatabaseRecord> Rows(VocabularyDocument document)
    {
        VocabularyArchiveValidation.ValidateRepresentability(document);
        var rows = new List<VocabularyDatabaseRecord>();
        for (int position = 0; position < document.Entries.Count; position++)
        {
            var entry = document.Entries[position]; string parent = Id(entry.Id);
            rows.Add(new(VocabularyDatabaseTable.Entries, parent, null, position, new VocabularyDatabasePayload.Entry(entry with { Senses = [] }),
                [VocabularyCapturePolicy.EntryKey(entry), entry.Language.Code(), entry.Expression]));
            for (int index = 0; index < entry.Senses.Count; index++) Append(entry.Senses[index], parent, index, rows);
        }
        for (int position = 0; position < (document.ReviewTemplates?.Count ?? 0); position++)
        {
            var template = document.ReviewTemplates![position];
            if (template is null) throw Invalid();
            rows.Add(new(VocabularyDatabaseTable.Templates, Id(template.Id), null, position, new VocabularyDatabasePayload.Template(template)));
        }
        return Map(rows);
    }
    private static void Append(VocabularySense sense, string parent, int position, List<VocabularyDatabaseRecord> rows)
    {
        string id = Id(sense.Id);
        var header = sense with { Examples = [], Explanations = [], Review = null,
            Content = sense.Content is null ? null : [], ReviewCards = sense.ReviewCards is null ? null : [] };
        rows.Add(new(VocabularyDatabaseTable.Senses, id, parent, position, new VocabularyDatabasePayload.Sense(header), [sense.Meaning]));
        Append(sense.Explanations, id, VocabularyDatabaseTable.SenseExplanations, rows);
        for (int index = 0; index < sense.Examples.Count; index++)
        {
            var usage = sense.Examples[index];
            rows.Add(new(VocabularyDatabaseTable.Usages, Id(usage.Id), id, index, new VocabularyDatabasePayload.Usage(usage with { Explanations = [] })));
            Append(usage.Explanations, Id(usage.Id), VocabularyDatabaseTable.UsageExplanations, rows);
        }
        for (int index = 0; index < (sense.Content?.Count ?? 0); index++)
        {
            var block = sense.Content![index]; if (block is null) throw Invalid();
            rows.Add(new(VocabularyDatabaseTable.Blocks, Id(block.Id), id, index, new VocabularyDatabasePayload.Block(block)));
        }
        if (sense.Review is { } legacy) Append(legacy, id + ":legacy", id, 0, rows);
        for (int index = 0; index < (sense.ReviewCards?.Count ?? 0); index++)
        {
            var review = sense.ReviewCards![index];
            if (review is null || review.Plan is null) throw Invalid();
            Append(review, id + ":card:" + Id(review.Plan.Id), id, index + (sense.Review is null ? 0 : 1), rows);
        }
    }
    private static void Append(IReadOnlyList<LearningExplanationRecord> records, string parent, VocabularyDatabaseTable table, List<VocabularyDatabaseRecord> rows)
    {
        for (int index = 0; index < records.Count; index++)
        {
            var record = records[index]; rows.Add(new(table, parent + ":" + Id(record.Id), parent, index, new VocabularyDatabasePayload.Explanation(record)));
        }
    }
    private static void Append(Review.VocabularyReview review, string id, string parent, int position, List<VocabularyDatabaseRecord> rows)
    {
        if (review is null || review.Plan is null || review.Bindings.Any(binding => binding is null)) throw Invalid();
        rows.Add(new(VocabularyDatabaseTable.Reviews, id, parent, position, new VocabularyDatabasePayload.Review(review with { Bindings = [] })));
        for (int index = 0; index < review.Bindings.Count; index++)
        {
            var binding = review.Bindings[index]; rows.Add(new(VocabularyDatabaseTable.Bindings, id + ":" + Id(binding.Id), id, index, new VocabularyDatabasePayload.Binding(binding)));
        }
    }

    public static VocabularyDocument Document(IReadOnlyList<VocabularyDatabaseRecord> rows, bool hasTemplates)
    {
        var expected = Map(rows); var graph = new Graph(rows);
        var entries = graph.Children(VocabularyDatabaseTable.Entries).Select(row => row.Payload is VocabularyDatabasePayload.Entry { Value: not null } payload
            ? payload.Value with { Senses = graph.Children(VocabularyDatabaseTable.Senses, row.Id).Select(graph.Sense).ToArray() } : throw Invalid()).ToArray();
        var templates = graph.Children(VocabularyDatabaseTable.Templates).Select(row => row.Payload is VocabularyDatabasePayload.Template { Value: not null } payload ? payload.Value : throw Invalid()).ToArray();
        var result = new VocabularyDocument { Entries = entries, ReviewTemplates = hasTemplates ? templates : null };
        var actual = Rows(result);
        if (actual.Count != expected.Count || !actual.All(pair => expected.TryGetValue(pair.Key, out var row) && row == pair.Value)) throw Invalid();
        return result;
    }

    private readonly record struct Owner(VocabularyDatabaseTable Table, string? Parent)
    {
        public bool Equals(Owner other) => Table == other.Table && CollectionValues.Same(Parent, other.Parent);
        public override int GetHashCode() => HashCode.Combine(Table, CollectionValues.StringHash(Parent));
    }
    private sealed class Graph
    {
        private readonly IReadOnlyDictionary<Owner, VocabularyDatabaseRecord[]> _groups;
        public Graph(IReadOnlyList<VocabularyDatabaseRecord> rows) => _groups = rows.GroupBy(row => new Owner(row.Table, row.ParentId)).ToDictionary(group => group.Key, group => group.ToArray());
        public VocabularyDatabaseRecord[] Children(VocabularyDatabaseTable table, string? parent = null)
        {
            var values = _groups.TryGetValue(new(table, parent), out var group) ? group.OrderBy(row => row.Position).ToArray() : [];
            if (!values.Select(row => row.Position).SequenceEqual(Enumerable.Range(0, values.Length))) throw Invalid();
            return values;
        }
        private LearningExplanationRecord[] Explanations(VocabularyDatabaseTable table, string parent) => Children(table, parent)
            .Select(row => row.Payload is VocabularyDatabasePayload.Explanation { Value: not null } payload ? payload.Value : throw Invalid()).ToArray();
        public VocabularySense Sense(VocabularyDatabaseRecord row)
        {
            if (row.Payload is not VocabularyDatabasePayload.Sense { Value: not null } payload) throw Invalid();
            var sense = payload.Value with
            {
                Explanations = Explanations(VocabularyDatabaseTable.SenseExplanations, row.Id),
                Examples = Children(VocabularyDatabaseTable.Usages, row.Id).Select(usage => usage.Payload is VocabularyDatabasePayload.Usage { Value: not null } value
                    ? value.Value with { Explanations = Explanations(VocabularyDatabaseTable.UsageExplanations, usage.Id) } : throw Invalid()).ToArray(),
            };
            var blocks = Children(VocabularyDatabaseTable.Blocks, row.Id).Select(block => block.Payload is VocabularyDatabasePayload.Block { Value: not null } value ? value.Value : throw Invalid()).ToArray();
            sense = sense with { Content = sense.Content is null ? null : blocks };
            foreach (var child in Children(VocabularyDatabaseTable.Reviews, row.Id))
            {
                if (child.Payload is not VocabularyDatabasePayload.Review { Value: not null } value) throw Invalid();
                var review = value.Value with { Bindings = Children(VocabularyDatabaseTable.Bindings, child.Id)
                    .Select(binding => binding.Payload is VocabularyDatabasePayload.Binding { Value: not null } bindingValue ? bindingValue.Value : throw Invalid()).ToArray() };
                if (child.Id == row.Id + ":legacy") sense = sense with { Review = review };
                else
                {
                    if (sense.ReviewCards is null) throw Invalid();
                    sense = sense with { ReviewCards = sense.ReviewCards.Append(review).ToArray() };
                }
            }
            return sense;
        }
    }
    private static IReadOnlyDictionary<VocabularyDatabaseKey, VocabularyDatabaseRecord> Map(IEnumerable<VocabularyDatabaseRecord> rows)
    {
        if (rows is null) throw Invalid();
        var result = new Dictionary<VocabularyDatabaseKey, VocabularyDatabaseRecord>();
        foreach (var row in rows)
            if (row is null || !Enum.IsDefined(row.Table) || !result.TryAdd(row.Key, row)) throw Invalid();
        return new ReadOnlyDictionary<VocabularyDatabaseKey, VocabularyDatabaseRecord>(result);
    }
    private static string Id(Guid value) => value.ToString("D").ToUpperInvariant();
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
}
