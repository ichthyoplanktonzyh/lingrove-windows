using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Real archive + SQLite + WIC/media operations. The original Mac actor supplies twelve shared
/// observations; native file regressions cover Windows acknowledgement, rollback and lock spans.
/// A forwarding storage wrapper only pauses/cancels after actual IO, never supplies fake bytes or
/// persistence answers. Every owner drains before its disposable temporary folder is removed.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyMediaTransactionTests(WpfDispatcher dispatcher)
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-media-transactions-v1.json"))).RootElement.Clone();
    private static byte[] Input => Convert.FromBase64String(Corpus.GetProperty("input").GetString()!);
    private static VocabularyPreparedImage Prepared()
    { byte[] bytes = Input; return new(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes); }
    private static IReadOnlyDictionary<string, VocabularyPreparedImage> Supplied(VocabularyPreparedImage image) => new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = image };
    private static VocabularyCapture Capture() => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, "core");
    private static VocabularyContentBlock Block(VocabularyImageReference image) => VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image, "river"));
    private static VocabularyEntry Content(VocabularyEntry entry, params VocabularyContentBlock[] blocks) => entry with { Senses = [entry.Senses[0] with { Content = blocks }, .. entry.Senses.Skip(1)] };
    private static VocabularyEntry Note(VocabularyEntry entry, string note) => entry with { Senses = [entry.Senses[0] with { Note = note }, .. entry.Senses.Skip(1)] };
    private static async Task<bool> Available(VocabularyArchive archive, VocabularyImageReference image)
    { try { _ = await archive.ReadImageAsync(image); return true; } catch (VocabularyException) { return false; } }
    private static async Task<VocabularyFailure> Failure(Func<Task> work) => (await Assert.ThrowsAsync<VocabularyException>(work)).Failure;
    private static string Error(VocabularyFailure failure) => char.ToLowerInvariant(failure.ToString()[0]) + failure.ToString()[1..];
    private static void EqualSnapshot(VocabularySnapshot expected, VocabularySnapshot actual)
    { Assert.Equal(expected.Revision, actual.Revision); Assert.Equal(expected.Document, actual.Document); }
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public Task OriginalMacActorOwnsMediaAdmissionCleanupAndSqlAcknowledgement(string id) => dispatcher.RunAsync(async () =>
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift", row.GetProperty("macSource").GetString());
        var connection = VocabularySQLiteConnection.OpenMemory(); VocabularySQLiteDatabase.CreateSchema(connection);
        await using var archive = new VocabularyArchive(() => VocabularySQLiteDatabase.Admit(connection), images: new WindowsVocabularyImageStorage(null));
        var prepared = Prepared(); var image = prepared.Reference; object observation;
        if (id == "draft-only")
        {
            var draft = await archive.PrepareImageAsync(Input);
            observation = new { width = draft.Reference.Width, height = draft.Reference.Height, available = await Available(archive, draft.Reference), revision = (await archive.LoadAsync()).Revision };
        }
        else observation = await Observe(id, archive, connection, prepared);
        var actual = JsonSerializer.SerializeToNode(observation);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    });
    private static async Task<object> Observe(string mode, VocabularyArchive archive, VocabularySQLiteConnection connection, VocabularyPreparedImage prepared)
    {
        var image = prepared.Reference; var original = (await archive.SaveAsync(Capture())).Document.Entries[0]; var edited = Content(original, Block(image));
        if (mode == "missing")
        {
            var failure = await Failure(() => archive.UpdateAsync(edited, original));
            return new { error = Error(failure), revision = (await archive.LoadAsync()).Revision, available = await Available(archive, image) };
        }
        if (mode == "extra")
        {
            var result = await archive.UpdateAsync(Note(original, "edited"), original, Supplied(prepared));
            return new { revision = result.Revision, note = result.Document.Entries[0].Senses[0].Note, available = await Available(archive, image) };
        }
        if (mode == "conflict")
        {
            var winner = Content(original, VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("winner")))); await archive.UpdateAsync(winner, original);
            var failure = await Failure(() => archive.UpdateAsync(edited, original, Supplied(prepared))); var state = await archive.LoadAsync();
            return new { error = Error(failure), revision = state.Revision, available = await Available(archive, image), preserved = state.Document.Entries[0].Senses[0].Content!.SequenceEqual(winner.Senses[0].Content!) };
        }
        if (mode == "sql-rollback")
        {
            connection.Execute("CREATE TRIGGER fail_sense BEFORE UPDATE ON senses BEGIN SELECT RAISE(ABORT,'fixture'); END");
            var failure = await Failure(() => archive.UpdateAsync(edited, original, Supplied(prepared))); var state = await archive.LoadAsync();
            return new { error = Error(failure), revision = state.Revision, available = await Available(archive, image), preserved = state.Document.Entries[0] == original };
        }
        if (mode == "existing-missing")
        {
            await archive.CommitAsync((await archive.LoadAsync()).Document with { Entries = [edited] });
            var result = await archive.UpdateAsync(Note(edited, "metadata"), edited);
            return new { revision = result.Revision, note = result.Document.Entries[0].Senses[0].Note, available = await Available(archive, image) };
        }
        if (mode == "prune")
        {
            var input = LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(original, null, original.Senses[0]));
            var record = new LearningExplanationRecord(Guid.NewGuid(), input, LearningLanguage.English, new("generated", "detail"), "fixture", DateTimeOffset.UnixEpoch);
            var baseline = (await archive.SaveExplanationAsync(record)).Document.Entries[0];
            var changed = Content(baseline with { Expression = "riverbank", DictionaryForm = "riverbank" }, Block(image));
            var result = await archive.UpdateAsync(changed, baseline, Supplied(prepared));
            return new { revision = result.Revision, before = baseline.Senses[0].Explanations.Count, after = result.Document.Entries[0].Senses[0].Explanations.Count, available = await Available(archive, image) };
        }
        if (mode == "budget")
        {
            var senses = Enumerable.Range(0, 9).Select(owner => (owner == 0 ? original.Senses[0] : new VocabularySense(Guid.NewGuid(), "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)) with
            { Content = Enumerable.Range(0, 4).Select(part => Block(new((owner * 4 + part).ToString("x64"), 1, 1, VocabularyContentPolicy.MaximumImageBytes))).ToArray() }).ToArray();
            var failure = await Failure(() => archive.UpdateAsync(original with { Senses = senses }, original)); var state = await archive.LoadAsync();
            return new { error = Error(failure), revision = state.Revision, senses = state.Document.Entries[0].Senses.Count };
        }
        if (mode == "shared")
        {
            var second = Guid.NewGuid(); var baseline = (await archive.CreateSenseAsync(original.Id, second)).Document.Entries[0];
            var both = baseline with { Senses = baseline.Senses.Select(sense => sense with { Content = new[] { Block(image) } }).ToArray() };
            var before = await archive.UpdateAsync(both, baseline, Supplied(prepared)); var ids = before.Document.Entries[0].Senses.SelectMany(sense => sense.Content!).Select(block => block.Id).ToHashSet();
            var merged = await archive.MergeSensesAsync(original.Id, second, original.Senses[0].Id);
            bool retained = await Available(archive, image), same = ids.SetEquals(merged.Document.Entries[0].Senses[0].Content!.Select(block => block.Id));
            var removed = await archive.RemoveSenseAsync(original.Id, original.Senses[0].Id);
            return new { revision = removed.Revision, blocksRetained = same, sharedAvailable = retained, available = await Available(archive, image), entries = removed.Document.Entries.Count };
        }
        var imported = await archive.UpdateAsync(edited, original, Supplied(prepared));
        if (mode == "import") return new { revision = imported.Revision, content = imported.Document.Entries[0].Senses[0].Content!.SequenceEqual(edited.Senses[0].Content!), exact = (await archive.ReadImageAsync(image)).SequenceEqual(prepared.CopyBytes()) };
        if (mode == "filename-retention")
        {
            var mismatch = image with { Width = 81 }; var seeded = Content(imported.Document.Entries[0], Block(image), Block(mismatch));
            var baseline = (await archive.CommitAsync(imported.Document with { Entries = [seeded] })).Document.Entries[0];
            var changed = Content(baseline, baseline.Senses[0].Content![1]); var result = await archive.UpdateAsync(changed, baseline);
            return new { revision = result.Revision, validAvailable = await Available(archive, image), mismatchAvailable = await Available(archive, mismatch) };
        }
        Assert.Equal("remove", mode); var old = imported.Document.Entries[0]; var removedContent = await archive.UpdateAsync(Content(old), old);
        return new { revision = removedContent.Revision, available = await Available(archive, image), blocks = removedContent.Document.Entries[0].Senses[0].Content!.Count };
    }

    [Fact]
    public Task PreparingNamedArchiveDraftDoesNotOpenSqliteOrCreateItsParent() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(create: false); await using var archive = new VocabularyArchive(folder.Notebook);
        var draft = await archive.PrepareImageAsync(Input); Assert.Equal(80, draft.Reference.Width); Assert.Equal(40, draft.Reference.Height);
        Assert.False(Directory.Exists(folder.Path));
    });
    [Fact]
    public Task ImportedContentAndExactBytesSurviveNamedArchiveReopen() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); var image = Prepared(); VocabularyEntry saved;
        await using (var archive = new VocabularyArchive(folder.Notebook))
        {
            var baseline = (await archive.SaveAsync(Capture())).Document.Entries[0]; saved = (await archive.UpdateAsync(Content(baseline, Block(image.Reference)), baseline, Supplied(image))).Document.Entries[0];
        }
        await using var reopened = new VocabularyArchive(folder.Notebook); Assert.Equal(saved, (await reopened.LoadAsync()).Document.Entries[0]);
        Assert.Equal(image.CopyBytes(), await reopened.ReadImageAsync(image.Reference)); Assert.Single(Directory.GetFiles(folder.Media));
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedSqlRemovesOnlyNewFilesAndKeepsARepair(bool repair) => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); await using var archive = new VocabularyArchive(folder.Notebook); var prepared = Prepared();
        var baseline = (await archive.SaveAsync(Capture())).Document.Entries[0];
        if (repair)
        {
            baseline = (await archive.UpdateAsync(Content(baseline, Block(prepared.Reference)), baseline, Supplied(prepared))).Document.Entries[0];
            File.WriteAllBytes(Path.Combine(folder.Media, prepared.Reference.Filename), [7]); Assert.False(await Available(archive, prepared.Reference));
        }
        var snapshot = await archive.LoadAsync();
        using (var connection = VocabularySQLiteConnection.OpenNamed(folder.Notebook))
            connection.Execute("CREATE TRIGGER fail_sense BEFORE UPDATE ON senses BEGIN SELECT RAISE(ABORT,'fixture'); END");
        var next = repair ? Note(baseline, "fails") : Content(baseline, Block(prepared.Reference));
        Assert.Equal(VocabularyFailure.InvalidArchive, await Failure(() => archive.UpdateAsync(next, baseline, Supplied(prepared))));
        EqualSnapshot(snapshot, await archive.LoadAsync()); Assert.Equal(repair, await Available(archive, prepared.Reference));
        if (repair) Assert.Equal(prepared.CopyBytes(), await archive.ReadImageAsync(prepared.Reference));
        Assert.Empty(Directory.GetFiles(folder.Path, ".vocabulary-image-*", SearchOption.AllDirectories));
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CancellationAfterActualStagingRollsBackNewFilesButPreservesRepairs(bool repair) => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); using var lifetime = new CancellationTokenSource(); bool armed = false;
        var storage = new ForwardingStorage(folder.Notebook, () => { if (armed) lifetime.Cancel(); });
        await using var archive = new VocabularyArchive(() => VocabularySQLiteDatabase.OpenFile(folder.Notebook), images: storage); var prepared = Prepared();
        var baseline = (await archive.SaveAsync(Capture())).Document.Entries[0];
        if (repair)
        {
            baseline = (await archive.UpdateAsync(Content(baseline, Block(prepared.Reference)), baseline, Supplied(prepared))).Document.Entries[0];
            File.WriteAllBytes(Path.Combine(folder.Media, prepared.Reference.Filename), [7]);
        }
        var snapshot = await archive.LoadAsync(); armed = true;
        var next = repair ? Note(baseline, "cancelled") : Content(baseline, Block(prepared.Reference));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => archive.UpdateAsync(next, baseline, Supplied(prepared), lifetime.Token));
        EqualSnapshot(snapshot, await archive.LoadAsync()); Assert.Equal(repair, await Available(archive, prepared.Reference));
    });
    [Fact]
    public Task APostCommitCleanupFailureLeavesAnOrphanAndAcknowledgesMetadata() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); await using var archive = new VocabularyArchive(folder.Notebook); var prepared = Prepared();
        var baseline = (await archive.SaveAsync(Capture())).Document.Entries[0];
        baseline = (await archive.UpdateAsync(Content(baseline, Block(prepared.Reference)), baseline, Supplied(prepared))).Document.Entries[0];
        using (var held = VocabularyWindowsFile.Open(Path.Combine(folder.Media, prepared.Reference.Filename)))
        {
            var removed = await archive.UpdateAsync(Content(baseline), baseline); Assert.Equal(3UL, removed.Revision);
            Assert.Empty(removed.Document.Entries[0].Senses[0].Content!); EqualSnapshot(removed, await archive.LoadAsync());
            Assert.Equal(prepared.CopyBytes(), await archive.ReadImageAsync(prepared.Reference));
        }
        new WindowsVocabularyImageStorage(folder.Notebook).Remove(prepared.Reference); Assert.False(await Available(archive, prepared.Reference));
    });
    [Fact]
    public Task ExclusiveArchiveLockCoversActualStagingUntilSqlCommits() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); using var staged = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var storage = new ForwardingStorage(folder.Notebook, () => { staged.Set(); if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException(); });
        await using var first = new VocabularyArchive(() => VocabularySQLiteDatabase.OpenFile(folder.Notebook), images: storage);
        await using var second = new VocabularyArchive(folder.Notebook); var prepared = Prepared();
        var baseline = (await first.SaveAsync(Capture())).Document.Entries[0]; await second.LoadAsync();
        var saving = first.UpdateAsync(Content(baseline, Block(prepared.Reference)), baseline, Supplied(prepared));
        try
        {
            Assert.True(await Task.Run(() => staged.Wait(TimeSpan.FromSeconds(10))));
            Assert.Equal(VocabularyFailure.StorageBusy, await Failure(() => second.ReadImageAsync(prepared.Reference)));
        }
        finally { release.Set(); }
        Assert.Equal(2UL, (await saving).Revision); Assert.Equal(prepared.CopyBytes(), await second.ReadImageAsync(prepared.Reference));
    });
    [Fact]
    public Task ARevisionChangeAfterStagingCannotBecomeTheCandidatesNewBaseline() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder();
        var storage = new ForwardingStorage(folder.Notebook, () =>
        {
            // Deliberately bypass the cooperative archive lock to exercise the SQL revision CAS.
            // This is an actual second SQLite connection; no fake commit result is injected.
            using var external = VocabularySQLiteConnection.OpenNamed(folder.Notebook); external.Execute("UPDATE state SET revision=revision+1 WHERE singleton=1");
        });
        await using var archive = new VocabularyArchive(() => VocabularySQLiteDatabase.OpenFile(folder.Notebook), images: storage); var prepared = Prepared();
        var baseline = (await archive.SaveAsync(Capture())).Document.Entries[0];
        Assert.Equal(VocabularyFailure.EditConflict, await Failure(() => archive.UpdateAsync(Content(baseline, Block(prepared.Reference)), baseline, Supplied(prepared))));
        var winner = await archive.LoadAsync(); Assert.Equal(2UL, winner.Revision); Assert.Equal(baseline, winner.Document.Entries[0]);
        Assert.False(await Available(archive, prepared.Reference));
    });
    private sealed class ForwardingStorage(string notebook, Action afterWrite) : IVocabularyImageStorage
    {
        private readonly WindowsVocabularyImageStorage _storage = new(notebook);
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken cancellationToken) => _storage.Prepare(data, cancellationToken);
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken cancellationToken) => _storage.PrepareFile(path, cancellationToken);
        public byte[] Read(VocabularyImageReference image) => _storage.Read(image);
        public bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image) { bool created = _storage.Write(data, image); afterWrite(); return created; }
        public void Remove(VocabularyImageReference image) => _storage.Remove(image);
    }
    private sealed class Folder : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lingrove-media-transaction-" + Guid.NewGuid().ToString("N"));
        internal string Notebook => System.IO.Path.Combine(Path, "notebook.sqlite");
        internal string Media => Notebook + ".media";
        internal Folder(bool create = true) { if (create) VocabularyWindowsFile.CreateParentDirectory(Path); }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
