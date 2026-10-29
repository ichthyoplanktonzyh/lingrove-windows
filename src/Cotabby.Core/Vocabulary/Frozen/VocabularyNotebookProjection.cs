using LearningExplanationInput = Cotabby.Core.Vocabulary.Resources.LearningExplanationInput;
using LearningExplanationContext = Cotabby.Core.Vocabulary.Resources.LearningExplanationContext;
using FrozenCompleteness = Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness;
using Cotabby.Core.Support;

using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>The pure, immutable counterpart of Mac's joined-entry adapter. The future v8 archive
/// owns canonical records; views borrow joined snapshots. This boundary performs no IO, garbage
/// collection or classification, and never routes a new graph through the older v3 codec.</summary>
public static class VocabularyNotebookProjection
{
    public static IReadOnlyList<VocabularyEntry> Entries(VocabularyDocument document, bool collectedOnly)
    {
        var sources = FirstById(document.Sources, source => source.Id);
        var occurrences = FirstById(document.Occurrences, occurrence => occurrence.Id);
        var collections = FirstById(document.Collections, collection => collection.SenseId);
        var claims = document.Interpretations.Where(claim => claim.Status == VocabularyInterpretationStatus.Accepted && claim.SenseId is not null)
            .GroupBy(claim => claim.SenseId!.Value).ToDictionary(group => group.Key, group => group.ToArray());
        var result = new List<VocabularyEntry>();
        foreach (var entry in document.Catalog)
        {
            var senses = new List<VocabularySense>();
            foreach (var header in entry.Senses)
            {
                collections.TryGetValue(header.Id, out var collection);
                if (collectedOnly && collection is null) continue;
                var selected = (collection?.InterpretationIds ?? []).ToHashSet();
                var examples = new List<VocabularyExample>();
                foreach (var claim in claims.GetValueOrDefault(header.Id) ?? [])
                {
                    if (collectedOnly && !selected.Contains(claim.Id)) continue;
                    if (occurrences.TryGetValue(claim.OccurrenceId, out var occurrence)
                        && sources.TryGetValue(occurrence.SourceId, out var source)) examples.Add(Example(claim, occurrence, source));
                }
                senses.Add(header with { Examples = examples, Note = collection?.Note ?? "", Tags = collection?.Tags ?? [],
                    Content = collection?.Content, Review = collection?.Review, ReviewCards = collection?.ReviewCards,
                    IsSavedWithoutContext = collection?.IsSavedWithoutContext ?? false });
            }
            if (!collectedOnly || senses.Count > 0) result.Add(entry with { Senses = senses });
        }
        return CollectionValues.List(result);
    }

    public static VocabularyExample Example(VocabularyInterpretation claim, VocabularySourceOccurrence occurrence, VocabularySourceSnapshot source) =>
        new(claim.Id, source.Text, claim.Translation, occurrence.Ranges, claim.Completeness, claim.Meaning, claim.CreatedAt)
        {
            SourceId = source.Id, OccurrenceId = occurrence.Id == claim.Id ? null : occurrence.Id, SourceKind = source.Kind,
            TranslationLanguage = claim.TranslationLanguage, TranslationVariants = claim.TranslationVariants,
            MeaningLanguage = claim.MeaningLanguage, MeaningOrigin = claim.MeaningOrigin,
            SourceApplicationId = source.SourceApplicationId, Explanations = claim.Explanations,
        };

