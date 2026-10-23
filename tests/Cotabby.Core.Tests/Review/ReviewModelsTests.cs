using System.Text.Json;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Review;

public sealed class ReviewModelsTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("answer")]
    [InlineData("name")]
    [InlineData("deck")]
    [InlineData("layout")]
    [InlineData("includeContent")]
    [InlineData("audioKinds")]
    [InlineData("audioReceipts")]
    public void OptionalPlanExtensionsDoNotCollapseAbsentIntoEmptyOrFalse(string extension)
    {
        var original = new ReviewPlan(Id);
        var changed = extension switch
        {
            "answer" => original with { Answer = "" }, "name" => original with { Name = "" },
            "deck" => original with { Deck = "" }, "layout" => original with { Layout = new() },
            "includeContent" => original with { IncludeSenseContent = false },
            "audioKinds" => original with { AudioKinds = new HashSet<ReviewAudioKind>() },
            _ => original with { AudioReceipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt>() },
        };
        Assert.NotEqual(original, changed); Assert.True(original.Recognition); Assert.False(original.Gap);
        Assert.False(original.IncludeNotes); Assert.False(original.IncludeExplanation); Assert.Null(original.ExampleId);
    }

    [Fact]
    public void MapsAndSetsCompareByValueWithCanonicalStringsAndSnapshotsDetachDrafts()
    {
        var fields = new Dictionary<string, string> { ["cafe\u0301"] = "cafe\u0301", ["other"] = "literal" };
        var kinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression, ReviewAudioKind.Sentence };
        var receipt = new ReviewAudioReceipt("fingerprint", "hash", "loopback", "profile", "audio.wav");
        var receipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt> { [ReviewAudioKind.Expression] = receipt };
        var first = new ReviewPlan(Id) { FieldOverrides = fields, AudioKinds = kinds, AudioReceipts = receipts };
        var same = new ReviewPlan(Id) { FieldOverrides = new Dictionary<string, string> { ["other"] = "literal", ["café"] = "café" },
            AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Sentence, ReviewAudioKind.Expression },
            AudioReceipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt> { [ReviewAudioKind.Expression] = receipt with { } } };
        var clone = first with { FieldOverrides = fields, AudioKinds = kinds, AudioReceipts = receipts };
        fields.Clear(); kinds.Clear(); receipts.Clear();
        Assert.Equal(first, same); Assert.Equal(first.GetHashCode(), same.GetHashCode()); Assert.Equal(first, clone);
        Assert.Equal(2, first.FieldOverrides.Count); Assert.Equal(2, first.AudioKinds!.Count); Assert.Single(first.AudioReceipts!);
        Assert.Equal("cafe\u0301", first.FieldOverrides["café"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)first.FieldOverrides).Add("new", "value"));
    }

    [Fact]
    public void AmbiguousCanonicalDictionaryKeysRejectWithoutLeakingUserFieldNames()
    {
        var source = new Dictionary<string, string> { ["private-cafe\u0301"] = "first", ["private-café"] = "second" };
        var error = Assert.Throws<ArgumentException>(() => new ReviewPlan(Id) { FieldOverrides = source });
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public void LayoutRetainsFrontBackAndCustomBlockOrderWhileAudioProjectionIsASet()
    {
        var a = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("front")));
        var b = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("back")));
        var first = ReviewCardPart.Create(ReviewContentSource.ExpressionAudio) with { Content = [a] };
        var second = ReviewCardPart.Create(ReviewContentSource.SentenceAudio);
        var third = ReviewCardPart.Create(ReviewContentSource.ExpressionAudio) with { Content = [b] };
        var draft = new List<ReviewCardPart> { first, second };
        var layout = new ReviewLayout(draft, [third]); draft.Clear();
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, layout.Parts.Select(part => part.Id));
        Assert.Equal(new[] { a.Id, b.Id }, layout.CustomContent.Select(block => block.Id));
        Assert.True(layout.AudioKinds.SetEquals(new[] { ReviewAudioKind.Expression, ReviewAudioKind.Sentence }));
        Assert.NotEqual(layout, layout with { Front = layout.Front.Reverse().ToArray() });
        Assert.Null(first.BlockId); Assert.False(first.ShowCaption);
        Assert.Equal(ReviewField.ExpressionAudio, ReviewAudioKind.Expression.Field());
        Assert.Equal(ReviewField.SentenceAudio, ReviewAudioKind.Sentence.Field());
        Assert.Equal("\"LingroveID\"", JsonSerializer.Serialize(ReviewField.Identity));
        Assert.Equal("\"contextualMeaning\"", JsonSerializer.Serialize(ReviewContentSource.ContextualMeaning));
        Assert.Equal("\"expression\"", JsonSerializer.Serialize(ReviewAudioKind.Expression));
    }

    [Fact]
    public void UnchosenOrMissingBindingNeverUsesTheFirstArrayElementImplicitly()
    {
        var binding = Binding(); var review = new VocabularyReview(new(Id), [binding]);
        Assert.Null(review.ActiveBinding); Assert.True(review.NeedsBindingChoice);
        Assert.True((review with { ActiveBindingId = Guid.NewGuid() }).NeedsBindingChoice);
        Assert.False((review with { ActiveBindingId = binding.Id }).NeedsBindingChoice);
        Assert.Same(binding, (review with { ActiveBindingId = binding.Id }).ActiveBinding);
        Assert.False(new VocabularyReview(new(Id)).NeedsBindingChoice);
    }

    [Fact]
    public void BindingReceiptDetachesMapsAndPreservesOriginalScopeAndInt64RemoteIdentity()
    {
        var mapping = new Dictionary<ReviewField, string> { [ReviewField.Identity] = "Identity" };
        var fields = new Dictionary<string, string> { ["Identity"] = "old-sense" };
        var binding = new AnkiNoteBinding(Id, Guid.NewGuid(), "loopback", "profile", long.MaxValue, "model", "deck", "Identity", mapping, fields);
        var changed = binding with { Deck = "new deck", Fields = fields, SyncedAt = DateTimeOffset.UnixEpoch };
        mapping.Clear(); fields.Clear();
        Assert.Single(binding.Mapping); Assert.Single(binding.Fields); Assert.Single(changed.Fields);
        Assert.Equal(binding.OriginSenseId, changed.OriginSenseId); Assert.Equal(binding.Endpoint, changed.Endpoint);
        Assert.Equal(binding.Profile, changed.Profile); Assert.Equal(long.MaxValue, changed.NoteId); Assert.Null(binding.SyncedAt);
        Assert.NotEqual(binding, changed); Assert.NotEqual(ReviewPlan.Create().Id, ReviewPlan.Create().Id);
    }

    private static AnkiNoteBinding Binding() => new(Guid.NewGuid(), Guid.NewGuid(), "loopback", "profile", 1,
        "model", "deck", "Identity", new Dictionary<ReviewField, string>(), new Dictionary<string, string>());
}
