using Cotabby.Core.Support;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

public enum DateOwnerKind { Entry, Sense, Example, Source, Interpretation, Collection, Explanation, Meaning, ReviewBinding }
public enum DateField { CreatedAt, UpdatedAt, EncounteredAt, SavedAt, SyncedAt }

/// <summary>One explicit v7 Windows-tick to Foundation-Double conversion. Path disambiguates
/// equal nested IDs in distinct sense, interpretation and review owners.</summary>
public sealed record DateConversionReceipt(DateOwnerKind OwnerKind, Guid OwnerId, DateField Field, string Path,
    long OriginalUtcTicks, SourceDate Result, bool PreservedWindowsTick);
public sealed class VocabularyV7UpgradeResult
{
    private readonly IReadOnlyList<DateConversionReceipt> _dates;
    public VocabularyV7UpgradeResult(VocabularyDocument document, IReadOnlyList<DateConversionReceipt> dates)
    { Document = document; _dates = CollectionValues.List(dates); }
    public VocabularyDocument Document { get; }
    public IReadOnlyList<DateConversionReceipt> Dates => _dates;
}

/// <summary>Explicit detached conversion only. It performs no storage migration or publication.
/// Receipts expose every source-time conversion so a future owner can choose its migration policy.</summary>
public static class VocabularyV7Upgrade
{
    public static VocabularyV7UpgradeResult Convert(R.VocabularyDocument source, R.VocabularyClassificationIndex classification)
    {
        R.VocabularyFrozenArchiveValidation.Validate(new(source, classification));
        var receipts = new List<DateConversionReceipt>();
        SourceDate Date(DateOwnerKind kind, Guid id, DateField field, string path, DateTimeOffset value)
        {
            var result = SourceDate.FromWindows(value);
            bool preserved = result.TryToWindows(out var windows, out _) && windows.UtcTicks == value.UtcTicks;
            receipts.Add(new(kind, id, field, path, value.UtcTicks, result, preserved));
            return result;
        }
        LearningExplanationRecord Record(R.LearningExplanationRecord value, string path) => new(value.Id, value.Input, value.Language, value.Result, value.Model,
            Date(DateOwnerKind.Explanation, value.Id, DateField.CreatedAt, path + ".createdAt", value.CreatedAt));
        VocabularyExample Example(R.VocabularyExample value, string path) => new(value.Id, value.Text, value.Translation, value.Ranges, value.Completeness, value.Meaning,
            Date(DateOwnerKind.Example, value.Id, DateField.CreatedAt, path + ".createdAt", value.CreatedAt))
        {
            SourceId = value.SourceId, OccurrenceId = value.OccurrenceId, SourceKind = value.SourceKind,
            TranslationLanguage = value.TranslationLanguage, TranslationVariants = value.TranslationVariants,
            MeaningLanguage = value.MeaningLanguage, MeaningOrigin = value.MeaningOrigin, SourceApplicationId = value.SourceApplicationId,
            Explanations = value.Explanations.Select((record, i) => Record(record, path + $".explanations[{i}]")).ToArray(),
        };
        AnkiNoteBinding Binding(Cotabby.Core.Review.AnkiNoteBinding value, string path) => new(value.Id, value.OriginSenseId, value.Endpoint, value.Profile, value.NoteId,
            value.Model, value.Deck, value.IdentityField, value.Mapping, value.Fields,
            value.SyncedAt is { } synced ? Date(DateOwnerKind.ReviewBinding, value.Id, DateField.SyncedAt, path + ".syncedAt", synced) : null);
        VocabularyReview Review(Cotabby.Core.Review.VocabularyReview value, string path) => new(value.Plan,
            value.Bindings.Select((binding, i) => Binding(binding, path + $".bindings[{i}]")).ToArray(), value.ActiveBindingId, value.PreservedPlans);
        VocabularySense Sense(R.VocabularySense value, string path) => new(value.Id, value.Meaning,
            Date(DateOwnerKind.Sense, value.Id, DateField.CreatedAt, path + ".createdAt", value.CreatedAt),
            Date(DateOwnerKind.Sense, value.Id, DateField.UpdatedAt, path + ".updatedAt", value.UpdatedAt))
        {
            MeaningLanguage = value.MeaningLanguage, MeaningOrigin = value.MeaningOrigin, Note = value.Note, Tags = value.Tags,
            Examples = value.Examples.Select((example, i) => Example(example, path + $".examples[{i}]")).ToArray(), IsSavedWithoutContext = value.IsSavedWithoutContext,
            StandaloneMeanings = value.StandaloneMeanings, Explanations = value.Explanations.Select((record, i) => Record(record, path + $".explanations[{i}]")).ToArray(),
            AlternateMeanings = value.AlternateMeanings, MergedFrom = value.MergedFrom,
            Review = value.Review is null ? null : Review(value.Review, path + ".review"),
            ReviewCards = value.ReviewCards?.Select((review, i) => Review(review, path + $".reviewCards[{i}]")).ToArray(),
            Content = value.Content, CreatedWithoutContext = value.CreatedWithoutContext,
        };
        var catalog = source.Catalog.Select((value, i) => new VocabularyEntry(value.Id, value.Expression, value.DictionaryForm, value.Language,
            value.Kind, value.Senses.Select((sense, j) => Sense(sense, $"catalog[{i}].senses[{j}]")).ToArray(),
            Date(DateOwnerKind.Entry, value.Id, DateField.CreatedAt, $"catalog[{i}].createdAt", value.CreatedAt),
            Date(DateOwnerKind.Entry, value.Id, DateField.UpdatedAt, $"catalog[{i}].updatedAt", value.UpdatedAt))).ToArray();
        var sources = source.Sources.Select((value, i) => new VocabularySourceSnapshot(value.Id, value.Text, value.Kind, value.SourceApplicationId,
            Date(DateOwnerKind.Source, value.Id, DateField.EncounteredAt, $"sources[{i}].encounteredAt", value.EncounteredAt))).ToArray();
        var claims = source.Interpretations.Select((value, i) => new VocabularyInterpretation(value.Id, value.OccurrenceId,
            Date(DateOwnerKind.Interpretation, value.Id, DateField.CreatedAt, $"interpretations[{i}].createdAt", value.CreatedAt))
        {
            SenseId = value.SenseId, Status = value.Status, Meaning = value.Meaning, MeaningLanguage = value.MeaningLanguage,
            MeaningOrigin = value.MeaningOrigin, Translation = value.Translation, TranslationLanguage = value.TranslationLanguage,
            TranslationVariants = value.TranslationVariants, Completeness = value.Completeness, Provenance = value.Provenance,
            Explanations = value.Explanations.Select((record, j) => Record(record, $"interpretations[{i}].explanations[{j}]")).ToArray(),
        }).ToArray();
        var collections = source.Collections.Select((value, i) => new VocabularyCollectionItem(value.SenseId,
            Date(DateOwnerKind.Collection, value.SenseId, DateField.SavedAt, $"collections[{i}].savedAt", value.SavedAt))
        {
            InterpretationIds = value.InterpretationIds, IsSavedWithoutContext = value.IsSavedWithoutContext, Note = value.Note, Tags = value.Tags,
            Content = value.Content, Review = value.Review is null ? null : Review(value.Review, $"collections[{i}].review"),
            ReviewCards = value.ReviewCards?.Select((review, j) => Review(review, $"collections[{i}].reviewCards[{j}]")).ToArray(),
        }).ToArray();
        var nodes = source.Semantics.Nodes.Select((value, i) => new VocabularyMeaningNode(value.Id, value.Labels)
        {
            Kind = value.Kind, Definition = value.Definition, ScopeNote = value.ScopeNote, Roles = value.Roles, Provenance = value.Provenance,
            CreatedAt = Date(DateOwnerKind.Meaning, value.Id, DateField.CreatedAt, $"semantics.nodes[{i}].createdAt", value.CreatedAt),
            UpdatedAt = Date(DateOwnerKind.Meaning, value.Id, DateField.UpdatedAt, $"semantics.nodes[{i}].updatedAt", value.UpdatedAt),
        }).ToArray();
        var semantics = new VocabularySemanticIndex { Nodes = nodes, Patterns = source.Semantics.Patterns, Mappings = source.Semantics.Mappings,
            Relations = source.Semantics.Relations, Cues = source.Semantics.Cues, CueTargets = source.Semantics.CueTargets };
        var document = new VocabularyDocument { Catalog = catalog, Sources = sources, Occurrences = source.Occurrences, Interpretations = claims,
            Collections = collections, Semantics = semantics, ReviewTemplates = source.ReviewTemplates, Classification = classification };
        VocabularyArchiveValidation.Validate(document);
        return new(document, receipts);
    }
}
