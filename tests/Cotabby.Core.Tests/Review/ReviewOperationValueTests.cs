using Cotabby.Core.Review;

namespace Cotabby.Core.Tests.Review;

public sealed class ReviewOperationValueTests
{
    [Fact]
    public void OperationAndReadValuesOwnCollectionsAndKeepCanonicalEqualityWithoutPrintingAKey()
    {
        var mapping = new Dictionary<ReviewField, string> { [ReviewField.Identity] = "e\u0301" };
        var connection = new AnkiConnection("http://localhost", "é-secret");
        var scope = new ReviewOperationScope(connection, "Profile", "Custom", mapping, null, "Default", false, true, false);
        var equivalent = new ReviewOperationScope(new("http://localhost", "e\u0301-secret"), "Profile", "Custom", new Dictionary<ReviewField, string> { [ReviewField.Identity] = "é" }, null, "Default", false, true, false);
        mapping[ReviewField.Identity] = "changed";
        Assert.Equal("e\u0301", scope.Mapping[ReviewField.Identity]); Assert.Equal(scope, equivalent); Assert.Equal(scope.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(scope, new ReviewOperationScope(connection, "Profile", "Custom", scope.Mapping, null, "Default", true, true, false));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<ReviewField, string>)scope.Mapping).Add(ReviewField.Meaning, "new"));
        Assert.DoesNotContain("secret", connection.ToString()); Assert.DoesNotContain("secret", scope.ToString());
        string[] decks = ["e\u0301"]; var fields = new Dictionary<string, string> { ["e\u0301"] = "é" };
        var catalog = new AnkiReviewCatalog("Profile", decks, ["Custom"]); var note = new AnkiRemoteNote(10, "Custom", fields);
        decks[0] = "changed"; fields.Clear();
        Assert.Equal("e\u0301", catalog.Decks[0]); Assert.Equal("é", note.Fields["é"]);
        Assert.Equal(catalog, new AnkiReviewCatalog("Profile", ["é"], ["Custom"]));
        Assert.Equal(note, new AnkiRemoteNote(10, "Custom", new Dictionary<string, string> { ["é"] = "e\u0301" }));
    }
}
