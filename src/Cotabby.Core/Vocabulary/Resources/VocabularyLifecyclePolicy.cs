using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Mac's resource-reference admission, independent of whole-archive identity, template,
/// semantic-cycle and SQL checks. The future archive invokes those additional gates too. Raw
/// evidence and uncertain claims without collection membership are valid, not automatic favorites.</summary>
public static class VocabularyLifecyclePolicy
{
    public static void Validate(VocabularyDocument document)
    {
        var senses = document.Catalog.SelectMany(entry => entry.Senses).ToArray();
        var senseIds = senses.Select(sense => sense.Id).ToHashSet();
        if (document.Sources.Select(source => source.Id).Distinct().Count() != document.Sources.Count
            || document.Occurrences.Select(occurrence => occurrence.Id).Distinct().Count() != document.Occurrences.Count
            || document.Interpretations.Select(claim => claim.Id).Distinct().Count() != document.Interpretations.Count
            || document.Collections.Select(collection => collection.SenseId).Distinct().Count() != document.Collections.Count) throw Invalid();
        foreach (var sense in senses)
            if (sense.Examples.Count != 0 || sense.Note.Length != 0 || sense.Tags.Count != 0 || sense.IsSavedWithoutContext
                || sense.Content is not null || sense.Review is not null || sense.ReviewCards is not null) throw Invalid();
        var sources = document.Sources.ToDictionary(source => source.Id);
        var occurrences = document.Occurrences.ToDictionary(occurrence => occurrence.Id);
        var claims = document.Interpretations.ToDictionary(claim => claim.Id);
        var standalone = document.Collections.Where(collection => collection.IsSavedWithoutContext).Select(collection => collection.SenseId).ToHashSet();
        if (document.Catalog.Any(entry => entry.Senses.Count(sense => standalone.Contains(sense.Id)) > 1)) throw Invalid();
        foreach (var source in document.Sources)
            if (source.Text is null || FoundationText.TrimWhitespaceAndNewlines(source.Text).Length == 0 || !Fits(source.Text, 262_144)
                || source.SourceApplicationId is { } app && !Fits(app, 1_024) || !Enum.IsDefined(source.Kind)) throw Invalid();
        foreach (var occurrence in document.Occurrences)
        {
            if (!sources.TryGetValue(occurrence.SourceId, out var source) || occurrence.Ranges.Count > 64) throw Invalid();
            var boundaries = FoundationTextRanges.Boundaries(source.Text);
            if (occurrence.Ranges.Any(range => !FoundationTextRanges.IsValid(range.Location, range.Length, source.Text, boundaries))) throw Invalid();
        }
        var accepted = new HashSet<Guid>();
        foreach (var claim in document.Interpretations)
        {
            if (!occurrences.ContainsKey(claim.OccurrenceId) || claim.SenseId is { } senseId && !senseIds.Contains(senseId)
                || claim.Status == VocabularyInterpretationStatus.Accepted && (claim.SenseId is null || !accepted.Add(claim.OccurrenceId))
                || !Fits(claim.Meaning, 32_000) || !Fits(claim.Translation, 262_144) || claim.TranslationVariants.Count > 64 || claim.Explanations.Count > 24
                || claim.Provenance.Confidence is { } confidence && (!double.IsFinite(confidence) || confidence is < 0 or > 1)
                || !Enum.IsDefined(claim.Status) || !Enum.IsDefined(claim.Completeness) || !Enum.IsDefined(claim.MeaningOrigin)
                || !Enum.IsDefined(claim.Provenance.Origin) || !Enum.IsDefined(claim.Provenance.Status)) throw Invalid();
            foreach (var record in claim.Explanations)
                if (!ValidExplanation(record)) throw Invalid();
        }
        foreach (var collection in document.Collections)
            if (!senseIds.Contains(collection.SenseId) || collection.InterpretationIds.Count > 10_000
                || collection.InterpretationIds.Distinct().Count() != collection.InterpretationIds.Count
                || collection.InterpretationIds.Any(id => !claims.TryGetValue(id, out var claim) || claim.SenseId != collection.SenseId || claim.Status != VocabularyInterpretationStatus.Accepted)
                || !Fits(collection.Note, 262_144) || collection.Tags.Count > 100
                || collection.Tags.Any(tag => tag is null || tag.Length == 0 || !Fits(tag, 200))) throw Invalid();
    }

    // .NET callers can supply malformed UTF-16/enum values that Swift cannot represent. These
    // checks do not add new semantic rules or impose archive budgets on unrelated resource fields.
    // They are not a complete CLR representability pass; that separate gate is required before
    // the future decoder/persistence adapter admits arbitrary resource graphs.
    private static bool Fits(string text, int maximum) => LearningExplanationText.Fits(text, maximum);
    private static bool ValidExplanation(LearningExplanationRecord record)
    {
        if (record is null || record.Result is null) return false;
        var input = record.Input;
        if (input is null || !Fits(input.Target, 32_000) || FoundationText.TrimWhitespaceAndNewlines(input.Target).Length == 0
            || !Fits(input.DictionaryForm, 32_000) || !Fits(input.Context, ModelContextBudget.MaximumTextBytes)
            || input.Ranges.Count is 0 or > 64 || !Enum.IsDefined(input.Kind) || !Enum.IsDefined(input.ContextKind)
            || input.Language is { } language && !Enum.IsDefined(language) || !LearningExplanationPolicy.IsValid(record.Result)) return false;
        var boundaries = FoundationTextRanges.Boundaries(input.Context);
        return input.Ranges.All(range => FoundationTextRanges.IsValid(range.Location, range.Length, input.Context, boundaries));
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
}
