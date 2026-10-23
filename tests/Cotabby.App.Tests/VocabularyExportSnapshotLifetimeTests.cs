using System.Security.Cryptography;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Real memory SQLite and actual export sequencing, with an explicit opaque media-reader
/// boundary for portable scheduling/cancellation tests. These cases make no WIC/pixel/PNG claim.</summary>
public sealed class VocabularyExportSnapshotLifetimeTests
{
    private static readonly Guid EntryId = Guid.Parse("00000000-0000-0000-0000-000000000001"), SenseId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly byte[] Bytes = [1, 2, 3];
    private static readonly VocabularyImageReference Image = new(Convert.ToHexStringLower(SHA256.HashData(Bytes)), 1, 1, Bytes.Length);
    private static readonly VocabularySelection[] Selection = [new(EntryId, SenseId)];
    private static VocabularyDocument Document() => new() { Entries = [new(EntryId, "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
        [new(SenseId, "core", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { Content = [new(Guid.NewGuid(), new VocabularyBlockContent.Image(Image))] }], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)] };
    private static VocabularyArchive Archive(OpaqueMedia media) => new(VocabularySQLiteDatabase.CreateMemory, images: media);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DisabledMediaOrContentNeverTouchesTheImageReader(bool images, bool content)
    {
        using var media = new OpaqueMedia { Failure = new InvalidOperationException("Reader must remain lazy.") }; await using var archive = Archive(media);
        await archive.CommitAsync(Document()); var p = await archive.PrepareExportAsync(Selection, new() { Images = images, Content = content });
        Assert.Empty(p.CopyImages()); Assert.Equal(0, media.Reads); Assert.Equal(1L, p.SenseCount);
    }
    [Fact]
    public async Task QueuedScopesDetachAndPreparedBytesSurviveLaterSourceRemoval()
    {
        using var media = new OpaqueMedia { BlockFirstRead = true }; await using var archive = Archive(media); await archive.CommitAsync(Document());
        var first = archive.PrepareExportAsync(Selection, new() { Images = true });
        try
        {
            await media.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var mutableScope = Selection.ToList(); var second = archive.PrepareExportAsync(mutableScope, new() { Images = true }); mutableScope.Clear();
            var removal = archive.RemoveEntryAsync(EntryId); Assert.False(second.IsCompleted); Assert.False(removal.IsCompleted);
            media.Release.Set();
            var prepared = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15)); await removal;
            Assert.All(prepared, p => { Assert.Equal(1L, p.SenseCount); Assert.Equal(Bytes, Assert.Single(p.CopyImages()).Value); });
            Assert.Empty((await archive.LoadAsync()).Document.Entries); Assert.False(media.Available); Assert.Equal(2, media.Reads);
        }
        finally { media.Release.Set(); }
    }
    [Fact]
    public async Task DisposalDrainsActivePreparationAndClosesFurtherAdmission()
    {
        using var media = new OpaqueMedia { BlockFirstRead = true }; var archive = Archive(media); await archive.CommitAsync(Document());
        var preparing = archive.PrepareExportAsync(Selection, new() { Images = true });
        try
        {
            await media.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); var closing = archive.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
            Assert.Equal(VocabularyFailure.InvalidArchive, (await Assert.ThrowsAsync<VocabularyException>(() => archive.PrepareExportAsync(Selection, new()))).Failure);
            media.Release.Set(); var p = await preparing.WaitAsync(TimeSpan.FromSeconds(15)); await closing.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(Bytes, Assert.Single(p.CopyImages()).Value);
        }
        finally { media.Release.Set(); await archive.DisposeAsync(); }
    }
    [Fact]
    public async Task ReaderFailureMapsToFixedMissingImageWithoutPoisoningTheQueue()
    {
        using var media = new OpaqueMedia { Failure = new VocabularyException(VocabularyFailure.InvalidContent) }; await using var archive = Archive(media);
        var before = await archive.CommitAsync(Document());
        Assert.Equal(VocabularyExportFailure.MissingImage, (await Assert.ThrowsAsync<VocabularyExportException>(() => archive.PrepareExportAsync(Selection, new() { Images = true }))).Failure);
        Assert.Equal(before.Revision, (await archive.LoadAsync()).Revision);
        Assert.Equal(1L, (await archive.PrepareExportAsync(Selection, new())).SenseCount); Assert.Equal(1, media.Reads);
    }
    [Fact]
    public async Task CompleteReferenceBudgetIsRejectedBeforeAnyImageRead()
    {
        using var media = new OpaqueMedia(); await using var archive = Archive(media); var d = Document();
        var senses = Enumerable.Range(0, 9).Select(owner => new VocabularySense(Guid.NewGuid(), "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) {
            Content = Enumerable.Range(0, 4).Select(part => new VocabularyContentBlock(Guid.NewGuid(), new VocabularyBlockContent.Image(new((owner * 4 + part).ToString("x64"), 1, 1, VocabularyContentPolicy.MaximumImageBytes)))).ToArray() }).ToArray();
        d = d with { Entries = [d.Entries[0] with { Senses = senses }] }; var before = await archive.CommitAsync(d);
        Assert.Equal(VocabularyExportFailure.TooLarge, (await Assert.ThrowsAsync<VocabularyExportException>(() => archive.PrepareExportAsync(senses.Select(s => new VocabularySelection(EntryId, s.Id)).ToArray(), new() { Images = true }))).Failure);
        Assert.Equal(0, media.Reads); Assert.Equal(before.Revision, (await archive.LoadAsync()).Revision);
    }
    private sealed class OpaqueMedia : IVocabularyImageStorage, IDisposable
    {
        internal Exception? Failure { get; init; }
        internal bool BlockFirstRead { get; init; }
        internal int Reads;
        internal bool Available = true;
        internal TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        public byte[] Read(VocabularyImageReference image)
        {
            int read = Interlocked.Increment(ref Reads);
            if (Failure is { } failure) throw failure;
            if (BlockFirstRead && read == 1) { Entered.TrySetResult(true); if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException(); }
            if (!Available) throw new VocabularyException(VocabularyFailure.InvalidContent); return (byte[])Bytes.Clone();
        }
        public void Remove(VocabularyImageReference image) => Available = false;
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image) => throw new NotSupportedException();
        public void Dispose() => Release.Dispose();
    }
}
