using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class VocabularyFrozenArchiveValidationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static R.VocabularyFrozenDocument Fixture()
    {
        var sense = new R.VocabularySense(Id(2), "greeting", Now, Now) { CreatedWithoutContext = true };
        var entry = new R.VocabularyEntry(Id(1), "hello", "hello", null, VocabularyEntryKind.Word, [sense], Now, Now);
        var source = new R.VocabularySourceSnapshot(Id(3), "hello", R.VocabularySourceKind.Imported, null, Now);
        var claim = new R.VocabularyInterpretation(Id(5), Id(4), Now) { SenseId = sense.Id, Status = R.VocabularyInterpretationStatus.Accepted };
        return new(new R.VocabularyDocument { Catalog = [entry], Sources = [source], Occurrences = [new(Id(4), source.Id, [new(0,5)])], Interpretations = [claim] }, new());
    }
    private static void Reject(R.VocabularyFrozenDocument value, VocabularyFailure failure = VocabularyFailure.InvalidArchive) =>
        Assert.Equal(failure, Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenArchiveValidation.Validate(value)).Failure);
    [Fact]
    public void ValidNoncharactersPreserveNfcLexicalAndSemanticIdentity()
    {
        var value = Fixture(); var r = value.Resources;
        var composed = new R.VocabularyEntry(Id(30), "\ufffeé", "", null, VocabularyEntryKind.Word, [], Now, Now);
        var decomposed = new R.VocabularyEntry(Id(31), "\ufffee\u0301", "", null, VocabularyEntryKind.Word, [], Now, Now);
        var one = value with { Resources = r with { Catalog = [.. r.Catalog, composed] } };
        R.VocabularyFrozenArchiveValidation.Validate(one);
        Reject(one with { Resources = one.Resources with { Catalog = [.. one.Resources.Catalog, decomposed] } });

        var pattern = new R.VocabularyUsagePattern(Id(2), "template") { Slots = [new("\ufffeé"), new("\ufffee\u0301")] };
        Reject(value with { Resources = r with { Semantics = new() { Patterns = [pattern] } } }, VocabularyFailure.InvalidContent);
        Reject(value with { Resources = r with { Sources = [new(Id(3), "\ud800", R.VocabularySourceKind.Imported, null, Now)] } });
    }
    [Fact]
    public void LifecycleAndClassificationAreCheckedAgainstTheSameFrozenGraph()
    {
        var value = Fixture(); var r = value.Resources; var claim = r.Interpretations[0];
        Reject(value with { Resources = r with { Occurrences = [new(Id(4), Id(3), [new(99, 1)])] } });
        Reject(value with { Resources = r with { Interpretations = [claim, new(Id(22), Id(4), Now) { SenseId = Id(2), Status = R.VocabularyInterpretationStatus.Accepted }] } });
        Reject(value with { Resources = r with { Collections = [new(Id(2), Now) { InterpretationIds = [Id(999)] }] } });
        var node = new R.VocabularyMeaningNode(Id(10), [new("Meaning")]); var category = new R.VocabularyCategory(Id(11), R.VocabularyCategoryAxis.Content, [new("Category")]);
        var valid = value with { Resources = r with { Semantics = new() { Nodes = [node] } }, Classification = new() { Categories = [category], Memberships = [new(Id(12), node.Id, category.Id)] } };
        R.VocabularyFrozenArchiveValidation.Validate(valid);
        Reject(valid with { Classification = valid.Classification with { Memberships = [new(Id(12), Id(999), category.Id)] } }, VocabularyFailure.InvalidContent);
    }
    [Fact]
    public void ReviewIdentityScopesAndLegacyPlansArePreserved()
    {
        var value = Fixture(); var r = value.Resources;
        var plan = new ReviewPlan(Id(20)) { Layout = new() }; var legacy = new VocabularyReview(plan);
        var collection = new R.VocabularyCollectionItem(Id(2), Now) { Review = legacy, ReviewCards = [new(plan)] };
        R.VocabularyFrozenArchiveValidation.Validate(value with { Resources = r with { Collections = [collection] } });
        Reject(value with { Resources = r with { Collections = [collection with { ReviewCards = [new(plan), new(plan)] }] } });
        var duplicatedTemplates = new[] { new ReviewLayoutTemplate(Id(21), "First", new()), new ReviewLayoutTemplate(Id(21), "Second", new()) };
        Reject(value with { Resources = r with { ReviewTemplates = duplicatedTemplates } });
    }
    [Fact]
    public void UncollectedEvidenceAndNullLanguageRemainValidAndAreJoinedForSemanticMappings()
    {
        var value = Fixture(); var node = new R.VocabularyMeaningNode(Id(6), [new("Greeting") { Id = Id(7) }]);
        value = value with { Resources = value.Resources with { Semantics = new() { Nodes = [node], Mappings = [new(node.Id, Id(2)) { Id = Id(8), EvidenceIds = [Id(5)] }] } } };
        R.VocabularyFrozenArchiveValidation.Validate(value);
        Assert.Empty(value.Resources.CollectedEntries); Assert.Single(value.Resources.Entries[0].Examples); Assert.Null(value.Resources.Entries[0].Language);
        var noSenseEntry = new R.VocabularyEntry(Id(9), "empty", "empty", null, VocabularyEntryKind.Word, [], Now, Now);
        R.VocabularyFrozenArchiveValidation.Validate(value with { Resources = value.Resources with { Catalog = [.. value.Resources.Catalog, noSenseEntry] } });
    }
    [Fact]
    public void JoinedGeneralAndUnknownContextExplanationsRequireExactSourceDerivedInput()
    {
        var value = Fixture(); var entry = value.Resources.Catalog[0]; var sense = entry.Senses[0];
        var result = new LearningExplanationResult("greeting", "A greeting.");
        var general = new R.LearningExplanationInput("hello", "hello", null, VocabularyEntryKind.Word, "hello", [new(0,5)], R.LearningExplanationContext.None,
            "sense:" + sense.Id.ToString("D").ToUpperInvariant());
        var unknown = new R.LearningExplanationInput("hello", "hello", null, VocabularyEntryKind.Word, "hello", [new(0,5)], R.LearningExplanationContext.Unknown,
            "c4a7706c333e599abcbeb7c0fb75d8fdeb9ccb662ea94ba0fcc814ad1dccbe22");
        var generalRecord = new R.LearningExplanationRecord(Id(9), general, LearningLanguage.English, result, "model", Now);
        var unknownRecord = new R.LearningExplanationRecord(Id(10), unknown, LearningLanguage.English, result, "model", Now);
        var claim = value.Resources.Interpretations[0] with { Explanations = [unknownRecord] };
        var resources = value.Resources with { Catalog = [entry with { Senses = [sense with { Explanations = [generalRecord] }] }], Interpretations = [claim] };
        R.VocabularyFrozenArchiveValidation.Validate(value with { Resources = resources });
        var wrong = new R.LearningExplanationInput("hello", "hello", null, VocabularyEntryKind.Word, "hello", [new(0,5)], R.LearningExplanationContext.Fragment, unknown.OccurrenceIdentity);
        Reject(value with { Resources = resources with { Interpretations = [claim with { Explanations = [unknownRecord with { Input = wrong }] }] } });
    }
    [Fact]
    public void IdentityAndOwnershipChecksRejectDuplicateEntriesAliasesAndBlocks()
    {
        var value = Fixture(); var resources = value.Resources; var entry = resources.Catalog[0]; var sense = entry.Senses[0];
        var duplicate = new R.VocabularyEntry(Id(20), "hello", "other", null, VocabularyEntryKind.Word, [], Now, Now);
        Reject(value with { Resources = resources with { Catalog = [entry, duplicate] } });
        Reject(value with { Resources = resources with { Catalog = [entry with { Senses = [sense with { MergedFrom = [entry.Id] }] }] } });
        var block = new VocabularyContentBlock(sense.Id, new VocabularyBlockContent.Text(new("content")));
        Reject(value with { Resources = resources with { Collections = [new R.VocabularyCollectionItem(sense.Id, Now) { Content = [block] }] } });
        var uncollected = resources with { Interpretations = [resources.Interpretations[0] with { Status = R.VocabularyInterpretationStatus.Candidate, SenseId = null }] };
        R.VocabularyFrozenArchiveValidation.Validate(value with { Resources = uncollected });
    }
    [Fact]
    public void ReviewTemplateMediaAndSemanticFailuresCrossTheSameBoundary()
    {
        var value = Fixture(); var resources = value.Resources;
        var badImage = new VocabularyContentBlock(Id(11), new VocabularyBlockContent.Image(new("bad", 1, 1, 1)));
        Reject(value with { Resources = resources with { Collections = [new(Id(2), Now) { Content = [badImage] }] } }, VocabularyFailure.InvalidContent);
        var card = new VocabularyReview(new ReviewPlan(Id(12)));
        Reject(value with { Resources = resources with { Collections = [new(Id(2), Now) { ReviewCards = [card] }] } });
        Reject(value with { Resources = resources with { ReviewTemplates = [new(Id(13), "", new())] } });
        var node = new R.VocabularyMeaningNode(Id(14), [new("node")]);
        Reject(value with { Resources = resources with { Semantics = new() { Nodes = [node], Mappings = [new(node.Id, Id(2)) { EvidenceIds = [Id(999)] }] } } }, VocabularyFailure.InvalidContent);
    }
    [Fact]
    public void MalformedClrValuesAreRejectedEvenInUncollectedResources()
    {
        var value = Fixture(); var r = value.Resources; var claim = r.Interpretations[0]; var sense = r.Catalog[0].Senses[0]; var entry = r.Catalog[0];
        Reject(value with { Resources = r with { Catalog = [null!] } });
        Reject(value with { Resources = r with { Catalog = [entry with { Senses = [sense with { Meaning = "\ud800" }] }] } });
        Reject(value with { Resources = r with { Interpretations = [claim with { MeaningLanguage = (LearningLanguage)999 }] } });
        Reject(value with { Resources = r with { Interpretations = [claim with { Provenance = null! }] } });
        Reject(value with { Resources = r with { Interpretations = [claim with { TranslationVariants = [null!] }] } });
        Reject(value with { Resources = r with { Semantics = new() { Nodes = [new(Id(20), [new("valid")]) { Provenance = null! }] } } });
        Reject(value with { Classification = new() { Categories = [new(Id(21), (R.VocabularyCategoryAxis)999, [new("valid")])] } });
        Reject(value with { Resources = r with { ReviewTemplates = [new(Id(22), "name", null!)] } });
    }
}
