using System.Text;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;
using Cotabby.Tests.Shared;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyBackupCodecTests
{
    [Fact]
    public void WholeGraphRoundTripRetainsChildrenAndReviewRelationshipsWithoutDatabaseProjection()
    {
        var document = VocabularyReferenceGraph.Document();
        var decoded = VocabularyBackupCodec.DecodeDocument(VocabularyBackupCodec.EncodeDocument(document));
        Assert.Equal(document, decoded);
        Assert.Single(decoded.Entries[0].Senses); Assert.Single(decoded.Entries[0].Examples);
        Assert.Single(decoded.Entries[0].Senses[0].Review!.Bindings); Assert.Single(decoded.ReviewTemplates!);
        Assert.NotNull(decoded.Entries[0].Senses[0].Content); Assert.NotNull(decoded.Entries[0].Senses[0].ReviewCards);
    }
    [Fact]
    public void InputAndReturnedMediaArraysCannotMutateCapturedBackupOrEncodedTransport()
    {
        byte[] bytes = [1, 2, 3]; var media = new Dictionary<string, byte[]> { ["fixture"] = bytes };
        var backup = new VocabularyBackup(new(), media); byte[] encoded = VocabularyBackupCodec.Encode(backup);
        bytes[0] = 99; media.Clear(); backup.CopyImages()["fixture"][1] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, backup.CopyImages()["fixture"]);
        var restored = VocabularyBackupCodec.Decode(encoded); restored.CopyImages()["fixture"][2] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, restored.CopyImages()["fixture"]);
        Assert.Equal("AQID", JsonNode.Parse(VocabularyBackupCodec.Encode(restored))!["images"]!["fixture"]!.GetValue<string>());
    }
    [Fact]
    public void WireDecodingDoesNotClaimRestoreVersionGraphOrMediaAdmission()
    {
        var document = new VocabularyDocument { SchemaVersion = 99 };
        var value = new VocabularyBackup(document, new Dictionary<string, byte[]> { ["not-a-media-filename"] = [0, 1] }, version: 7);
        var restored = VocabularyBackupCodec.Decode(VocabularyBackupCodec.Encode(value));
        Assert.Equal(7, restored.Version); Assert.Equal(99, restored.Document.SchemaVersion);
        Assert.Equal(new byte[] { 0, 1 }, restored.CopyImages()["not-a-media-filename"]);
        Assert.Throws<VocabularyException>(() => VocabularyArchiveValidation.Validate(restored.Document));
    }
    [Fact]
    public void EncodingRejectsInvalidUtf16AndDecoderFailuresDoNotExposeLiteralInput()
    {
        var document = VocabularyReferenceGraph.Document(); var entry = document.Entries[0] with { Expression = "\ud800" };
        Assert.Equal(VocabularyFailure.InvalidArchive, Assert.Throws<VocabularyException>(() =>
            VocabularyBackupCodec.EncodeDocument(document with { Entries = [entry] })).Failure);
        string secret = "private fixture text";
        var error = Assert.Throws<VocabularyException>(() => VocabularyBackupCodec.Decode(Encoding.UTF8.GetBytes("{\"images\":\"" + secret + "\"}")));
        Assert.Equal(VocabularyFailure.InvalidArchive, error.Failure); Assert.DoesNotContain(secret, error.ToString());
    }
    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    public void FoundationDataRejectsWhitespaceThatDotNetWouldIgnore(string whitespace)
    {
        var node = JsonNode.Parse(VocabularyBackupCodec.Encode(new(new(), new Dictionary<string, byte[]> { ["x"] = [1, 2, 3] })))!;
        node["images"]!["x"] = "AQ" + whitespace + "ID";
        Assert.Equal(VocabularyFailure.InvalidArchive, Assert.Throws<VocabularyException>(() =>
            VocabularyBackupCodec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString()))).Failure);
    }
}
