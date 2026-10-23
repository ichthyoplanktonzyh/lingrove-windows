using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Exercises real Windows path/ACL/publication effects over the same prepared package
/// as the complete original Mac save method. Dummy protected files prove saving does not open SQL.</summary>
[Collection("WPF views")]
public sealed class VocabularyExportSaveTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-save-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalMacSaveKeepsCapturedBytesProtectedFilesAndCancellationOrder(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyArchive+Export.swift", row.GetProperty("macSource").GetString());
        var actual = JsonSerializer.SerializeToNode(await Observe(row));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static async Task<object> Observe(JsonElement row)
    {
        using var folder = new VocabularyExportFileTests.Folder(); string mode = row.GetProperty("mode").GetString()!;
        byte[] old = [9, 8, 7]; string destination = folder.Export, media = folder.Notebook + ".media";
        if (mode == "protected") { destination = folder.Notebook + row.GetProperty("suffix").GetString(); File.WriteAllBytes(destination, old); }
        if (mode is "hardLink" or "symlink")
        {
            File.WriteAllBytes(folder.Notebook, old);
            if (mode == "hardLink") Assert.True(CreateHardLinkW(destination, folder.Notebook, IntPtr.Zero));
            else File.CreateSymbolicLink(destination, folder.Notebook);
        }
        if (mode is "mediaRoot" or "mediaChild")
        {
            VocabularyWindowsFile.CreateParentDirectory(media); File.WriteAllBytes(System.IO.Path.Combine(media, "child.json"), old);
            destination = mode == "mediaRoot" ? media : System.IO.Path.Combine(media, "child.json");
        }
        if (Flag(row, "replace")) File.WriteAllBytes(destination, old);
        var protectedPaths = new[] { "", "-wal", "-shm", "-journal", ".lock" }.Select(suffix => folder.Notebook + suffix).ToArray();
        var before = protectedPaths.Select(Read).ToArray();
        var options = new VocabularyExportConfiguration { Format = Enum.Parse<VocabularyExportFormat>(row.GetProperty("format").GetString()!, true), Images = Flag(row, "images") };
        var package = VocabularyExportPolicy.Package([], options);
        if (Flag(row, "attachment")) package = package.WithImages(new Dictionary<string, byte[]> { [Flag(row, "badImage") ? "unsafe.png" : "lg-image-" + new string('a', 64) + ".png"] = [1, 2, 3] });
        string? error = null; await using var archive = new VocabularyArchive(folder.Notebook);
        try { await archive.WriteExportAsync(package, destination, new(Flag(row, "cancelled"))); }
        catch (OperationCanceledException) { error = "cancelled"; }
        catch (VocabularyExportException failure) { error = JsonNamingPolicy.CamelCase.ConvertName(failure.Failure.ToString()); }
        catch (VocabularyException failure) { error = JsonNamingPolicy.CamelCase.ConvertName(failure.Failure.ToString()); }
        return new { error, destinationBase64 = Read(destination) is { } data ? Convert.ToBase64String(data) : null,
            protectedUnchanged = before.Zip(protectedPaths).All(pair => Same(pair.First, Read(pair.Second))), notebookExists = File.Exists(folder.Notebook),
            mediaUnchanged = !mode.StartsWith("media", StringComparison.Ordinal) || Same(Read(System.IO.Path.Combine(media, "child.json")), old),
            stages = Directory.GetFiles(folder.Path, ".notebook-*").Length };
    }
    [Theory]
    [InlineData(VocabularyExportFormat.Text)]
    [InlineData(VocabularyExportFormat.Csv)]
    [InlineData(VocabularyExportFormat.Json)]
    public async Task SaveUsesPreparedDataAfterActualSourceDeletion(VocabularyExportFormat format)
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var archive = new VocabularyArchive((string?)null);
        Guid entryId = Guid.NewGuid(), senseId = Guid.NewGuid(); var now = DateTimeOffset.UnixEpoch;
        var entry = new VocabularyEntry(entryId, "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
            [new(senseId, "captured", now, now)], now, now);
        await archive.CommitAsync(new() { Entries = [entry] });
        var package = await archive.PrepareExportAsync([new(entryId, senseId)], new() { Format = format });
        await archive.RemoveEntryAsync(entryId); Assert.Empty((await archive.LoadAsync()).Document.Entries);
        await archive.WriteExportAsync(package, folder.Export);
        Assert.Equal(package.CopyData(), File.ReadAllBytes(folder.Export));
    }
    [Fact]
    public async Task ClosedArchiveRejectsSaveWithoutCreatingDestination()
    {
        using var folder = new VocabularyExportFileTests.Folder(); var archive = new VocabularyArchive((string?)null); await archive.DisposeAsync();
        var failure = await Assert.ThrowsAsync<VocabularyException>(() => archive.WriteExportAsync(VocabularyExportPolicy.Package([], new()), folder.Export));
        Assert.Equal(VocabularyFailure.InvalidArchive, failure.Failure); Assert.Empty(Directory.GetFiles(folder.Path));
    }
    private static bool Flag(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.GetBoolean();
    private static byte[]? Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    private static bool Same(byte[]? first, byte[]? second) => first is null ? second is null : second is not null && first.AsSpan().SequenceEqual(second);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string name, string existing, IntPtr security);
}
