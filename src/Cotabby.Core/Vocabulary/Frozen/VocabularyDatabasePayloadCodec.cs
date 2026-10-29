using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Exhaustive frozen row-leaf dispatch. The table determines the Codable shape;
/// no whole-document codec, v3 row, storage or UI service is involved.</summary>
public static class VocabularyDatabasePayloadCodec
{
    public const int MaximumPayloadBytes = 64 * 1024 * 1024;
    public static byte[] Encode(VocabularyDatabaseRecord row) => EncodeCore(row, historical35b: false);

    /// <summary>Historical source-probe encoding only. Production SQLite always uses Encode;
    /// populated 3b guidance is rejected instead of being lost to the older wire shape.</summary>
    public static byte[] EncodeAt35b(VocabularyDatabaseRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Payload is VocabularyCategory category && (category.Examples.Count != 0 || category.ScopeNote.Length != 0
                || category.References.Count != 0 || category.Localizations.Count != 0)
            || row.Payload is VocabularyCategoryOverride overlay && (overlay.Examples is not null || overlay.ScopeNote is not null
                || overlay.References is not null || overlay.Localizations is not null))
            throw new VocabularyException(VocabularyFailure.InvalidContent);
        return EncodeCore(row, historical35b: true);
    }

    private static byte[] EncodeCore(VocabularyDatabaseRecord row, bool historical35b)
    {
        ArgumentNullException.ThrowIfNull(row);
        byte[] EncodeValue<T>(T value) => VocabularyPayloadCodec.EncodePortableValue(value, MaximumPayloadBytes, historical35b);
        return (row.Table, row.Payload) switch
        {
            (VocabularyDatabaseTable.Entries, VocabularyEntry x) => EncodeValue(x), (VocabularyDatabaseTable.Senses, VocabularySense x) => EncodeValue(x),
            (VocabularyDatabaseTable.Sources, VocabularySourceSnapshot x) => EncodeValue(x), (VocabularyDatabaseTable.Occurrences, VocabularySourceOccurrence x) => EncodeValue(x),
            (VocabularyDatabaseTable.Usages, VocabularyInterpretation x) => EncodeValue(x), (VocabularyDatabaseTable.Collections, VocabularyCollectionItem x) => EncodeValue(x),
            (VocabularyDatabaseTable.CollectionInterpretations or VocabularyDatabaseTable.MappingEvidence, Guid x) => EncodeValue(x),
            (VocabularyDatabaseTable.SenseExplanations or VocabularyDatabaseTable.UsageExplanations, LearningExplanationRecord x) => EncodeValue(x),
            (VocabularyDatabaseTable.Blocks, VocabularyContentBlock x) => EncodeValue(x), (VocabularyDatabaseTable.Reviews, VocabularyReview x) => EncodeValue(x),
            (VocabularyDatabaseTable.Bindings, AnkiNoteBinding x) => EncodeValue(x), (VocabularyDatabaseTable.Templates, ReviewLayoutTemplate x) => EncodeValue(x),
            (VocabularyDatabaseTable.MeaningNodes, VocabularyMeaningNode x) => EncodeValue(x), (VocabularyDatabaseTable.MeaningLabels, VocabularyMeaningLabel x) => EncodeValue(x),
            (VocabularyDatabaseTable.MeaningRoles, VocabularyMeaningRole x) => EncodeValue(x), (VocabularyDatabaseTable.UsagePatterns, VocabularyUsagePattern x) => EncodeValue(x),
            (VocabularyDatabaseTable.PatternSlots, VocabularyPatternSlot x) => EncodeValue(x), (VocabularyDatabaseTable.MeaningMappings, VocabularyMeaningMapping x) => EncodeValue(x),
            (VocabularyDatabaseTable.MappingRoles, VocabularyRoleBinding x) => EncodeValue(x), (VocabularyDatabaseTable.MeaningRelations, VocabularyMeaningRelation x) => EncodeValue(x),
            (VocabularyDatabaseTable.RetrievalCues, VocabularyRetrievalCue x) => EncodeValue(x), (VocabularyDatabaseTable.CueTargets, VocabularyCueTarget x) => EncodeValue(x),
            (VocabularyDatabaseTable.ClassificationFrameworks, VocabularyClassificationFramework x) => EncodeValue(x), (VocabularyDatabaseTable.Categories, VocabularyCategory x) => EncodeValue(x),
            (VocabularyDatabaseTable.CategoryParents, VocabularyCategoryParent x) => EncodeValue(x), (VocabularyDatabaseTable.CategoryMemberships, VocabularyMeaningCategoryMembership x) => EncodeValue(x),
            (VocabularyDatabaseTable.CategoryOverrides, VocabularyCategoryOverride x) => EncodeValue(x), _ => throw VocabularyDatabaseTables.Invalid(),
        };
    }
    public static object Decode(VocabularyDatabaseTable table, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumPayloadBytes) throw new VocabularyException(VocabularyFailure.OversizedArchive);
        return table switch
        {
            VocabularyDatabaseTable.Entries => DecodeValue<VocabularyEntry>(bytes), VocabularyDatabaseTable.Senses => DecodeValue<VocabularySense>(bytes),
            VocabularyDatabaseTable.Sources => DecodeValue<VocabularySourceSnapshot>(bytes), VocabularyDatabaseTable.Occurrences => DecodeValue<VocabularySourceOccurrence>(bytes),
            VocabularyDatabaseTable.Usages => DecodeValue<VocabularyInterpretation>(bytes), VocabularyDatabaseTable.Collections => DecodeValue<VocabularyCollectionItem>(bytes),
            VocabularyDatabaseTable.CollectionInterpretations or VocabularyDatabaseTable.MappingEvidence => DecodeValue<Guid>(bytes),
            VocabularyDatabaseTable.SenseExplanations or VocabularyDatabaseTable.UsageExplanations => DecodeValue<LearningExplanationRecord>(bytes),
            VocabularyDatabaseTable.Blocks => DecodeValue<VocabularyContentBlock>(bytes), VocabularyDatabaseTable.Reviews => DecodeValue<VocabularyReview>(bytes),
            VocabularyDatabaseTable.Bindings => DecodeValue<AnkiNoteBinding>(bytes), VocabularyDatabaseTable.Templates => DecodeValue<ReviewLayoutTemplate>(bytes),
            VocabularyDatabaseTable.MeaningNodes => DecodeValue<VocabularyMeaningNode>(bytes), VocabularyDatabaseTable.MeaningLabels => DecodeValue<VocabularyMeaningLabel>(bytes),
            VocabularyDatabaseTable.MeaningRoles => DecodeValue<VocabularyMeaningRole>(bytes), VocabularyDatabaseTable.UsagePatterns => DecodeValue<VocabularyUsagePattern>(bytes),
            VocabularyDatabaseTable.PatternSlots => DecodeValue<VocabularyPatternSlot>(bytes), VocabularyDatabaseTable.MeaningMappings => DecodeValue<VocabularyMeaningMapping>(bytes),
            VocabularyDatabaseTable.MappingRoles => DecodeValue<VocabularyRoleBinding>(bytes), VocabularyDatabaseTable.MeaningRelations => DecodeValue<VocabularyMeaningRelation>(bytes),
            VocabularyDatabaseTable.RetrievalCues => DecodeValue<VocabularyRetrievalCue>(bytes), VocabularyDatabaseTable.CueTargets => DecodeValue<VocabularyCueTarget>(bytes),
            VocabularyDatabaseTable.ClassificationFrameworks => DecodeValue<VocabularyClassificationFramework>(bytes), VocabularyDatabaseTable.Categories => DecodeValue<VocabularyCategory>(bytes),
            VocabularyDatabaseTable.CategoryParents => DecodeValue<VocabularyCategoryParent>(bytes), VocabularyDatabaseTable.CategoryMemberships => DecodeValue<VocabularyMeaningCategoryMembership>(bytes),
            VocabularyDatabaseTable.CategoryOverrides => DecodeValue<VocabularyCategoryOverride>(bytes), _ => throw VocabularyDatabaseTables.Invalid(),
        };
    }
    private static T DecodeValue<T>(ReadOnlyMemory<byte> bytes) => VocabularyPayloadCodec.DecodePortableValue<T>(bytes);
}
