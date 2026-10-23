using System.Globalization;
using System.Text.RegularExpressions;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Pure whole-notebook admission from the stable Mac archive. A future serialized archive calls it
/// on load and before committing a detached draft; this type neither caches nor publishes values.
/// Identity scopes deliberately match Mac: entry/sense/usage/retired aliases/sense blocks/flexible
/// card plans share a set, while templates, layout slots and bindings have their own local scopes.
/// </summary>
public static partial class VocabularyArchiveValidation
{
    public static void Validate(VocabularyDocument document)
    {
        if (document is null || document.SchemaVersion != 3 || document.Entries.Any(entry => !Representable(entry))
            || document.Entries.Select(VocabularyCapturePolicy.EntryKey).Distinct(StringComparer.Ordinal).Count() != document.Entries.Count) throw Invalid();
        ValidateTemplates(document.ReviewTemplates ?? []);
        var ids = new HashSet<Guid>();
        foreach (var entry in document.Entries)
        {
            if (!ids.Add(entry.Id) || VocabularyCapturePolicy.Normalized(entry.Expression).Length == 0 || entry.Senses.Count == 0
                || entry.Examples.Select(VocabularyCapturePolicy.ExampleKey).Distinct(StringComparer.Ordinal).Count() != entry.Examples.Count
                || entry.Senses.Count(sense => sense.IsSavedWithoutContext) > 1) throw Invalid();
            foreach (var sense in entry.Senses)
            {
                VocabularyContentPolicy.Validate(sense.Content ?? []);
                foreach (var block in sense.Content ?? []) if (!ids.Add(block.Id)) throw Invalid();
                if (sense.Review is { } review) ValidateReview(review);
                ValidateCards(sense.ReviewCards ?? [], ids);
                var input = LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, null, sense));
                if (!ids.Add(sense.Id) || !sense.MergedFrom.All(ids.Add) || !ValidRecords(sense.Explanations, input, 128)) throw Invalid();
                foreach (var usage in sense.Examples)
                {
                    var usageInput = LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, usage, sense));
                    if (!ids.Add(usage.Id) || LearningExplanationText.Trim(usage.Text).Length == 0 || !Fits(usage.Text, 262_144)
                        || !ValidRecords(usage.Explanations, usageInput, 24)) throw Invalid();
                    var boundaries = FoundationTextRanges.Boundaries(usage.Text);
                    if (!usage.Ranges.All(range => FoundationTextRanges.IsValid(range.Location, range.Length, usage.Text, boundaries))) throw Invalid();
                }
            }
        }
    }

    public static void ValidateReview(VocabularyReview review)
    {
        if (review is null || review.Plan is null || review.Bindings.Any(binding => binding is null) || review.PreservedPlans.Any(plan => plan is null)
            || review.Bindings.Count > 64 || review.PreservedPlans.Count > 64 || review.Bindings.Select(binding => binding.Id).Distinct().Count() != review.Bindings.Count
            || review.ActiveBindingId is { } active && !review.Bindings.Any(binding => binding.Id == active)) throw Invalid();
        foreach (var plan in new[] { review.Plan }.Concat(review.PreservedPlans))
        {
            if (plan.Layout is { } layout) ReviewLayoutPolicy.Validate(layout);
            if (!Optional(plan.Name, 1024) || !Optional(plan.Answer, 262_144) || !Optional(plan.Deck)
                || plan.FieldOverrides.Count > 32 || !plan.FieldOverrides.All(pair => Fits(pair.Key, 200) && Fits(pair.Value, 1_000_000))
                || plan.AudioKinds?.Any(kind => !Enum.IsDefined(kind)) == true
                || plan.AudioReceipts?.Any(pair => !Enum.IsDefined(pair.Key)) == true) throw Invalid();
        }
        foreach (var plan in new[] { review.Plan }.Concat(review.PreservedPlans))
        {
            if (plan.AudioReceipts?.Count > 2 || plan.AudioReceipts?.Values.Any(receipt => receipt is null
                || !Fits(receipt.Fingerprint) || new StringInfo(receipt.Fingerprint).LengthInTextElements != 64
                || !Fits(receipt.TextHash) || new StringInfo(receipt.TextHash).LengthInTextElements != 64
                || !IsValidAudioFilename(receipt.Filename)
                || !AnkiArchiveEndpointPolicy.IsValid(receipt.Endpoint) || !Fits(receipt.Profile) || receipt.Profile.Length == 0) == true) throw Invalid();
        }
        foreach (var binding in review.Bindings)
        {
            if (binding.NoteId is <= 0 || !Fits(binding.Profile) || binding.Profile.Length == 0 || !Fits(binding.Model) || binding.Model.Length == 0
                || !AnkiArchiveEndpointPolicy.IsValid(binding.Endpoint) || !Fits(binding.IdentityField) || !Fits(binding.Deck)
                || !binding.Mapping.All(pair => Enum.IsDefined(pair.Key) && Fits(pair.Value))
                || !binding.Mapping.TryGetValue(ReviewField.Identity, out string? field) || !CanonicalStringComparer.Instance.Equals(field, binding.IdentityField)
                || !binding.Fields.TryGetValue(binding.IdentityField, out string? identity) || identity != binding.OriginSenseId.ToString("D")
                || binding.Fields.Count > 32 || !binding.Fields.All(pair => Fits(pair.Key) && Fits(pair.Value, 1_000_000))) throw Invalid();
        }
    }

    public static void ValidateTemplates(IReadOnlyList<ReviewLayoutTemplate> templates)
    {
        if (templates is null || templates.Count > 64 || templates.Any(template => template is null)
            || templates.Select(template => template.Id).Distinct().Count() != templates.Count) throw Invalid();
        foreach (var template in templates)
        {
            if (!Fits(template.Name, 1024) || LearningExplanationText.Trim(template.Name).Length == 0) throw Invalid();
            ReviewLayoutPolicy.Validate(template.Layout);
        }
    }
    /// <summary>Mac retained-filename format only; no file, audio bytes or digest is verified.</summary>
    public static bool IsValidAudioFilename(string? value) => Fits(value) && AudioFilename().IsMatch(value!);
    private static void ValidateCards(IReadOnlyList<VocabularyReview> cards, HashSet<Guid> ids)
    {
        if (cards.Count > 64) throw Invalid();
        foreach (var card in cards)
        {
            if (card is null || card.Plan is null || !ids.Add(card.Plan.Id) || card.Plan.Layout is null) throw Invalid();
            ReviewLayoutPolicy.Validate(card.Plan.Layout); ValidateReview(card);
        }
    }
    private static bool ValidRecords(IReadOnlyList<LearningExplanationRecord> records, LearningExplanationInput input, int maximum) =>
        records.Count <= maximum && records.All(record => record is { Input: not null, Result: not null }
            && Enum.IsDefined(record.Language) && Fits(record.Model) && Optional(record.Input.OccurrenceIdentity)
            && LearningExplanationPolicy.IsValid(record.Input) && LearningExplanationPolicy.IsValid(record.Result) && record.Input == input);

    // These are representability checks only. Swift has no null strings/objects or invalid enum
    // integers/malformed UTF-16; do not accidentally turn this pass into new product text budgets.
    private static bool Representable(VocabularyEntry entry) => entry is not null && Enum.IsDefined(entry.Language) && Enum.IsDefined(entry.Kind)
        && Fits(entry.Expression) && Fits(entry.DictionaryForm) && entry.Senses.All(sense => sense is not null && Fits(sense.Meaning) && Fits(sense.Note)
            && Language(sense.MeaningLanguage) && Enum.IsDefined(sense.MeaningOrigin) && sense.Tags.All(tag => Fits(tag))
            && sense.StandaloneMeanings.Concat(sense.AlternateMeanings).All(meaning => meaning is not null && Fits(meaning.Text) && Language(meaning.Language))
            && sense.Examples.All(usage => usage is not null && Fits(usage.Text) && Fits(usage.Translation) && Fits(usage.Meaning)
                && Optional(usage.SourceApplicationId) && Language(usage.TranslationLanguage) && Language(usage.MeaningLanguage)
                && Enum.IsDefined(usage.MeaningOrigin) && Enum.IsDefined(usage.Completeness)
                && usage.TranslationVariants.All(meaning => meaning is not null && Fits(meaning.Text) && Language(meaning.Language))));
    private static bool Language(LearningLanguage? language) => language is null || Enum.IsDefined(language.Value);
    private static bool Optional(string? text, int maximum = int.MaxValue) => text is null || Fits(text, maximum);
    private static bool Fits(string? text, int maximum = int.MaxValue) => LearningExplanationText.Fits(text, maximum);
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
    // Foundation/ICU's $ allows one final Unicode newline sequence; .NET's $ only handles LF.
    // Keep that admission behavior here without trimming or changing the retained filename.
    [GeneratedRegex("^lingrove-[a-f0-9]{64}\\.(wav|mp3)(?:\\r\\n|[\\r\\n\\u0085\\u2028\\u2029\\v\\f])?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex AudioFilename();
}
