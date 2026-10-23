using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Actual SQLite + WIC/media + serialized archive export, compared with the complete
/// original Mac actor. Literal PNG bytes avoid different native PNG-encoding choices in the corpus.</summary>
[Collection("WPF views")]
public sealed class VocabularyExportSnapshotTests(WpfDispatcher dispatcher)
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-snapshot-v1.json"))).RootElement.Clone();
    private static byte[] Input => Convert.FromBase64String(Corpus.GetProperty("input").GetString()!);
    private static VocabularyImageReference Image => new(Convert.ToHexStringLower(SHA256.HashData(Input)), 80, 40, Input.Length);
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public Task OriginalMacArchiveOwnsExactScopeAndMediaSnapshot(string id) => dispatcher.RunAsync(async () =>
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var connection = VocabularySQLiteConnection.OpenMemory(); VocabularySQLiteDatabase.CreateSchema(connection);
        await using var archive = new VocabularyArchive(() => VocabularySQLiteDatabase.Admit(connection), images: new WindowsVocabularyImageStorage(null));
        var actual = JsonSerializer.SerializeToNode(await Observe(row, archive));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    });
    private static async Task<object> Observe(JsonElement row, VocabularyArchive archive)
    {
        string mode = row.GetProperty("mode").GetString()!; var image = Image; var initial = Seed();
        if (mode.StartsWith("budget", StringComparison.Ordinal))
        {
            int count = mode == "budgetExact" ? 32 : mode == "budgetPlusOne" ? 33 : 36;
            initial = initial with { Entries = [initial.Entries[0] with { Senses = Enumerable.Range(0, (count + 3) / 4).Select(owner => new VocabularySense(Id(100 + owner), "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) {
                Content = Enumerable.Range(0, Math.Min(4, count - owner * 4)).Select(part => {
                    int index = owner * 4 + part;
                    return Block(200 + index, new(index.ToString("x64", CultureInfo.InvariantCulture), 1, 1,
                        mode == "budgetPlusOne" && index == 32 ? 1 : VocabularyContentPolicy.MaximumImageBytes));
                }).ToArray() }).ToArray() }] };
        }
        await archive.CommitAsync(initial); var edited = initial.Entries[0];
        if (!mode.StartsWith("budget", StringComparison.Ordinal))
        {
            var senses = edited.Senses.ToArray(); int owner = mode == "otherSense" ? 1 : 0;
            senses[owner] = senses[owner] with { Content = [Block(owner == 1 ? 21 : 20, image)] };
            if (mode == "shared") { senses[0] = senses[0] with { Content = [Block(20, image), Block(22, image)] }; senses[1] = senses[1] with { Content = [Block(23, image)] }; }
            edited = edited with { Senses = senses };
            if (mode is "missing" or "metadataOnly" or "otherSense") await archive.CommitAsync(initial with { Entries = [edited] });
            else await archive.UpdateAsync(edited, initial.Entries[0], new Dictionary<string, VocabularyPreparedImage> { [image.Filename] = new(image, Input) });
            if (mode == "mismatch")
            {
                var d = (await archive.LoadAsync()).Document; var e = d.Entries[0]; var s = e.Senses[0] with { Content = [Block(20, image with { Width = 81 })] };
                await archive.CommitAsync(d with { Entries = [e with { Senses = new[] { s }.Concat(e.Senses.Skip(1)).ToArray() }] });
            }
        }
        var options = new VocabularyExportConfiguration { Format = Enum.Parse<VocabularyExportFormat>(row.GetProperty("format").GetString()!, true),
            Images = row.GetProperty("images").GetBoolean(), Content = !row.TryGetProperty("content", out var content) || content.GetBoolean() };
        VocabularySelection[] selections = [new(Id(1), Id(2))];
        switch (mode)
        {
            case "empty": case "cancelledEmpty": selections = []; break;
            case "staleEntry": selections = [new(Id(99), Id(2))]; break;
            case "staleSense": selections = [new(Id(1), Id(99))]; break;
            case "alias": selections = [new(Id(1), Id(90))]; break;
            case "duplicate": selections = [selections[0], selections[0]]; break;
            case "shared": case "ordered": selections = [new(Id(1), Id(4)), new(Id(1), Id(2))]; break;
            case "budget": case "budgetExact": case "budgetPlusOne": selections = initial.Entries[0].Senses.Select(s => new VocabularySelection(Id(1), s.Id)).ToArray(); break;
        }
        ulong before = (await archive.LoadAsync()).Revision;
        try
        {
            var p = await archive.PrepareExportAsync(selections, options, cancellationToken: new(mode.StartsWith("cancelled", StringComparison.Ordinal)));
            ulong after = (await archive.LoadAsync()).Revision;
            if (mode == "removeAfter")
            {
                await archive.RemoveEntryAsync(Id(1)); bool available;
                try { _ = await archive.ReadImageAsync(image); available = true; } catch (VocabularyException) { available = false; }
                return new { package = Facts(p), revisionUnchanged = before == after, sourceAvailable = available, entriesAfter = (await archive.LoadAsync()).Document.Entries.Count };
            }
            return new { package = Facts(p), revisionUnchanged = before == after };
        }
        catch (VocabularyExportException error) { return new { error = JsonNamingPolicy.CamelCase.ConvertName(error.Failure.ToString()), revisionUnchanged = (await archive.LoadAsync()).Revision == before }; }
        catch (OperationCanceledException) { return new { error = "cancelled", revisionUnchanged = (await archive.LoadAsync()).Revision == before }; }
    }
    private static object Facts(VocabularyExportPackage p)
    {
        var images = p.CopyImages(); return new { sha256 = Convert.ToHexStringLower(SHA256.HashData(p.CopyData())), byteCount = p.DataByteCount, filename = p.Filename,
            entryCount = p.EntryCount, senseCount = p.SenseCount, exampleCount = p.ExampleCount, imageNames = images.Keys.Order(StringComparer.Ordinal),
            imageHashes = images.ToDictionary(pair => pair.Key, pair => Convert.ToHexStringLower(SHA256.HashData(pair.Value))) };
    }
    [Fact]
    public Task SharedStorageLockCoversActualMediaReadUntilSnapshotOwnsBytes() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var storage = new ForwardingReadStorage(folder.Notebook, () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException(); });
        await using var first = new VocabularyArchive(() => VocabularySQLiteDatabase.OpenFile(folder.Notebook), images: storage);
        await using var second = new VocabularyArchive(folder.Notebook); var initial = Seed(); await first.CommitAsync(initial);
        var original = initial.Entries[0]; var edited = original with { Senses = [original.Senses[0] with { Content = [Block(20, Image)] }, original.Senses[1]] };
        await first.UpdateAsync(edited, original, new Dictionary<string, VocabularyPreparedImage> { [Image.Filename] = new(Image, Input) });
        await second.LoadAsync();
        var preparing = first.PrepareExportAsync([new(Id(1), Id(2))], new() { Format = VocabularyExportFormat.Json, Images = true });
        try
        {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))));
            Assert.Equal(VocabularyFailure.StorageBusy, (await Assert.ThrowsAsync<VocabularyException>(() => second.RemoveEntryAsync(Id(1)))).Failure);
            Assert.Single((await second.LoadAsync()).Document.Entries);
        }
        finally { release.Set(); }
        var package = await preparing.WaitAsync(TimeSpan.FromSeconds(15)); await second.RemoveEntryAsync(Id(1));
        Assert.Equal(Input, Assert.Single(package.CopyImages()).Value);
        await Assert.ThrowsAsync<VocabularyException>(() => first.ReadImageAsync(Image));
    });
    private sealed class ForwardingReadStorage(string path, Action afterFirstRead) : IVocabularyImageStorage
    {
        private readonly WindowsVocabularyImageStorage _storage = new(path); private int _reads;
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken cancellationToken) => _storage.Prepare(data, cancellationToken);
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken cancellationToken) => _storage.PrepareFile(path, cancellationToken);
        public byte[] Read(VocabularyImageReference image) { byte[] bytes = _storage.Read(image); if (Interlocked.Increment(ref _reads) == 1) afterFirstRead(); return bytes; }
        public bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image) => _storage.Write(data, image);
        public void Remove(VocabularyImageReference image) => _storage.Remove(image);
    }
    private sealed class Folder : IDisposable
    {
        private string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lingrove-export-snapshot-" + Guid.NewGuid().ToString("N"));
        internal string Notebook => System.IO.Path.Combine(Path, "notebook.sqlite");
        internal Folder() { VocabularyWindowsFile.CreateParentDirectory(Path); }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
    private static VocabularyDocument Seed()
    {
        var usage = new VocabularyExample(Id(3), "The bank.", "原句", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "context", DateTimeOffset.UnixEpoch);
        var sense = new VocabularySense(Id(2), "core", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { Note = "note", Tags = ["Work"], Examples = [usage], MergedFrom = [Id(90)] };
        return new() { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
            [sense, new(Id(4), "second", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)] };
    }
    private static VocabularyContentBlock Block(int n, VocabularyImageReference image) => new(Id(n), new VocabularyBlockContent.Image(image, "river"));
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12", CultureInfo.InvariantCulture));
}
