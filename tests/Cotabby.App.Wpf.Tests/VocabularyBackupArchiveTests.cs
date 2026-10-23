using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Real private Windows SQLite/media and atomic file publication. Fixture setup writes
/// through the real media owner before seeding metadata; the exercised BackupAsync is untouched.
/// Foundation's raw missing-file error and the Windows media boundary's fixed InvalidContent are
/// intentionally distinct error classes, while failure/no-write and graph/file effects must agree.</summary>
[Collection("WPF views")]
public sealed class VocabularyBackupArchiveTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-backup-archive-v1.json"))).RootElement.Clone();
    private static readonly byte[] Input = Convert.FromBase64String(Corpus.GetProperty("input").GetString()!);
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalFullBackupPreservesGraphMediaAndProtectedDestinations(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(22, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift", row.GetProperty("macSource").GetString());
        var expected = row.GetProperty("expected");
        using var folder = new VocabularyExportFileTests.Folder(); await using var archive = new VocabularyArchive(folder.Notebook);
        var image = Reference(); string destination = Path.Combine(folder.Path, "备份.lingrove"), media = folder.Notebook + ".media";
        byte[] old = [9, 8, 7]; ulong? before = null;
        bool protectedCase = id.StartsWith("protected-", StringComparison.Ordinal) || id is "hard-link" or "symbolic-link" or "media-root" or "media-child";
        if (id.StartsWith("protected-", StringComparison.Ordinal))
        { destination = folder.Notebook + row.GetProperty("suffix").GetString(); File.WriteAllBytes(destination, old); }
        else if (id is "hard-link" or "symbolic-link")
        {
            File.WriteAllBytes(folder.Notebook, old);
            if (id == "hard-link") Assert.True(CreateHardLinkW(destination, folder.Notebook, IntPtr.Zero));
            else File.CreateSymbolicLink(destination, folder.Notebook);
        }
        else if (id is "media-root" or "media-child")
        {
            VocabularyWindowsFile.CreateParentDirectory(media); string child = Path.Combine(media, "child"); File.WriteAllBytes(child, old);
            destination = id == "media-root" ? media : child;
        }
        else
        {
            var document = Graph(id, image);
            if (id == "mismatch") document = document with { Entries = [document.Entries[0] with
                { Senses = new[] { document.Entries[0].Senses[0] with { Content = [Block(20, image with { Width = 81 })] } }.Concat(document.Entries[0].Senses.Skip(1)).ToArray() }] };
            if (id != "missing" && (id is "sense" or "shared" or "legacy" or "preserved" or "card" or "template" or "all" or "mismatch" or "replace" or "unreferenced"))
                Assert.True(new WindowsVocabularyImageStorage(folder.Notebook).Write(Input, image));
            await archive.CommitAsync(document); before = (await archive.LoadAsync()).Revision;
            if (id == "replace") File.WriteAllBytes(destination, old);
            if (id == "media-sibling")
            { string sibling = media + "-keep"; VocabularyWindowsFile.CreateParentDirectory(sibling); destination = Path.Combine(sibling, "backup.lingrove"); }
        }
        Exception? failure = await Record.ExceptionAsync(() => archive.BackupAsync(destination));
        var nativeError = expected.GetProperty("error"); Assert.Equal(nativeError.ValueKind != JsonValueKind.Null, failure is not null);
        if (failure is not null)
        {
            // Keep the actual native failure in the corpus. Windows storage deliberately sanitizes
            // filesystem reader failures; it does not expose a Foundation NSError or local path.
            Assert.Contains(nativeError.GetString(), new[] { "invalidContent", "io" });
            Assert.Equal(VocabularyFailure.InvalidContent, Assert.IsType<VocabularyException>(failure).Failure);
        }
        byte[]? bytes = Read(destination); JsonNode? json = null;
        if (failure is null) json = JsonNode.Parse(bytes!);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("backup").GetRawText()), json), id);
        Assert.Equal(expected.GetProperty("revisionUnchanged").GetBoolean(), before is null || (await archive.LoadAsync()).Revision == before);
        bool unchanged = !protectedCase || Same(bytes, old) || id == "media-root" && Same(Read(Path.Combine(media, "child")), old);
        Assert.Equal(expected.GetProperty("protectedUnchanged").GetBoolean(), unchanged);
        Assert.Equal(expected.GetProperty("notebookExists").GetBoolean(), File.Exists(folder.Notebook));
        Assert.Equal(expected.GetProperty("stages").GetInt32(), Directory.EnumerateFiles(folder.Path, ".notebook-*", SearchOption.AllDirectories).Count());
    }
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12"));
    private static VocabularyImageReference Reference() => new(Convert.ToHexStringLower(SHA256.HashData(Input)), 80, 40, Input.Length);
    private static VocabularyContentBlock Block(int n, VocabularyImageReference image) => new(Id(n), new VocabularyBlockContent.Image(image, "river"));
    private static ReviewLayout Layout(int part, int block, VocabularyImageReference image) => new([new(Id(part), ReviewContentSource.Custom, content: [Block(block, image)])]);
    private static VocabularyDocument Graph(string mode, VocabularyImageReference image)
    {
        var now = DateTimeOffset.UnixEpoch;
        var usage = new VocabularyExample(Id(3), "The bank.", "原句", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "context", now);
        VocabularySense[] senses = [new(Id(2), "core", now, now) { Note = "note", Tags = ["Work"], Examples = [usage], MergedFrom = [Id(90)] }, new(Id(4), "second", now, now)];
        if (mode is "sense" or "shared" or "all" or "missing" or "mismatch" or "replace") senses[0] = senses[0] with { Content = [Block(20, image)] };
        if (mode is "shared" or "all") senses[1] = senses[1] with { Content = [Block(21, image)] };
        if (mode is "legacy" or "preserved" or "all")
        {
            var binding = new AnkiNoteBinding(Id(40), Id(2), "http://localhost", "fixture", 7, "Basic", "Default", "ID",
                new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID" }, new Dictionary<string, string> { ["ID"] = Id(2).ToString("D") });
            senses[0] = senses[0] with { Review = new(new(Id(30)) { Layout = mode == "preserved" ? null : Layout(50, 22, image) },
                [binding], binding.Id, mode == "legacy" ? [] : [new(Id(31)) { Layout = Layout(51, 23, image) }]) };
        }
        if (mode is "card" or "all") senses[0] = senses[0] with { ReviewCards = [new(new(Id(32)) { Layout = Layout(52, 24, image) })] };
        return new() { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, senses, now, now)],
            ReviewTemplates = mode is "template" or "all" ? [new(Id(70), "Template", Layout(53, 25, image))] : null };
    }
    [Fact]
    public async Task ClosedArchiveCannotCreateABackupDestination()
    {
        using var folder = new VocabularyExportFileTests.Folder(); var archive = new VocabularyArchive(folder.Notebook); await archive.DisposeAsync();
        Assert.Equal(VocabularyFailure.InvalidArchive, (await Assert.ThrowsAsync<VocabularyException>(() => archive.BackupAsync(folder.Export))).Failure);
        Assert.False(File.Exists(folder.Export)); Assert.False(File.Exists(folder.Notebook));
    }
    private sealed class ObservedImages(WindowsVocabularyImageStorage actual, bool hold) : IVocabularyImageStorage
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads;
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> bytes, CancellationToken token) => actual.Prepare(bytes, token);
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken token) => actual.PrepareFile(path, token);
        public bool Write(ReadOnlySpan<byte> bytes, VocabularyImageReference image) => actual.Write(bytes, image);
        public void Remove(VocabularyImageReference image) => actual.Remove(image);
        public byte[] Read(VocabularyImageReference image)
        {
            Interlocked.Increment(ref Reads); Entered.TrySetResult();
            if (hold) Release.Task.GetAwaiter().GetResult(); // Test controls ordering, never the returned native bytes.
            return actual.Read(image);
        }
    }
    [Fact]
    public async Task SharedReferenceAcrossAllPlansAndTemplatesIsReadOnlyOnce()
    {
        using var folder = new VocabularyExportFileTests.Folder(); var actual = new WindowsVocabularyImageStorage(folder.Notebook);
        var observed = new ObservedImages(actual, hold: false); var image = Reference(); Assert.True(actual.Write(Input, image));
        await using var archive = new VocabularyArchive(() => VocabularySQLiteDatabase.OpenFile(folder.Notebook), images: observed);
        var graph = Graph("all", image); await archive.CommitAsync(graph); await archive.BackupAsync(folder.Export);
        Assert.Equal(1, observed.Reads);
        var backup = VocabularyBackupCodec.Decode(File.ReadAllBytes(folder.Export)); Assert.Equal(graph, backup.Document);
        Assert.Equal(Input, Assert.Single(backup.CopyImages()).Value); Assert.Equal(Input, actual.Read(image));
    }
    [Fact]
    public async Task BackupDrainsBeforeQueuedDeletionAndRetirementAndKeepsItsOwnBytes()
    {
        using var folder = new VocabularyExportFileTests.Folder(); var actual = new WindowsVocabularyImageStorage(folder.Notebook);
        var observed = new ObservedImages(actual, hold: true); var image = Reference(); Assert.True(actual.Write(Input, image));
        var archive = new VocabularyArchive(() => VocabularySQLiteDatabase.OpenFile(folder.Notebook), images: observed);
        try
        {
            var graph = Graph("sense", image); await archive.CommitAsync(graph); Task backup = archive.BackupAsync(folder.Export);
            await observed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task deletion = archive.RemoveEntryAsync(Id(1)), retirement = archive.DisposeAsync().AsTask();
            Assert.False(backup.IsCompleted); Assert.False(deletion.IsCompleted); Assert.False(retirement.IsCompleted);
            Assert.False(File.Exists(folder.Export));
            Assert.Equal(VocabularyFailure.InvalidArchive, (await Assert.ThrowsAsync<VocabularyException>(() => archive.BackupAsync(folder.Export))).Failure);
            observed.Release.TrySetResult(); await Task.WhenAll(backup, deletion, retirement);
            var captured = VocabularyBackupCodec.Decode(File.ReadAllBytes(folder.Export)); Assert.Equal(graph, captured.Document); Assert.Equal(Input, Assert.Single(captured.CopyImages()).Value);
            await using var reopened = new VocabularyArchive(folder.Notebook); Assert.Empty((await reopened.LoadAsync()).Document.Entries);
            Assert.Throws<VocabularyException>(() => actual.Read(image));
        }
        finally { observed.Release.TrySetResult(); await archive.DisposeAsync(); }
    }
    private static byte[]? Read(string path)
    { try { return File.Exists(path) ? File.ReadAllBytes(path) : null; } catch (UnauthorizedAccessException) { return null; } }
    private static bool Same(byte[]? a, byte[] b) => a is not null && a.AsSpan().SequenceEqual(b);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string name, string existing, IntPtr security);
}
