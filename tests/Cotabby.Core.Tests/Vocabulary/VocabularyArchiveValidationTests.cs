using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyArchiveValidationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static VocabularyEntry Entry() => new(Guid.NewGuid(), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
        [new(Guid.NewGuid(), "", Now, Now)], Now, Now);
    private static VocabularyDocument Document(VocabularyEntry entry) => new() { Entries = [entry] };
    private static void Invalid(Action action) => Assert.Equal(VocabularyFailure.InvalidArchive, Assert.Throws<VocabularyException>(action).Failure);

    [Fact]
    public void ValidatingSnapshotNeverChangesRawTextOptionalPresenceOrIdentity()
    {
        var entry = Entry(); var sense = entry.Senses[0] with { Meaning = "cafe\u0301", Tags = ["cafe\u0301"], Content = null, ReviewCards = [] };
        var document = Document(entry with { Senses = [sense] });
        VocabularyArchiveValidation.Validate(document);
        Assert.Same(sense, document.Entries[0].Senses[0]); Assert.Null(sense.Content); Assert.Empty(sense.ReviewCards!);
        Assert.Equal("cafe\u0301", sense.Meaning); Assert.Equal("cafe\u0301", sense.Tags[0]);
    }

    [Fact]
    public void DotNetNullMalformedTextAndUndefinedEnumsHaveRecoverableArchiveFailure()
    {
        var entry = Entry(); var sense = entry.Senses[0]; string malformed = new((char)0xD800, 1);
        VocabularyEntry[] invalid = [entry with { Expression = null! }, entry with { DictionaryForm = malformed },
            new(entry.Id, entry.Expression, entry.DictionaryForm, (LearningLanguage)999, entry.Kind, entry.Senses, Now, Now),
            entry with { Senses = [null!] }, entry with { Senses = [sense with { Tags = [null!] }] },
            entry with { Senses = [sense with { Meaning = malformed }] }, entry with { Senses = [sense with { MeaningOrigin = (VocabularyMeaningOrigin)999 }] },
            entry with { Senses = [sense with { StandaloneMeanings = [new("", (LearningLanguage)999)] }] },
            entry with { Senses = [sense with { Explanations = [null!] }] }];
        foreach (var value in invalid) Invalid(() => VocabularyArchiveValidation.Validate(Document(value)));
        Invalid(() => VocabularyArchiveValidation.Validate(null!));
        Assert.Equal("bank", entry.Expression); Assert.Empty(sense.Tags);
    }

    [Fact]
    public void ReviewDotNetOnlyInvalidNestedValuesNeverEscapeAsNativeOrNullReferenceErrors()
    {
        var plan = ReviewPlan.Create(); var review = new VocabularyReview(plan);
        VocabularyReview[] invalid = [review with { Plan = null! }, review with { PreservedPlans = [null!] }, review with { Bindings = [null!] },
            review with { Plan = plan with { Name = new((char)0xD800, 1) } },
            review with { Plan = plan with { AudioKinds = new HashSet<ReviewAudioKind> { (ReviewAudioKind)999 } } },
            review with { Plan = plan with { AudioReceipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt> { [ReviewAudioKind.Expression] = null! } } }];
        foreach (var value in invalid) Invalid(() => VocabularyArchiveValidation.ValidateReview(value));
        Invalid(() => VocabularyArchiveValidation.ValidateTemplates([null!]));
        var layout = new ReviewLayout([new(Guid.NewGuid(), (ReviewContentSource)999)]);
        Assert.Equal(VocabularyFailure.InvalidContent, Assert.Throws<VocabularyException>(() => ReviewLayoutPolicy.Validate(layout)).Failure);
        Assert.False(AnkiArchiveEndpointPolicy.IsValid(null)); Assert.False(VocabularyArchiveValidation.IsValidAudioFilename(null));
    }

    [Fact]
    public void DifferentReviewLayoutsMaySharePartAndCustomBlockIdsAndDanglingSourceReferences()
    {
        var entry = Entry(); var partId = Guid.NewGuid(); var block = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("Literal")));
        var layout = new ReviewLayout([new(partId, ReviewContentSource.Custom, content: [block]),
            new(Guid.NewGuid(), ReviewContentSource.SenseText, Guid.NewGuid())]);
        var a = new VocabularyReview(ReviewPlan.Create() with { Layout = layout, ExampleId = Guid.NewGuid() });
        var b = new VocabularyReview(ReviewPlan.Create() with { Layout = layout });
        VocabularyArchiveValidation.Validate(Document(entry with { Senses = [entry.Senses[0] with { ReviewCards = [a, b] }] }));
        Assert.Same(layout, a.Plan.Layout); Assert.Same(layout, b.Plan.Layout);
    }
}
