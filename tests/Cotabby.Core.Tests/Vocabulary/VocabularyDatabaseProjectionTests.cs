using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyDatabaseProjectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static VocabularyDocument Document()
    {
        var usage = new VocabularyExample(Guid.NewGuid(), "The bank.", "", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "", Now);
        return new() { Entries = [new(Guid.NewGuid(), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
            [new(Guid.NewGuid(), "", Now, Now) { Examples = [usage] }], Now, Now)] };
    }
    private static void Invalid(Action action) => Assert.Equal(VocabularyFailure.InvalidArchive, Assert.Throws<VocabularyException>(action).Failure);

    [Fact]
    public void SingleNoteEditChangesOnlyItsSenseRowAndDoesNotRewriteUsagePayload()
    {
        var d = Document(); var e = d.Entries[0]; var original = VocabularyDatabaseProjection.Rows(d);
        var changed = VocabularyDatabaseProjection.Rows(d with { Entries = [e with { Senses = [e.Senses[0] with { Note = "My note" }] }] });
        var row = Assert.Single(changed.Values, row => row != original[row.Key]);
        Assert.Equal(VocabularyDatabaseTable.Senses, row.Table);
        var originalUsage = original.Values.Single(row => row.Table == VocabularyDatabaseTable.Usages);
        Assert.Equal(originalUsage, changed[originalUsage.Key]);
        Assert.Equal(d, VocabularyDatabaseProjection.Document(original.Values.ToArray(), false));
    }

    [Fact]
    public void UsageMoveRetainsRowIdentityAndChangesOnlyItsParentWhenMetadataIsUnchanged()
    {
        var d = Document(); var e = d.Entries[0]; var source = e.Senses[0]; var target = new VocabularySense(Guid.NewGuid(), "", Now, Now);
        d = d with { Entries = [e with { Senses = [source, target] }] }; var before = VocabularyDatabaseProjection.Rows(d);
        var moved = d with { Entries = [e with { Senses = [source with { Examples = [] }, target with { Examples = source.Examples }] }] };
        var after = VocabularyDatabaseProjection.Rows(moved); var row = Assert.Single(after.Values, row => row != before[row.Key]);
        Assert.Equal(VocabularyDatabaseTable.Usages, row.Table); Assert.Equal(before[row.Key].Payload, row.Payload);
        Assert.Equal(target.Id.ToString("D").ToUpperInvariant(), row.ParentId); Assert.Equal(moved, VocabularyDatabaseProjection.Document(after.Values.ToArray(), false));
    }

    [Fact]
    public void LeafRowsAreFrozenAndCanonicalKeyAndExtraEqualityMatchSwiftValues()
    {
        var d = Document(); var row = VocabularyDatabaseProjection.Rows(d).Values.First(); string[] extra = ["cafe\u0301"];
        var first = row with { Id = "cafe\u0301", ParentId = "cafe\u0301", Extra = extra };
        var second = row with { Id = "café", ParentId = "café", Extra = ["café"] };
        extra[0] = "mutated";
        Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode()); Assert.Equal(first.Key, second.Key);
        Assert.Equal(first.Key.GetHashCode(), second.Key.GetHashCode()); Assert.Equal("cafe\u0301", first.Extra[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)first.Extra)[0] = "changed");
        Assert.NotEqual(first.Key, new VocabularyDatabaseKey(VocabularyDatabaseTable.Bindings, first.Id));
    }

    [Fact]
    public void MissingBindingIsAProjectionButFailsSeparateWholeArchiveAdmission()
    {
        var d = Document(); var e = d.Entries[0]; var binding = AnkiNoteBinding.Create(e.Senses[0].Id, "http://localhost", "profile", 1, "model", "deck", "ID",
            new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID" }, new Dictionary<string, string> { ["ID"] = e.Senses[0].Id.ToString("D") });
        var review = new VocabularyReview(ReviewPlan.Create(), [binding], binding.Id);
        d = d with { Entries = [e with { Senses = [e.Senses[0] with { Review = review }] }] }; VocabularyArchiveValidation.Validate(d);
        var rows = VocabularyDatabaseProjection.Rows(d).Values.Where(row => row.Table != VocabularyDatabaseTable.Bindings).ToArray();
        var reconstructed = VocabularyDatabaseProjection.Document(rows, false);
        Assert.Empty(reconstructed.Entries[0].Senses[0].Review!.Bindings);
        Invalid(() => VocabularyArchiveValidation.Validate(reconstructed));
    }

    [Fact]
    public void DotNetOnlyNullRowsAndPayloadNodesHaveRecoverableFailure()
    {
        var d = Document(); var e = d.Entries[0]; var s = e.Senses[0]; var rows = VocabularyDatabaseProjection.Rows(d).Values.ToArray();
        Invalid(() => VocabularyDatabaseProjection.Document(null!, false));
        Invalid(() => VocabularyDatabaseProjection.Document([null!], false));
        Invalid(() => VocabularyDatabaseProjection.Document([rows[0] with { Table = (VocabularyDatabaseTable)999 }], false));
        Invalid(() => VocabularyDatabaseProjection.Document(rows.Select(row => row.Table == VocabularyDatabaseTable.Entries ? row with { Payload = new VocabularyDatabasePayload.Entry(null!) } : row).ToArray(), false));
        Invalid(() => VocabularyDatabaseProjection.Rows(d with { Entries = [e with { Senses = [s with { Content = [null!] }] }] }));
        Invalid(() => VocabularyDatabaseProjection.Rows(d with { Entries = [e with { Senses = [s with { ReviewCards = [null!] }] }] }));
        Invalid(() => VocabularyDatabaseProjection.Rows(d with { Entries = [e with { Senses = [s with { Review = new(null!) }] }] }));
        Invalid(() => VocabularyDatabaseProjection.Rows(d with { ReviewTemplates = [null!] }));
    }
}
