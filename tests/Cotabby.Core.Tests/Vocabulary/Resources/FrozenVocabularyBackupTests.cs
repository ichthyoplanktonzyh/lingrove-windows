using System.Text;
using System.Text.Json;
using F = Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenVocabularyBackupTests
{
    [Fact]
    public void EmptyEnvelopeRoundtripsAndDetachesMedia()
    {
        byte[] bytes = [0, 1, 2, 255]; var input = new F.FrozenVocabularyBackup(new F.VocabularyDocument(), new Dictionary<string, byte[]> { ["image"] = bytes });
        bytes[0] = 42; var encoded = F.FrozenVocabularyBackupCodec.Encode(input); var result = F.FrozenVocabularyBackupCodec.Decode(encoded);
        Assert.Equal(input.Document, result.Document); Assert.Equal(new byte[] { 0, 1, 2, 255 }, result.CopyImages()["image"]);
        var copy = result.CopyImages(); copy["image"][0] = 9; Assert.Equal(0, result.CopyImages()["image"][0]);
        Assert.Throws<VocabularyException>(() => F.FrozenVocabularyRestorePolicy.Validate(result));
    }
    [Fact]
    public void RichCurrentFixtureRetainsCompleteGraphAndExactDoubleBits()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        Assert.NotNull(root); using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "fixtures", "parity", "vocabulary-document-frozen-v1.json")));
        foreach (var test in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = test.GetProperty("input"); if (input.GetProperty("schemaVersion").GetInt64() != 8) continue;
            var doc = F.VocabularyDocumentCodec.Decode(Encoding.UTF8.GetBytes(input.GetRawText()));
            var backup = new F.FrozenVocabularyBackup(doc, new Dictionary<string, byte[]>()); var decoded = F.FrozenVocabularyBackupCodec.Decode(F.FrozenVocabularyBackupCodec.Encode(backup));
            Assert.Equal(doc, decoded.Document);
            Assert.Equal(BitConverter.DoubleToInt64Bits(doc.Sources[0].EncounteredAt.ReferenceSeconds), BitConverter.DoubleToInt64Bits(decoded.Document.Sources[0].EncounteredAt.ReferenceSeconds));
        }
    }
    [Fact]
    public void RestoreDoesNotReconcileEvidenceAgainstCurrentNotebook()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "fixtures", "parity", "vocabulary-document-frozen-v1.json")));
        var input = fixture.RootElement.GetProperty("cases").EnumerateArray().First(test => test.GetProperty("id").GetString() == "optional-absence").GetProperty("input");
        var doc = F.VocabularyDocumentCodec.Decode(Encoding.UTF8.GetBytes(input.GetRawText())); var entry = doc.Entries[0]; var sense = entry.Senses[0];
        var previous = doc.WithEntries([entry with { Senses = [sense with { Examples = sense.Examples.Select((value, index) => index == 0 ? value with { Meaning = value.Meaning + " changed" } : value).ToArray() }] }]);
        var normalCommit = F.CanonicalWritingPolicy.PrepareCommit(previous, doc); var restore = F.CanonicalWritingPolicy.PrepareRestore(doc);
        Assert.NotEqual(doc.Semantics, normalCommit.Semantics); Assert.Equal(doc.Semantics, restore.Semantics);
        Assert.Equal(doc.Sources, restore.Sources); Assert.Equal(doc.Occurrences, restore.Occurrences); Assert.Equal(doc.Interpretations, restore.Interpretations);
        Assert.Equal(doc.Collections[0].Review, restore.Collections[0].Review); Assert.Equal(doc.Classification, restore.Classification);
    }

}