    /// <summary>Explicit lexical editing replaces joined entries without inferring that evidence
    /// became garbage. Uncollecting is a separate collection edit, not this lexical-deletion path.</summary>
    public static VocabularyDocument Replace(IReadOnlyList<VocabularyEntry> entries, VocabularyDocument document)
    {
        var priorSenses = document.Catalog.SelectMany(entry => entry.Senses).Select(sense => sense.Id).ToHashSet();
        var priorClaims = document.Interpretations.Where(claim => claim.Status == VocabularyInterpretationStatus.Accepted && claim.SenseId is not null)
            .Select(claim => claim.Id).ToHashSet();
        var senses = entries.SelectMany(entry => entry.Senses).ToArray();
        var senseIds = senses.Select(sense => sense.Id).ToHashSet();
        var claimIds = senses.SelectMany(sense => sense.Examples).Select(example => example.Id).ToHashSet();
        var collections = document.Collections.Where(value => senseIds.Contains(value.SenseId)).ToList();
        var claims = document.Interpretations.Where(claim => !priorClaims.Contains(claim.Id) || claimIds.Contains(claim.Id))
            .Select(claim => claim.SenseId is { } senseId && !senseIds.Contains(senseId)
                ? claim with { SenseId = null, Status = VocabularyInterpretationStatus.Candidate } : claim).ToList();
        var sources = document.Sources.ToList(); var occurrences = document.Occurrences.ToList();
        var sourceOffsets = Offsets(sources, value => value.Id); var occurrenceOffsets = Offsets(occurrences, value => value.Id);
        var claimOffsets = Offsets(claims, value => value.Id); var collectionOffsets = Offsets(collections, value => value.SenseId);
        var catalog = entries.Select(entry => entry with { Senses = entry.Senses.Select(sense => sense with
        {
            Examples = [], Note = "", Tags = [], IsSavedWithoutContext = false,
            Content = null, Review = null, ReviewCards = null,
        }).ToArray() }).ToArray();
        foreach (var sense in senses)
        {
            var old = collectionOffsets.TryGetValue(sense.Id, out int offset) ? collections[offset] : null;
            if (old is not null || !priorSenses.Contains(sense.Id))
            {
                var collection = old ?? new VocabularyCollectionItem(sense.Id, sense.CreatedAt);
                var selected = collection.InterpretationIds.ToHashSet();
                collection = collection with { Note = sense.Note, Tags = sense.Tags, Content = sense.Content, Review = sense.Review,
                    ReviewCards = sense.ReviewCards, IsSavedWithoutContext = sense.IsSavedWithoutContext,
                    InterpretationIds = sense.Examples.Where(example => selected.Contains(example.Id) || !priorClaims.Contains(example.Id)).Select(example => example.Id).ToArray() };
                Upsert(collection, collections, collectionOffsets, value => value.SenseId);
            }
            foreach (var example in sense.Examples) Retain(example, sense.Id);
        }
        return document with { Catalog = catalog, Sources = sources, Occurrences = occurrences, Interpretations = claims, Collections = collections };

        void Retain(VocabularyExample example, Guid senseId)
        {
            Guid sourceId = example.SourceId;
            if (sourceOffsets.TryGetValue(sourceId, out int sourceOffset))
            {
                var previous = sources[sourceOffset];
                if (!CollectionValues.Same(previous.Text, example.Text) || previous.Kind != example.SourceKind
                    || !CollectionValues.Same(previous.SourceApplicationId, example.SourceApplicationId)) sourceId = Guid.NewGuid();
            }
            Guid occurrenceId = example.OccurrenceId ?? example.Id;
            if (occurrenceOffsets.TryGetValue(occurrenceId, out int occurrenceOffset))
            {
                var previous = occurrences[occurrenceOffset];
                if (previous.SourceId != sourceId || !CollectionValues.SequenceEqual(previous.Ranges, example.Ranges)) occurrenceId = Guid.NewGuid();
            }
            if (!sourceOffsets.ContainsKey(sourceId))
                Upsert(new VocabularySourceSnapshot(sourceId, example.Text, example.SourceKind, example.SourceApplicationId, example.CreatedAt), sources, sourceOffsets, value => value.Id);
            Upsert(new VocabularySourceOccurrence(occurrenceId, sourceId, example.Ranges), occurrences, occurrenceOffsets, value => value.Id);
            var provenance = claimOffsets.TryGetValue(example.Id, out int claimOffset) ? claims[claimOffset].Provenance : new VocabularySemanticProvenance();
            var claim = new VocabularyInterpretation(example.Id, occurrenceId, example.CreatedAt)
            {
                SenseId = senseId, Status = VocabularyInterpretationStatus.Accepted, Meaning = example.Meaning,
                MeaningLanguage = example.MeaningLanguage, MeaningOrigin = example.MeaningOrigin, Translation = example.Translation,
                TranslationLanguage = example.TranslationLanguage, TranslationVariants = example.TranslationVariants,
                Completeness = example.Completeness, Provenance = provenance, Explanations = example.Explanations,
            };
            Upsert(claim, claims, claimOffsets, value => value.Id);
        }
    }

    public static VocabularyDocument Collect(Guid senseId, Guid? interpretationId, VocabularyDocument document, SourceDate now, bool withoutContext = false)
    {
        var values = document.Collections.ToList(); var offsets = Offsets(values, value => value.SenseId);
        var value = offsets.TryGetValue(senseId, out int offset) ? values[offset] : new VocabularyCollectionItem(senseId, now);
        value = value with { IsSavedWithoutContext = value.IsSavedWithoutContext || withoutContext,
            InterpretationIds = interpretationId is { } id && !value.InterpretationIds.Contains(id) ? value.InterpretationIds.Append(id).ToArray() : value.InterpretationIds };
        Upsert(value, values, offsets, collection => collection.SenseId);
        return document with { Collections = values };
    }

    private static Dictionary<Guid, T> FirstById<T>(IEnumerable<T> values, Func<T, Guid> id) => values.GroupBy(id).ToDictionary(group => group.Key, group => group.First());
    private static Dictionary<Guid, int> Offsets<T>(IEnumerable<T> values, Func<T, Guid> id)
    {
        var result = new Dictionary<Guid, int>(); int offset = 0;
        foreach (var value in values) { result.TryAdd(id(value), offset); offset++; }
        return result;
    }
    private static void Upsert<T>(T value, List<T> values, Dictionary<Guid, int> offsets, Func<T, Guid> id)
    {
        if (offsets.TryGetValue(id(value), out int offset)) values[offset] = value;
        else { offsets[id(value)] = values.Count; values.Add(value); }
    }
}
