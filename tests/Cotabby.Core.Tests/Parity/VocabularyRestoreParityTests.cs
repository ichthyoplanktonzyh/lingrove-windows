using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>The same actual-source packages exercise only the portable admission prefix here.
/// PNG and file effects belong to the native counterpart, including four packages intentionally
/// admitted here whose bytes the native PNG validator must subsequently reject.</summary>
public sealed class VocabularyRestoreParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-restore-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray()
        .Where(row => row.GetProperty("id").GetString() is not ("directory" or "oversized" or "symbolic-link" or "hard-link" or "missing-source"))
        .Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalRestoreAdmitsVersionGraphAndExactMediaBeforeNativeValidation(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(27, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift", row.GetProperty("macSource").GetString());
        var expected = row.GetProperty("expected"); string? nativeError = expected.GetProperty("error").GetString();
        VocabularyBackup? backup = null; IReadOnlyList<VocabularyImageReference>? references = null;
        var failure = Record.Exception(() => { backup = VocabularyBackupCodec.Decode(Convert.FromBase64String(expected.GetProperty("package").GetString()!)); references = VocabularyRestorePolicy.Validate(backup); });
        VocabularyFailure? expectedFailure = nativeError switch
        {
            "unsupportedVersion" => VocabularyFailure.UnsupportedVersion,
            "invalidArchive" or "decode" => VocabularyFailure.InvalidArchive,
            _ => null
        };
        if (expectedFailure is { } code) Assert.Equal(code, Assert.IsType<VocabularyException>(failure).Failure);
        else
        {
            Assert.Null(failure); Assert.NotNull(backup); Assert.NotNull(references);
            Assert.True(VocabularyArchiveMediaPolicy.References(backup.Document).ToHashSet().SetEquals(references));
            Assert.True(references.Select(image => image.Filename).ToHashSet(StringComparer.Ordinal).SetEquals(backup.CopyImages().Keys));
            var original = JsonNode.Parse(Convert.FromBase64String(expected.GetProperty("package").GetString()!))!["document"];
            Assert.True(JsonNode.DeepEquals(original, JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(backup.Document))));
            if (nativeError == "invalidContent") Assert.NotEmpty(references);
            else Assert.Equal(VocabularyBackupCodec.DecodeDocument(System.Text.Encoding.UTF8.GetBytes(expected.GetProperty("document").GetRawText())), backup.Document);
        }
    }
}
