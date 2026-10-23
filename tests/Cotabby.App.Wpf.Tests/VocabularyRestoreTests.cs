using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Real private Windows files, WIC validation, SQLite replacement and reopen use the
/// packages produced by the complete pinned Mac restore probe. No successful IO is simulated.
/// Raw Foundation IO/DecodingError classes map explicitly to the fixed Windows archive error.</summary>
[Collection("WPF views")]
public sealed class VocabularyRestoreTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-restore-v1.json"))).RootElement.Clone();
    private static readonly byte[] Input = Convert.FromBase64String(Corpus.GetProperty("input").GetString()!);
    private static VocabularyImageReference Image => new(Convert.ToHexStringLower(SHA256.HashData(Input)), 80, 40, Input.Length);
    private static JsonElement Expected(string id) => Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id).GetProperty("expected");
    private static VocabularyDocument Document(JsonElement value) => VocabularyBackupCodec.DecodeDocument(Encoding.UTF8.GetBytes(value.GetRawText()));
    private static byte[] Package(string id) => Convert.FromBase64String(Expected(id).GetProperty("package").GetString()!);
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalRestorePreservesAcknowledgedGraphAndAllOrNothingMedia(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(27, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift", row.GetProperty("macSource").GetString());
        var expected = row.GetProperty("expected"); var before = Document(expected.GetProperty("before"));
        using var folder = new VocabularyExportFileTests.Folder(); var storage = new WindowsVocabularyImageStorage(folder.Notebook);
        if (VocabularyArchiveMediaPolicy.References(before).Count > 0) Assert.True(storage.Write(Input, Image));
        await using var archive = new VocabularyArchive(folder.Notebook); var baseline = await archive.CommitAsync(before);
        string media = Path.Combine(folder.Notebook + ".media", Image.Filename), source = Path.Combine(folder.Path, "source.lingrove"), chosen = source;
        if (id == "repair") File.WriteAllBytes(media, [0, 1, 2]);
        byte[] package = Package(id);
        if (id == "directory") Directory.CreateDirectory(source);
        else if (id == "oversized") { using var stream = File.Create(source); stream.SetLength((long)VocabularyRestorePolicy.MaximumBackupBytes + 1); }
        else if (id != "missing-source")
        {
            File.WriteAllBytes(source, package);
            if (id is "symbolic-link" or "hard-link")
            {
                chosen = Path.Combine(folder.Path, "alias.lingrove");
                if (id == "symbolic-link") File.CreateSymbolicLink(chosen, source);
                else Assert.True(CreateHardLinkW(chosen, source, IntPtr.Zero));
            }
        }
        Exception? failure = await Record.ExceptionAsync(() => archive.RestoreAsync(chosen));
        string? nativeError = expected.GetProperty("error").GetString();
        if (nativeError is null) Assert.Null(failure);
        else Assert.Equal(nativeError switch
        {
            "unsupportedVersion" => VocabularyFailure.UnsupportedVersion,
            "oversizedArchive" => VocabularyFailure.OversizedArchive,
            "invalidContent" => VocabularyFailure.InvalidContent,
            "invalidArchive" or "decode" or "io" => VocabularyFailure.InvalidArchive,
            _ => throw new InvalidOperationException("Unmapped actual source error.")
        }, Assert.IsType<VocabularyException>(failure).Failure);
        var result = await archive.LoadAsync();
        Assert.Equal(baseline.Revision + expected.GetProperty("revisionDelta").GetUInt64(), result.Revision);
        AssertDocument(expected.GetProperty("document"), result.Document);
        await using var reopened = new VocabularyArchive(folder.Notebook); AssertDocument(expected.GetProperty("reopened"), (await reopened.LoadAsync()).Document);
        byte[]? actual = null; try { actual = await archive.ReadImageAsync(Image); } catch (VocabularyException) { }
        Assert.Equal(expected.GetProperty("imageReadable").GetBoolean(), actual is not null && actual.SequenceEqual(Input));
        Assert.Equal(expected.GetProperty("mediaExists").GetBoolean(), File.Exists(media));
        Assert.Equal(expected.GetProperty("stages").GetInt32(), Directory.EnumerateFiles(folder.Path, ".notebook-*", SearchOption.AllDirectories).Count());
        if (id == "oversized") Assert.Equal((long)VocabularyRestorePolicy.MaximumBackupBytes + 1, new FileInfo(source).Length);
        else if (id == "directory") Assert.True(Directory.Exists(source));
        else if (id == "missing-source") Assert.False(File.Exists(source));
        else Assert.Equal(package, File.ReadAllBytes(source));
    }
    [Theory]
    [InlineData("all")]
    [InlineData("malformed-json")]
    [InlineData("unsupported-version")]
    public async Task CancellationStartsAtStagingAfterPackageAdmission(string id)
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var archive = new VocabularyArchive(folder.Notebook);
        var before = await archive.CommitAsync(Document(Expected("all").GetProperty("before")));
        File.WriteAllBytes(folder.Export, Package(id)); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        if (id == "all") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => archive.RestoreAsync(folder.Export, cancel.Token));
        else Assert.Equal(id == "malformed-json" ? VocabularyFailure.InvalidArchive : VocabularyFailure.UnsupportedVersion,
            (await Assert.ThrowsAsync<VocabularyException>(() => archive.RestoreAsync(folder.Export, cancel.Token))).Failure);
        var after = await archive.LoadAsync(); Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Document, after.Document);
        Assert.False(Directory.Exists(folder.Notebook + ".media"));
    }
    [Fact]
    public async Task StaleRestoreRollsBackNewMediaWithoutOverwritingAnotherArchive()
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var first = new VocabularyArchive(folder.Notebook);
        var before = await first.CommitAsync(Document(Expected("all").GetProperty("before")));
        await using var writer = new VocabularyArchive(folder.Notebook);
        var newer = await writer.SaveAsync(new("river", "river", LearningLanguage.English, VocabularyEntryKind.Word, "水流"));
        File.WriteAllBytes(folder.Export, Package("all"));
        Assert.Equal(VocabularyFailure.EditConflict, (await Assert.ThrowsAsync<VocabularyException>(() => first.RestoreAsync(folder.Export))).Failure);
        var after = await first.LoadAsync(); Assert.Equal(newer.Revision, after.Revision); Assert.Equal(newer.Document, after.Document);
        Assert.True(newer.Revision > before.Revision);
        Assert.False(File.Exists(Path.Combine(folder.Notebook + ".media", Image.Filename)));
        Assert.Equal(Package("all"), File.ReadAllBytes(folder.Export));
    }
    [Fact]
    public async Task RetiredArchiveRejectsRestoreWithoutOpeningItsSourceOrNotebook()
    {
        using var folder = new VocabularyExportFileTests.Folder(); var archive = new VocabularyArchive(folder.Notebook); await archive.DisposeAsync();
        Assert.Equal(VocabularyFailure.InvalidArchive, (await Assert.ThrowsAsync<VocabularyException>(() => archive.RestoreAsync(folder.Export))).Failure);
        Assert.False(File.Exists(folder.Notebook)); Assert.False(File.Exists(folder.Export));
    }
    private static void AssertDocument(JsonElement expected, VocabularyDocument actual) => Assert.True(JsonNode.DeepEquals(
        JsonNode.Parse(expected.GetRawText()), JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(actual))));
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string name, string existing, IntPtr security);
}
