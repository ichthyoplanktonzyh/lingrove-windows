using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;

using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

public static partial class VocabularyArchiveValidation
{
    // Separate CLR representability from Mac semantic budgets. Perform this pass on canonical
    // values before projection so malformed nulls cannot be hidden by an uncollected claim.
    private static void ValidateRepresentability(VocabularyDocument? document)
    {
        if (document is null || document is not { } value || document.Classification is not { } classification
            || value.Semantics is null || value.Catalog.Any(entry => !Entry(entry))
            || value.Sources.Any(source => source is null || !Fits(source.Text) || !Optional(source.SourceApplicationId) || !Enum.IsDefined(source.Kind))
            || value.Occurrences.Any(occurrence => occurrence is null)
            || value.Interpretations.Any(claim => !Claim(claim)) || value.Collections.Any(collection => !Collection(collection))
            || value.ReviewTemplates?.Any(template => !Template(template)) == true
            || !Semantic(value.Semantics) || !Classification(classification)) throw Invalid();
    }
    private static bool Optional(string? value, int maximum = int.MaxValue) => value is null || Fits(value, maximum);
    private static bool Language(LearningLanguage? value) => value is null || Enum.IsDefined(value.Value);
    private static bool Meaning(VocabularyMeaning? value) => value is not null && Fits(value.Text) && Language(value.Language);
    private static bool Record(LearningExplanationRecord? record) => record is { Input: not null, Result: not null }
        && Enum.IsDefined(record.Language) && Fits(record.Model) && Fits(record.Result.Meaning) && Fits(record.Result.Explanation)
        && Fits(record.Input.Target) && Fits(record.Input.DictionaryForm) && Fits(record.Input.Context) && Optional(record.Input.OccurrenceIdentity)
        && Language(record.Input.Language) && Enum.IsDefined(record.Input.Kind) && Enum.IsDefined(record.Input.ContextKind);
    private static bool Entry(VocabularyEntry? entry) => entry is not null && Fits(entry.Expression) && Fits(entry.DictionaryForm)
        && Language(entry.Language) && Enum.IsDefined(entry.Kind) && entry.Senses.All(Sense);
    private static bool Sense(VocabularySense? sense) => sense is not null && Fits(sense.Meaning) && Fits(sense.Note)
        && Language(sense.MeaningLanguage) && Enum.IsDefined(sense.MeaningOrigin) && sense.Tags.All(tag => Fits(tag))
        && sense.StandaloneMeanings.All(Meaning) && sense.AlternateMeanings.All(Meaning) && sense.Explanations.All(Record)
        && sense.Examples.All(Example) && (sense.Content is null || sense.Content.All(Block))
        && (sense.Review is null || Review(sense.Review)) && (sense.ReviewCards is null || sense.ReviewCards.All(Review));
    private static bool Example(VocabularyExample? example) => example is not null && Fits(example.Text) && Fits(example.Translation)
        && Fits(example.Meaning) && Optional(example.SourceApplicationId) && Language(example.MeaningLanguage) && Language(example.TranslationLanguage)
        && Enum.IsDefined(example.SourceKind) && Enum.IsDefined(example.Completeness) && Enum.IsDefined(example.MeaningOrigin)
        && example.TranslationVariants.All(Meaning) && example.Explanations.All(Record);
    private static bool Provenance(VocabularySemanticProvenance? value) => value is not null && Fits(value.Source)
        && Enum.IsDefined(value.Origin) && Enum.IsDefined(value.Status);
    private static bool Claim(VocabularyInterpretation? claim) => claim is not null && Fits(claim.Meaning) && Fits(claim.Translation)
        && Enum.IsDefined(claim.Status) && Enum.IsDefined(claim.MeaningOrigin) && Enum.IsDefined(claim.Completeness)
        && Language(claim.MeaningLanguage) && Language(claim.TranslationLanguage) && Provenance(claim.Provenance)
        && claim.TranslationVariants.All(Meaning) && claim.Explanations.All(Record);
    private static bool Collection(VocabularyCollectionItem? collection) => collection is not null && Fits(collection.Note)
        && collection.Tags.All(tag => Fits(tag)) && (collection.Content is null || collection.Content.All(Block))
        && (collection.Review is null || Review(collection.Review)) && (collection.ReviewCards is null || collection.ReviewCards.All(Review));
    private static bool Block(VocabularyContentBlock? block) => block?.Content switch
    {
        VocabularyBlockContent.Text { Value: { } text } => Fits(text.Text) && Enum.IsDefined(text.Style),
        VocabularyBlockContent.Image { Reference: { } reference } image => Fits(reference.Digest) && Fits(image.Caption),
        _ => false,
    };
    private static bool Layout(ReviewLayout? layout) => layout is not null && layout.Front.All(Part) && layout.Back.All(Part);
    private static bool Part(ReviewCardPart? part) => part is not null && Enum.IsDefined(part.Source) && part.Content.All(Block);
    private static bool Template(ReviewLayoutTemplate? template) => template is not null && Fits(template.Name) && Layout(template.Layout);
    private static bool Plan(ReviewPlan? plan) => plan is not null && Optional(plan.Name) && Optional(plan.Answer) && Optional(plan.Deck)
        && (plan.Layout is null || Layout(plan.Layout)) && plan.FieldOverrides.All(pair => Fits(pair.Key) && Fits(pair.Value))
        && (plan.AudioKinds is null || plan.AudioKinds.All(Enum.IsDefined))
        && (plan.AudioReceipts is null || plan.AudioReceipts.All(pair => Enum.IsDefined(pair.Key) && Receipt(pair.Value)));
    private static bool Receipt(ReviewAudioReceipt? receipt) => receipt is not null && Fits(receipt.Fingerprint) && Fits(receipt.TextHash)
        && Fits(receipt.Endpoint) && Fits(receipt.Profile) && Fits(receipt.Filename);
    private static bool Binding(AnkiNoteBinding? binding) => binding is not null && Fits(binding.Endpoint) && Fits(binding.Profile)
        && Fits(binding.Model) && Fits(binding.Deck) && Fits(binding.IdentityField)
        && binding.Mapping.All(pair => Enum.IsDefined(pair.Key) && Fits(pair.Value))
        && binding.Fields.All(pair => Fits(pair.Key) && Fits(pair.Value));
    private static bool Review(VocabularyReview? review) => review is not null && Plan(review.Plan)
        && review.PreservedPlans.All(Plan) && review.Bindings.All(Binding);

