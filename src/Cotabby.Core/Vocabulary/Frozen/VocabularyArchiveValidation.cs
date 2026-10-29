using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Text;

using Cotabby.Core.Vocabulary.Resources;
using LearningExplanationInput = Cotabby.Core.Vocabulary.Resources.LearningExplanationInput;
using LearningExplanationContext = Cotabby.Core.Vocabulary.Resources.LearningExplanationContext;
using FrozenCompleteness = Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Whole frozen-resource admission, without IO or mutation. Validate before any future
/// decoder result or archive draft can be used. Raw uncollected evidence stays in the graph.</summary>
public static partial class VocabularyArchiveValidation
{
    public static void Validate(VocabularyDocument document)
    {
        ValidateRepresentability(document);
        var resources = document;
        if (resources.SchemaVersion != 8) throw Invalid();
        VocabularyLifecyclePolicy.Validate(resources);
        var entries = resources.Entries;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (!keys.Add(EntryKey(entry))) throw Invalid();
        Cotabby.Core.Vocabulary.VocabularyArchiveValidation.ValidateTemplates(resources.ReviewTemplates ?? []);
        var ids = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (!ids.Add(entry.Id) || VocabularyCapturePolicy.Normalized(entry.Expression).Length == 0) throw Invalid();
            foreach (var sense in entry.Senses)
            {
                VocabularyContentPolicy.Validate(sense.Content ?? []);
                foreach (var block in sense.Content ?? []) if (!ids.Add(block.Id)) throw Invalid();
                if (sense.Review is { } review) ValidateReview(review);
                ValidateCards(sense.ReviewCards ?? [], ids);
                if (!ids.Add(sense.Id) || !sense.MergedFrom.All(ids.Add) || !ValidRecords(sense.Explanations, Input(entry, sense, null), 128)) throw Invalid();
                foreach (var example in sense.Examples)
                {
                    if (!ids.Add(example.Id) || FoundationText.TrimWhitespaceAndNewlines(example.Text).Length == 0
                        || !Fits(example.Text, 262_144) || !ValidRecords(example.Explanations, Input(entry, sense, example), 24)) throw Invalid();
                    var boundaries = FoundationTextRanges.Boundaries(example.Text);
                    if (!example.Ranges.All(range => FoundationTextRanges.IsValid(range.Location, range.Length, example.Text, boundaries))) throw Invalid();
                }
            }
        }
        VocabularySemanticPolicy.Validate(resources.Semantics, entries);
        VocabularyClassificationPolicy.ValidateNodeIds(document.Classification, resources.Semantics.Nodes.Select(node => node.Id).ToHashSet());
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
                || !Cotabby.Core.Vocabulary.VocabularyArchiveValidation.IsValidAudioFilename(receipt.Filename)
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

    private static void ValidateCards(IReadOnlyList<VocabularyReview> cards, HashSet<Guid> ids)
    {
        if (cards.Count > 64) throw Invalid();
        foreach (var card in cards)
        {
            if (!ids.Add(card.Plan.Id) || card.Plan.Layout is null) throw Invalid();
            ReviewLayoutPolicy.Validate(card.Plan.Layout);
            ValidateReview(card);
        }
    }
    private static bool ValidRecords(IReadOnlyList<LearningExplanationRecord> records, LearningExplanationInput expected, int max) =>
        records.Count <= max && records.All(record => ValidInput(record.Input) && LearningExplanationPolicy.IsValid(record.Result) && record.Input == expected);
    private static bool ValidInput(LearningExplanationInput input)
    {
        if (!Fits(input.Target, 32_000) || FoundationText.TrimWhitespaceAndNewlines(input.Target).Length == 0
            || !Fits(input.DictionaryForm, 32_000) || !Fits(input.Context, ModelContextBudget.MaximumTextBytes)
            || input.Ranges.Count is 0 or > 64) return false;
        var boundaries = FoundationTextRanges.Boundaries(input.Context);
        return input.Ranges.All(range => FoundationTextRanges.IsValid(range.Location, range.Length, input.Context, boundaries));
    }
    private static LearningExplanationInput Input(VocabularyEntry entry, VocabularySense sense, VocabularyExample? example) =>
        new(entry.Expression, entry.DictionaryForm, entry.Language, entry.Kind, example?.Text ?? entry.Expression,
            example?.Ranges ?? [new VocabularyTextRange(0, entry.Expression.Length)],
            example is null ? LearningExplanationContext.None : example.Completeness switch
            {
                FrozenCompleteness.Sentence => LearningExplanationContext.Sentence,
                FrozenCompleteness.Fragment => LearningExplanationContext.Fragment,
                FrozenCompleteness.Unknown => LearningExplanationContext.Unknown,
                _ => throw Invalid(),
            },
            example is not null ? ExampleKey(example) : sense.CreatedWithoutContext == true && !sense.IsSavedWithoutContext
                ? "sense:" + sense.Id.ToString("D").ToUpperInvariant() : null);
    private static string EntryKey(VocabularyEntry entry) => Key([entry.Language?.Code() ?? "und", Kind(entry.Kind), VocabularyCapturePolicy.Normalized(entry.Expression)]);
    private static string ExampleKey(VocabularyExample example) => Key([example.Text,
        string.Join(",", example.Ranges.Select(r => r.Location.ToString(CultureInfo.InvariantCulture) + ":" + r.Length.ToString(CultureInfo.InvariantCulture))),
        example.Completeness switch { FrozenCompleteness.Sentence => "sentence", FrozenCompleteness.Fragment => "fragment", FrozenCompleteness.Unknown => "unknown", _ => throw Invalid() }]);
    private static string Kind(VocabularyEntryKind kind) => kind switch
    { VocabularyEntryKind.Word => "word", VocabularyEntryKind.Expression => "expression", VocabularyEntryKind.Pattern => "pattern", VocabularyEntryKind.Sentence => "sentence", _ => throw Invalid() };
    private static string Key(IReadOnlyList<string> values)
    {
        var text = string.Concat(values.Select(value => Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + ":" + value));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    private static bool Fits(string? value, int maximum = int.MaxValue) => LearningExplanationText.Fits(value, maximum);
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
}
