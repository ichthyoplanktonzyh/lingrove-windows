using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Original Store observations run against actual Windows backup/restore IO. Only UI
/// notification scheduling and the lifetime tests' native IO entry gates are controlled; no fake
/// successful archive or media reply can satisfy the graph, repair, publication or reopen checks.</summary>
[Collection("WPF views")]
public sealed class VocabularyBackupStoreTests
{
    private static JsonElement Fixture(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", name))).RootElement.Clone();
    private static readonly JsonElement Corpus = Fixture("vocabulary-backup-store-v1.json");
    private static readonly JsonElement Restores = Fixture("vocabulary-restore-v1.json");
    private static readonly byte[] Input = Convert.FromBase64String(Corpus.GetProperty("input").GetString()!);
    private static VocabularyImageReference Image => new(Convert.ToHexStringLower(SHA256.HashData(Input)), 80, 40, Input.Length);
    private static byte[] Package(string id) => Convert.FromBase64String(Restores.GetProperty("cases").EnumerateArray()
        .Single(row => row.GetProperty("id").GetString() == id).GetProperty("expected").GetProperty("package").GetString()!);
    private static VocabularyDocument Initial => VocabularyBackupCodec.Decode(Package("all")).Document;
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalStorePublishesRestoreAndPreservesBackupState(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(13, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyStore.swift", row.GetProperty("macSource").GetString());
        using var folder = new VocabularyExportFileTests.Folder(); var archive = new VocabularyArchive(folder.Notebook);
        if (id != "fresh-backup") { Assert.True(new WindowsVocabularyImageStorage(folder.Notebook).Write(Input, Image)); await archive.CommitAsync(Initial); }
        var ui = new UiQueue(); await using var store = new VocabularyStore(archive, ui);
        if (id != "fresh-backup") await store.LoadAsync();
        ulong revision = store.Snapshot.Revision; Guid media = store.MediaRevision;
        if (id is "backup-preserves-error" or "restore-after-error") Assert.False(await store.RemoveSenseAsync(Id(999), Id(888)));
        if (id.StartsWith("backup", StringComparison.Ordinal) && id != "backup-after-restore" || id == "fresh-backup")
        {
            if (id is "backup-error" or "backup-failure-then-success") Assert.False(await store.BackupAsync(folder.Notebook));
            if (id != "backup-error") Assert.True(await store.BackupAsync(folder.Export));
        }
        else
        {
            string input = Path.Combine(folder.Path, "incoming.lingrove");
            if (id == "restore-repair") File.WriteAllBytes(Path.Combine(folder.Notebook + ".media", Image.Filename), [0, 1, 2]);
            byte[] bytes = id == "restore-error" ? Encoding.UTF8.GetBytes("{private invalid input")
                : Package(id is "restore-all" or "restore-repair" or "restore-twice" ? "all" : id == "restore-empty" ? "empty" : "plain");
            File.WriteAllBytes(input, bytes); Assert.Equal(id != "restore-error", await store.RestoreAsync(input));
            if (id == "restore-twice") Assert.True(await store.RestoreAsync(input));
            if (id == "backup-after-restore") Assert.True(await store.BackupAsync(folder.Export));
            Assert.Equal(bytes, File.ReadAllBytes(input));
        }
        var graph = JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(store.Snapshot.Document))!;
        byte[]? image = null; try { image = await store.ReadImageAsync(Image); } catch (VocabularyException) { }
        var actual = new JsonObject
        {
            ["loaded"] = store.IsLoaded, ["entries"] = graph["entries"]!.DeepClone(), ["templates"] = graph["reviewTemplates"]?.DeepClone() ?? new JsonArray(),
            ["error"] = store.ErrorMessage is not null, ["pending"] = store.Pending.Count, ["mediaChanged"] = store.MediaRevision != media,
            ["revisionDelta"] = store.Snapshot.Revision - revision, ["imageReadable"] = image is not null && image.SequenceEqual(Input),
            ["backup"] = File.Exists(folder.Export) ? JsonNode.Parse(File.ReadAllBytes(folder.Export)) : null
        };
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
        Assert.DoesNotContain("private", store.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Empty(store.Pending); ui.Drain();
    }
    [Fact]
    public async Task AcknowledgedRestoreInvalidatesMediaEvenWhenANewerSnapshotAlreadyWon()
    {
        using var folder = new VocabularyExportFileTests.Folder(); var archive = new VocabularyArchive(folder.Notebook);
        await archive.CommitAsync(new()); await using var store = new VocabularyStore(archive, new UiQueue()); await store.LoadAsync();
        var newer = new VocabularySnapshot(999, new()); store.Apply(newer); Guid media = store.MediaRevision;
        File.WriteAllBytes(folder.Export, Package("all")); Assert.True(await store.RestoreAsync(folder.Export));
        Assert.Same(newer, store.Snapshot); Assert.NotEqual(media, store.MediaRevision);
        Assert.Equal(Input, await store.ReadImageAsync(Image));
    }
    [Fact]
    public async Task RetiredDispatcherCannotReclassifyAcknowledgedRestoreAsFailure()
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = new VocabularyStore(new(folder.Notebook), new RetiredUi());
        Guid media = store.MediaRevision; File.WriteAllBytes(folder.Export, Package("all"));
        Assert.True(await store.RestoreAsync(folder.Export)); Assert.Null(store.ErrorMessage); Assert.NotEqual(media, store.MediaRevision);
        Assert.Equal(Initial, store.Snapshot.Document); Assert.Equal(Input, await store.ReadImageAsync(Image));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownDrainsAdmittedBackupOrRestoreAndClosesBothCommands(bool restoring)
    {
        using var folder = new VocabularyExportFileTests.Folder(); var actual = new WindowsVocabularyImageStorage(folder.Notebook);
        var observed = new GatedImages(actual, restoring); var archive = new VocabularyArchive(() => VocabularySQLiteDatabase.OpenFile(folder.Notebook), images: observed);
        if (!restoring) { Assert.True(actual.Write(Input, Image)); await archive.CommitAsync(Initial); }
        else await archive.CommitAsync(new());
        var ui = new UiQueue(); var store = new VocabularyStore(archive, ui); await store.LoadAsync(); Guid media = store.MediaRevision;
        if (restoring) File.WriteAllBytes(folder.Export, Package("all"));
        Task<bool> operation = restoring ? store.RestoreAsync(folder.Export) : store.BackupAsync(folder.Export);
        try
        {
            await observed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); var retiring = store.DisposeAsync().AsTask();
            Assert.False(operation.IsCompleted); Assert.False(retiring.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => store.BackupAsync(folder.Export));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => store.RestoreAsync(folder.Export));
            observed.Release.TrySetResult(); Assert.True(await operation.WaitAsync(TimeSpan.FromSeconds(10))); await retiring.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(restoring, media != store.MediaRevision); Assert.Null(store.ErrorMessage);
            await using var reopened = new VocabularyArchive(folder.Notebook); Assert.Equal(Initial, (await reopened.LoadAsync()).Document);
            Assert.Equal(Input, await reopened.ReadImageAsync(Image));
            if (!restoring) Assert.Equal(Initial, VocabularyBackupCodec.Decode(File.ReadAllBytes(folder.Export)).Document);
            ui.Drain();
        }
        finally { observed.Release.TrySetResult(); await store.DisposeAsync(); }
    }
    private sealed class GatedImages(WindowsVocabularyImageStorage actual, bool write) : IVocabularyImageStorage
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private void Hold() { Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); }
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> bytes, CancellationToken token) => actual.Prepare(bytes, token);
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken token) => actual.PrepareFile(path, token);
        public bool Write(ReadOnlySpan<byte> bytes, VocabularyImageReference image) { if (write) Hold(); return actual.Write(bytes, image); }
        public byte[] Read(VocabularyImageReference image) { if (!write) Hold(); return actual.Read(image); }
        public void Remove(VocabularyImageReference image) => actual.Remove(image);
    }
    private sealed class UiQueue : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _work = new();
        public override void Post(SendOrPostCallback callback, object? state) => _work.Enqueue(() => callback(state));
        internal void Drain() { while (_work.TryDequeue(out var work)) work(); }
    }
    private sealed class RetiredUi : SynchronizationContext
    { public override void Post(SendOrPostCallback callback, object? state) => throw new InvalidOperationException("Dispatcher closed"); }
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12"));
}
