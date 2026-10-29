using System.Collections.ObjectModel;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary.Resources;
using T = Cotabby.Core.Vocabulary.Frozen.VocabularyDatabaseTable;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Frozen resource-to-row projection. Reconstruct and reproject before accepting rows;
/// independent source, claim, collection, meaning and classification owners remain distinct.</summary>
public static class VocabularyDatabaseProjection
{
    public static IReadOnlyDictionary<VocabularyDatabaseKey, VocabularyDatabaseRecord> Rows(VocabularyDocument document)
    {
        VocabularyArchiveValidation.ValidateRepresentability(document); VocabularyLifecyclePolicy.Validate(document);
        VocabularySemanticPolicy.Validate(document.Semantics, document.Entries);
        VocabularyClassificationPolicy.ValidateNodeIds(document.Classification, document.Semantics.Nodes.Select(x => x.Id).ToHashSet());
        var rows = new List<VocabularyDatabaseRecord>();
        void Add(T table, string id, string? parent, int position, object payload, params string?[] extra) => rows.Add(new(table, id, parent, position, payload, extra));
        for (int i = 0; i < document.Sources.Count; i++) { var x = document.Sources[i]; Add(T.Sources, Id(x.Id), null, i, x); }
        for (int i = 0; i < document.Occurrences.Count; i++) { var x = document.Occurrences[i]; Add(T.Occurrences, Id(x.Id), Id(x.SourceId), i, x); }
        void Explanations(IReadOnlyList<LearningExplanationRecord> values, string parent, T table)
        { for (int i = 0; i < values.Count; i++) Add(table, parent + ":" + Id(values[i].Id), parent, i, values[i]); }
        for (int i = 0; i < document.Interpretations.Count; i++)
        {
            var x = document.Interpretations[i]; string id = Id(x.Id);
            Add(T.Usages, id, null, i, x with { Explanations = [] }, Id(x.OccurrenceId), x.SenseId is { } s ? Id(s) : null, Status(x.Status));
            Explanations(x.Explanations, id, T.UsageExplanations);
        }
        for (int i = 0; i < document.Catalog.Count; i++)
        {
            var entry = document.Catalog[i]; string id = Id(entry.Id);
            Add(T.Entries, id, null, i, entry with { Senses = [] }, VocabularyArchiveValidation.EntryKey(entry), entry.Language?.Code() ?? "und", entry.Expression);
            for (int j = 0; j < entry.Senses.Count; j++)
            {
                var sense = entry.Senses[j]; string sid = Id(sense.Id);
                Add(T.Senses, sid, id, j, sense with { Explanations = [] }, sense.Meaning);
                Explanations(sense.Explanations, sid, T.SenseExplanations);
            }
        }
        void Review(VocabularyReview review, string id, string parent, int position)
        {
            Add(T.Reviews, id, parent, position, review with { Bindings = [] });
            for (int i = 0; i < review.Bindings.Count; i++) Add(T.Bindings, id + ":" + Id(review.Bindings[i].Id), id, i, review.Bindings[i]);
        }
        for (int i = 0; i < document.Collections.Count; i++)
        {
            var x = document.Collections[i]; string id = Id(x.SenseId);
            Add(T.Collections, id, null, i, x with { InterpretationIds = [], Review = null, Content = x.Content is null ? null : [], ReviewCards = x.ReviewCards is null ? null : [] }, id);
            for (int j = 0; j < x.InterpretationIds.Count; j++) { var usage = x.InterpretationIds[j]; Add(T.CollectionInterpretations, id + ":" + Id(usage), id, j, usage, Id(usage)); }
            for (int j = 0; j < (x.Content?.Count ?? 0); j++) { var block = x.Content![j]; Add(T.Blocks, Id(block.Id), id, j, block); }
            if (x.Review is { } legacy) Review(legacy, id + ":legacy", id, 0);
            for (int j = 0; j < (x.ReviewCards?.Count ?? 0); j++) { var card = x.ReviewCards![j]; Review(card, id + ":card:" + Id(card.Plan.Id), id, j + (x.Review is null ? 0 : 1)); }
        }
        for (int i = 0; i < (document.ReviewTemplates?.Count ?? 0); i++) { var x = document.ReviewTemplates![i]; Add(T.Templates, Id(x.Id), null, i, x); }
        var sem = document.Semantics;
        for (int i = 0; i < sem.Nodes.Count; i++)
        {
            var node = sem.Nodes[i]; string id = Id(node.Id); Add(T.MeaningNodes, id, null, i, node with { Labels = [], Roles = [] }, NodeKind(node.Kind));
            for (int j = 0; j < node.Labels.Count; j++) { var x = node.Labels[j]; Add(T.MeaningLabels, Id(x.Id), id, j, x, x.Language?.Code() ?? "und", x.Text, x.Preferred ? "yes" : "no"); }
            for (int j = 0; j < node.Roles.Count; j++) { var x = node.Roles[j]; Add(T.MeaningRoles, Id(x.Id), id, j, x, x.Name); }
        }
        for (int i = 0; i < sem.Patterns.Count; i++)
        {
            var x = sem.Patterns[i]; string id = Id(x.Id); Add(T.UsagePatterns, id, Id(x.SenseId), i, x with { Slots = [] }, x.Template);
            for (int j = 0; j < x.Slots.Count; j++) { var slot = x.Slots[j]; Add(T.PatternSlots, Id(slot.Id), id, j, slot, slot.Name); }
        }
        for (int i = 0; i < sem.Mappings.Count; i++)
        {
            var x = sem.Mappings[i]; string id = Id(x.Id);
            Add(T.MeaningMappings, id, Id(x.SenseId), i, x with { RoleBindings = [], EvidenceIds = [] }, Id(x.NodeId), x.PatternId is { } p ? Id(p) : null, MappingRelation(x.Relation));
            for (int j = 0; j < x.RoleBindings.Count; j++) { var role = x.RoleBindings[j]; Add(T.MappingRoles, Id(role.Id), id, j, role, Id(role.RoleId), Id(role.SlotId)); }
            for (int j = 0; j < x.EvidenceIds.Count; j++) { var evidence = x.EvidenceIds[j]; Add(T.MappingEvidence, id + ":" + Id(evidence), id, j, evidence, Id(evidence)); }
        }
        for (int i = 0; i < sem.Relations.Count; i++) { var x = sem.Relations[i]; Add(T.MeaningRelations, Id(x.Id), Id(x.SourceId), i, x, Id(x.TargetId), Relation(x.Kind)); }
        for (int i = 0; i < sem.Cues.Count; i++) { var x = sem.Cues[i]; Add(T.RetrievalCues, Id(x.Id), null, i, x, x.Language?.Code() ?? "und", x.Text); }
        for (int i = 0; i < sem.CueTargets.Count; i++) { var x = sem.CueTargets[i]; Add(T.CueTargets, Id(x.Id), Id(x.CueId), i, x, Id(x.NodeId)); }
        var classification = document.Classification;
        for (int i = 0; i < classification.Frameworks.Count; i++) { var x = classification.Frameworks[i]; Add(T.ClassificationFrameworks, Id(x.Id), null, i, x, x.Key); }
        for (int i = 0; i < classification.Categories.Count; i++) { var x = classification.Categories[i]; Add(T.Categories, Id(x.Id), null, i, x, x.FrameworkId is { } f ? Id(f) : null, x.Key, x.Axis == VocabularyCategoryAxis.Content ? "content" : "function"); }
        for (int i = 0; i < classification.Parents.Count; i++) { var x = classification.Parents[i]; Add(T.CategoryParents, Id(x.Id), null, i, x, Id(x.CategoryId), Id(x.ParentId)); }
        for (int i = 0; i < classification.Memberships.Count; i++) { var x = classification.Memberships[i]; Add(T.CategoryMemberships, Id(x.Id), null, i, x, Id(x.NodeId), Id(x.CategoryId)); }
        for (int i = 0; i < classification.Overrides.Count; i++) { var x = classification.Overrides[i]; Add(T.CategoryOverrides, Id(x.Id), Id(x.CategoryId), i, x); }
        return Map(rows);
    }
    public static VocabularyDocument Document(IReadOnlyList<VocabularyDatabaseRecord> rows, bool hasTemplates)
    {
        if (rows is null) throw Invalid();
        var expected = Map(rows); var graph = new Graph(rows);
        var catalog = graph.Children(T.Entries).Select(row => graph.Value<VocabularyEntry>(row) with
        {
            Senses = graph.Children(T.Senses, row.Id).Select(sense => graph.Value<VocabularySense>(sense) with
            { Explanations = graph.Children(T.SenseExplanations, sense.Id).Select(graph.Value<LearningExplanationRecord>).ToArray() }).ToArray(),
        }).ToArray();
        var sources = graph.All(T.Sources).Select(graph.Value<VocabularySourceSnapshot>).ToArray();
        var occurrences = graph.All(T.Occurrences).Select(graph.Value<VocabularySourceOccurrence>).ToArray();
        var claims = graph.All(T.Usages).Select(row => graph.Value<VocabularyInterpretation>(row) with
        { Explanations = graph.Children(T.UsageExplanations, row.Id).Select(graph.Value<LearningExplanationRecord>).ToArray() }).ToArray();
        var collections = graph.All(T.Collections).Select(row =>
        {
            var collection = graph.Value<VocabularyCollectionItem>(row);
            var blocks = graph.Children(T.Blocks, row.Id).Select(graph.Value<VocabularyContentBlock>).ToArray();
            collection = collection with { InterpretationIds = graph.Children(T.CollectionInterpretations, row.Id).Select(graph.Value<Guid>).ToArray(),
                Content = collection.Content is null ? null : blocks };
            foreach (var child in graph.Children(T.Reviews, row.Id))
            {
                var review = graph.Value<VocabularyReview>(child) with { Bindings = graph.Children(T.Bindings, child.Id).Select(graph.Value<AnkiNoteBinding>).ToArray() };
                if (CollectionValues.Same(child.Id, row.Id + ":legacy")) collection = collection with { Review = review };
                else
                {
                    if (collection.ReviewCards is null) throw Invalid();
                    collection = collection with { ReviewCards = collection.ReviewCards.Append(review).ToArray() };
                }
            }
            return collection;
        }).ToArray();
        var templates = graph.Children(T.Templates).Select(graph.Value<ReviewLayoutTemplate>).ToArray();
        var nodes = graph.All(T.MeaningNodes).Select(row => graph.Value<VocabularyMeaningNode>(row) with
        {
            Labels = graph.Children(T.MeaningLabels, row.Id).Select(graph.Value<VocabularyMeaningLabel>).ToArray(),
            Roles = graph.Children(T.MeaningRoles, row.Id).Select(graph.Value<VocabularyMeaningRole>).ToArray(),
        }).ToArray();
        var patterns = graph.All(T.UsagePatterns).Select(row => graph.Value<VocabularyUsagePattern>(row) with
        { Slots = graph.Children(T.PatternSlots, row.Id).Select(graph.Value<VocabularyPatternSlot>).ToArray() }).ToArray();
        var mappings = graph.All(T.MeaningMappings).Select(row => graph.Value<VocabularyMeaningMapping>(row) with
        {
            RoleBindings = graph.Children(T.MappingRoles, row.Id).Select(graph.Value<VocabularyRoleBinding>).ToArray(),
            EvidenceIds = graph.Children(T.MappingEvidence, row.Id).Select(graph.Value<Guid>).ToArray(),
        }).ToArray();
        var semantic = new VocabularySemanticIndex { Nodes = nodes, Patterns = patterns, Mappings = mappings,
            Relations = graph.All(T.MeaningRelations).Select(graph.Value<VocabularyMeaningRelation>).ToArray(),
            Cues = graph.All(T.RetrievalCues).Select(graph.Value<VocabularyRetrievalCue>).ToArray(),
            CueTargets = graph.All(T.CueTargets).Select(graph.Value<VocabularyCueTarget>).ToArray() };
        var classification = new VocabularyClassificationIndex
        {
            Frameworks = graph.All(T.ClassificationFrameworks).Select(graph.Value<VocabularyClassificationFramework>).ToArray(),
            Categories = graph.All(T.Categories).Select(graph.Value<VocabularyCategory>).ToArray(),
            Parents = graph.All(T.CategoryParents).Select(graph.Value<VocabularyCategoryParent>).ToArray(),
            Memberships = graph.All(T.CategoryMemberships).Select(graph.Value<VocabularyMeaningCategoryMembership>).ToArray(),
            Overrides = graph.All(T.CategoryOverrides).Select(graph.Value<VocabularyCategoryOverride>).ToArray(),
        };
        var document = new VocabularyDocument { Catalog = catalog, Sources = sources, Occurrences = occurrences, Interpretations = claims,
            Collections = collections, ReviewTemplates = hasTemplates ? templates : null, Semantics = semantic, Classification = classification };
        var actual = Rows(document);
        if (actual.Count != expected.Count || !actual.All(pair => expected.TryGetValue(pair.Key, out var value) && value == pair.Value)) throw Invalid();
        return document;
    }
    private readonly record struct Owner(T Table, string? Parent)
    {
        public bool Equals(Owner other) => Table == other.Table && CollectionValues.Same(Parent, other.Parent);
        public override int GetHashCode() => HashCode.Combine(Table, CollectionValues.StringHash(Parent));
    }
    private sealed class Graph
    {
        private readonly IReadOnlyList<VocabularyDatabaseRecord> _rows;
        private readonly IReadOnlyDictionary<Owner, VocabularyDatabaseRecord[]> _groups;
        public Graph(IReadOnlyList<VocabularyDatabaseRecord> rows)
        { _rows = rows; _groups = rows.GroupBy(row => new Owner(row.Table, row.ParentId)).ToDictionary(group => group.Key, group => group.ToArray()); }
        public TPayload Value<TPayload>(VocabularyDatabaseRecord row) => row.Payload is TPayload value ? value : throw Invalid();
        public VocabularyDatabaseRecord[] Children(T table, string? parent = null) => Ordered(_groups.TryGetValue(new(table, parent), out var rows) ? rows : []);
        public VocabularyDatabaseRecord[] All(T table) => Ordered(_rows.Where(row => row.Table == table));
        private static VocabularyDatabaseRecord[] Ordered(IEnumerable<VocabularyDatabaseRecord> rows)
        {
            var result = rows.OrderBy(row => row.Position).ToArray();
            if (!result.Select(row => row.Position).SequenceEqual(Enumerable.Range(0, result.Length))) throw Invalid();
            return result;
        }
    }
    private static string Id(Guid value) => value.ToString("D").ToUpperInvariant();
    private static string Status(VocabularyInterpretationStatus value) => value switch { VocabularyInterpretationStatus.Candidate => "candidate", VocabularyInterpretationStatus.Accepted => "accepted", VocabularyInterpretationStatus.Rejected => "rejected", _ => throw Invalid() };
    private static string NodeKind(VocabularyMeaningNodeKind value) => value switch { VocabularyMeaningNodeKind.Concept => "concept", VocabularyMeaningNodeKind.Function => "function", _ => throw Invalid() };
    private static string MappingRelation(VocabularyMappingRelation value) => value switch { VocabularyMappingRelation.ExpressesContent => "expressesContent", VocabularyMappingRelation.RealizesFunction => "realizesFunction", _ => throw Invalid() };
    private static string Relation(VocabularyMeaningRelationKind value) => value switch { VocabularyMeaningRelationKind.Narrower => "narrower", VocabularyMeaningRelationKind.Opposite => "opposite", VocabularyMeaningRelationKind.Related => "related", _ => throw Invalid() };
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
    private static IReadOnlyDictionary<VocabularyDatabaseKey, VocabularyDatabaseRecord> Map(IEnumerable<VocabularyDatabaseRecord> rows)
    {
        var result = new Dictionary<VocabularyDatabaseKey, VocabularyDatabaseRecord>();
        foreach (var row in rows) if (row is null || !Enum.IsDefined(row.Table) || row.Id is null || row.Payload is null || !result.TryAdd(row.Key, row)) throw Invalid();
        return new ReadOnlyDictionary<VocabularyDatabaseKey, VocabularyDatabaseRecord>(result);
    }
}