    private static bool Label(VocabularyMeaningLabel? label) => label is not null && Fits(label.Text) && Language(label.Language);
    private static bool Semantic(VocabularySemanticIndex index) => index.Nodes.All(node => node is not null && Enum.IsDefined(node.Kind)
        && Fits(node.Definition) && Fits(node.ScopeNote) && Provenance(node.Provenance) && node.Labels.All(Label)
        && node.Roles.All(role => role is not null && Fits(role.Name) && Fits(role.Description)))
        && index.Patterns.All(pattern => pattern is not null && Fits(pattern.Template) && Fits(pattern.Region) && Fits(pattern.Note)
            && Enum.IsDefined(pattern.Formality) && Enum.IsDefined(pattern.Medium) && Provenance(pattern.Provenance)
            && pattern.Collocations.All(value => Fits(value)) && pattern.Slots.All(slot => slot is not null && Fits(slot.Name) && Fits(slot.Constraint)))
        && index.Mappings.All(mapping => mapping is not null && Enum.IsDefined(mapping.Relation) && Fits(mapping.Conditions) && Fits(mapping.Nuance)
            && Provenance(mapping.Provenance) && mapping.RoleBindings.All(binding => binding is not null))
        && index.Relations.All(relation => relation is not null && Enum.IsDefined(relation.Kind) && Fits(relation.Note) && Provenance(relation.Provenance))
        && index.Cues.All(cue => cue is not null && Fits(cue.Text) && Language(cue.Language) && Enum.IsDefined(cue.Kind))
        && index.CueTargets.All(target => target is not null && Fits(target.Conditions) && Provenance(target.Provenance));
    private static bool Classification(VocabularyClassificationIndex index) => index.Frameworks.All(framework => framework is not null && Fits(framework.Key))
        && index.Categories.All(category => category is not null && Enum.IsDefined(category.Axis) && Fits(category.Definition)
            && Optional(category.Key) && category.Labels.All(Label) && Provenance(category.Provenance))
        && index.Parents.All(parent => parent is not null)
        && index.Memberships.All(membership => membership is not null && Provenance(membership.Provenance))
        && index.Overrides.All(overlay => overlay is not null && Optional(overlay.Definition) && (overlay.Labels is null || overlay.Labels.All(Label)));
}
