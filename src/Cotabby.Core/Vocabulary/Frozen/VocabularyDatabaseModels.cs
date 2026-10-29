using Cotabby.Core.Review;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Frozen Mac table order is the dependency order for upserts and reverse deletes.</summary>
public enum VocabularyDatabaseTable
{
    Entries, Senses, Sources, Occurrences, Usages, Collections, CollectionInterpretations,
    SenseExplanations, UsageExplanations, Blocks, Reviews, Bindings, Templates,
    MeaningNodes, MeaningLabels, MeaningRoles, UsagePatterns, PatternSlots, MeaningMappings,
    MappingRoles, MappingEvidence, MeaningRelations, RetrievalCues, CueTargets,
    ClassificationFrameworks, Categories, CategoryParents, CategoryMemberships, CategoryOverrides,
}
public static class VocabularyDatabaseTables
{
    public static string Name(this VocabularyDatabaseTable value) => value switch
    {
        VocabularyDatabaseTable.Entries => "entries", VocabularyDatabaseTable.Senses => "senses", VocabularyDatabaseTable.Sources => "sources",
        VocabularyDatabaseTable.Occurrences => "occurrences", VocabularyDatabaseTable.Usages => "usages", VocabularyDatabaseTable.Collections => "collections",
        VocabularyDatabaseTable.CollectionInterpretations => "collection_interpretations", VocabularyDatabaseTable.SenseExplanations => "sense_explanations",
        VocabularyDatabaseTable.UsageExplanations => "usage_explanations", VocabularyDatabaseTable.Blocks => "blocks", VocabularyDatabaseTable.Reviews => "reviews",
        VocabularyDatabaseTable.Bindings => "bindings", VocabularyDatabaseTable.Templates => "templates", VocabularyDatabaseTable.MeaningNodes => "meaning_nodes",
        VocabularyDatabaseTable.MeaningLabels => "meaning_labels", VocabularyDatabaseTable.MeaningRoles => "meaning_roles", VocabularyDatabaseTable.UsagePatterns => "usage_patterns",
        VocabularyDatabaseTable.PatternSlots => "pattern_slots", VocabularyDatabaseTable.MeaningMappings => "meaning_mappings", VocabularyDatabaseTable.MappingRoles => "mapping_roles",
        VocabularyDatabaseTable.MappingEvidence => "mapping_evidence", VocabularyDatabaseTable.MeaningRelations => "meaning_relations", VocabularyDatabaseTable.RetrievalCues => "retrieval_cues",
        VocabularyDatabaseTable.CueTargets => "cue_targets", VocabularyDatabaseTable.ClassificationFrameworks => "classification_frameworks", VocabularyDatabaseTable.Categories => "categories",
        VocabularyDatabaseTable.CategoryParents => "category_parents", VocabularyDatabaseTable.CategoryMemberships => "category_memberships", VocabularyDatabaseTable.CategoryOverrides => "category_overrides",
        _ => throw Invalid(),
    };
    public static VocabularyDatabaseTable? Parent(this VocabularyDatabaseTable table) => table switch
    {
        VocabularyDatabaseTable.Entries or VocabularyDatabaseTable.Templates or VocabularyDatabaseTable.MeaningNodes or VocabularyDatabaseTable.RetrievalCues
            or VocabularyDatabaseTable.Sources or VocabularyDatabaseTable.Usages or VocabularyDatabaseTable.Collections or VocabularyDatabaseTable.ClassificationFrameworks
            or VocabularyDatabaseTable.Categories or VocabularyDatabaseTable.CategoryParents or VocabularyDatabaseTable.CategoryMemberships => null,
        VocabularyDatabaseTable.CategoryOverrides => VocabularyDatabaseTable.Categories, VocabularyDatabaseTable.Occurrences => VocabularyDatabaseTable.Sources,
        VocabularyDatabaseTable.Senses => VocabularyDatabaseTable.Entries, VocabularyDatabaseTable.SenseExplanations => VocabularyDatabaseTable.Senses,
        VocabularyDatabaseTable.Blocks or VocabularyDatabaseTable.Reviews or VocabularyDatabaseTable.CollectionInterpretations => VocabularyDatabaseTable.Collections,
        VocabularyDatabaseTable.UsageExplanations => VocabularyDatabaseTable.Usages, VocabularyDatabaseTable.Bindings => VocabularyDatabaseTable.Reviews,
        VocabularyDatabaseTable.MeaningLabels or VocabularyDatabaseTable.MeaningRoles or VocabularyDatabaseTable.MeaningRelations => VocabularyDatabaseTable.MeaningNodes,
        VocabularyDatabaseTable.UsagePatterns or VocabularyDatabaseTable.MeaningMappings => VocabularyDatabaseTable.Senses,
        VocabularyDatabaseTable.PatternSlots => VocabularyDatabaseTable.UsagePatterns,
        VocabularyDatabaseTable.MappingRoles or VocabularyDatabaseTable.MappingEvidence => VocabularyDatabaseTable.MeaningMappings,
        VocabularyDatabaseTable.CueTargets => VocabularyDatabaseTable.RetrievalCues, _ => throw Invalid(),
    };
    public static IReadOnlyList<string> ExtraColumns(this VocabularyDatabaseTable table) => table switch
    {
        VocabularyDatabaseTable.Entries => ["lookup_key", "language", "expression"], VocabularyDatabaseTable.Senses => ["meaning"],
        VocabularyDatabaseTable.Usages => ["occurrence_id", "sense_id", "status"], VocabularyDatabaseTable.Collections => ["sense_id"],
        VocabularyDatabaseTable.CollectionInterpretations or VocabularyDatabaseTable.MappingEvidence => ["usage_id"],
        VocabularyDatabaseTable.MeaningNodes => ["kind"], VocabularyDatabaseTable.MeaningLabels => ["language", "text", "preferred"],
        VocabularyDatabaseTable.MeaningRoles or VocabularyDatabaseTable.PatternSlots => ["name"], VocabularyDatabaseTable.UsagePatterns => ["template"],
        VocabularyDatabaseTable.MeaningMappings => ["meaning_id", "pattern_id", "relation"], VocabularyDatabaseTable.MappingRoles => ["role_id", "slot_id"],
        VocabularyDatabaseTable.MeaningRelations => ["target_id", "relation"], VocabularyDatabaseTable.RetrievalCues => ["language", "text"],
        VocabularyDatabaseTable.CueTargets => ["meaning_id"], VocabularyDatabaseTable.ClassificationFrameworks => ["catalog_key"],
        VocabularyDatabaseTable.Categories => ["framework_id", "catalog_key", "axis"], VocabularyDatabaseTable.CategoryParents => ["category_id", "parent_category_id"],
        VocabularyDatabaseTable.CategoryMemberships => ["meaning_id", "category_id"], _ when Enum.IsDefined(table) => [], _ => throw Invalid(),
    };
    internal static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
}
public readonly record struct VocabularyDatabaseKey(VocabularyDatabaseTable Table, string Id)
{
    public bool Equals(VocabularyDatabaseKey other) => Table == other.Table && CollectionValues.Same(Id, other.Id);
    public override int GetHashCode() => HashCode.Combine(Table, CollectionValues.StringHash(Id));
}
/// <summary>Table-specific immutable leaf/header. The codec enforces the expected payload type;
/// relationship IDs, nullable indexes and order are validated by re-projection.</summary>
public sealed record VocabularyDatabaseRecord
{
    private IReadOnlyList<string?> _extra = Array.Empty<string?>();
    private object _payload = null!;
    public VocabularyDatabaseRecord(VocabularyDatabaseTable table, string id, string? parentId, int position, object payload, IReadOnlyList<string?>? extra = null)
    { Table = table; Id = id; ParentId = parentId; Position = position; Payload = payload; Extra = extra ?? []; }
    public VocabularyDatabaseTable Table { get; init; }
    public string Id { get; init; }
    public string? ParentId { get; init; }
    public int Position { get; init; }
    public object Payload
    {
        get => _payload;
        init => _payload = value switch
        {
            VocabularyEntry or VocabularySense or VocabularySourceSnapshot or VocabularySourceOccurrence or VocabularyInterpretation
                or VocabularyCollectionItem or LearningExplanationRecord or VocabularyContentBlock or VocabularyReview or AnkiNoteBinding
                or ReviewLayoutTemplate or VocabularyMeaningNode or VocabularyMeaningLabel or VocabularyMeaningRole or VocabularyUsagePattern
                or VocabularyPatternSlot or VocabularyMeaningMapping or VocabularyRoleBinding or VocabularyMeaningRelation or VocabularyRetrievalCue
                or VocabularyCueTarget or VocabularyClassificationFramework or VocabularyCategory or VocabularyCategoryParent
                or VocabularyMeaningCategoryMembership or VocabularyCategoryOverride or Guid => value,
            _ => throw VocabularyDatabaseTables.Invalid(),
        };
    }
    public IReadOnlyList<string?> Extra { get => _extra; init => _extra = CollectionValues.List(value); }
    public VocabularyDatabaseKey Key => new(Table, Id);
    public bool Equals(VocabularyDatabaseRecord? other) => other is not null && Table == other.Table && CollectionValues.Same(Id, other.Id)
        && CollectionValues.Same(ParentId, other.ParentId) && Position == other.Position && Equals(Payload, other.Payload) && CollectionValues.SequenceEqual(Extra, other.Extra);
    public override int GetHashCode() => HashCode.Combine(Table, CollectionValues.StringHash(Id), CollectionValues.StringHash(ParentId), Position, Payload, CollectionValues.SequenceHash(Extra));
}
