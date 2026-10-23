using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Exercises the actual SQLite owner; gates control scheduling, never persistence answers.</summary>
public sealed class VocabularyArchiveLifetimeTests
{
    private static VocabularyCapture Capture(int index) => new("word" + index, "word" + index,
        LearningLanguage.English, VocabularyEntryKind.Word, "meaning");

    [Fact]
    public async Task ConstructionAndUnusedDisposalNeverOpenStorage()
    {
        int opens = 0;
        var archive = new VocabularyArchive(() => { Interlocked.Increment(ref opens); throw new InvalidOperationException(); });
        Assert.Equal(0, opens);
        await archive.DisposeAsync(); await archive.DisposeAsync();
        Assert.Equal(0, opens);
        Assert.Equal(VocabularyFailure.InvalidArchive,
            (await Assert.ThrowsAsync<VocabularyException>(() => archive.LoadAsync())).Failure);
    }

    [Fact]
    public async Task ConcurrentCapturesSerializeWithoutLostUpdatesAndOpenOnceOffCaller()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int opens = 0, caller = Environment.CurrentManagedThreadId, worker = caller;
        await using var archive = new VocabularyArchive(() =>
        {
            worker = Environment.CurrentManagedThreadId; Interlocked.Increment(ref opens); entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return VocabularySQLiteDatabase.CreateMemory();
        });
        var first = archive.SaveAsync(Capture(0));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10))); Assert.NotEqual(caller, worker);
            var rest = Enumerable.Range(1, 30).Select(index => archive.SaveAsync(Capture(index))).ToArray();
            Assert.All(rest, task => Assert.False(task.IsCompleted));
            release.Set();
            var snapshots = await Task.WhenAll(new[] { first }.Concat(rest)).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(Enumerable.Range(1, 31).Select(value => (ulong)value), snapshots.Select(value => value.Revision).Order());
            var final = await archive.LoadAsync(); Assert.Equal(31ul, final.Revision); Assert.Equal(31, final.Document.Entries.Count);
            Assert.Equal(1, opens);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task CloseRejectsNewWorkAndDrainsAllAdmittedCommitsBeforeClosingDatabase()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        VocabularySQLiteDatabase? db = null;
        var archive = new VocabularyArchive(() =>
        {
            entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return db = VocabularySQLiteDatabase.CreateMemory();
        });
        try
        {
            var first = archive.SaveAsync(Capture(0)); Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var second = archive.SaveAsync(Capture(1)); var closing = archive.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.Equal(VocabularyFailure.InvalidArchive,
                (await Assert.ThrowsAsync<VocabularyException>(() => archive.SaveAsync(Capture(2)))).Failure);
            Assert.Same(closing, archive.DisposeAsync().AsTask());
            release.Set();
            var saved = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(2ul, saved.Max(value => value.Revision)); Assert.Equal(2, saved.Max(value => value.Document.Entries.Count));
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(db); Assert.Equal(VocabularyFailure.InvalidArchive, Assert.Throws<VocabularyException>(() => db.Read()).Failure);
        }
        finally { release.Set(); await archive.DisposeAsync(); }
    }

    [Fact]
    public async Task FailedOpenDoesNotPoisonQueueAndCanBeRetried()
    {
        int attempts = 0;
        await using var archive = new VocabularyArchive(() => Interlocked.Increment(ref attempts) == 1
            ? throw new VocabularyException(VocabularyFailure.InvalidArchive) : VocabularySQLiteDatabase.CreateMemory());
        await Assert.ThrowsAsync<VocabularyException>(() => archive.LoadAsync());
        var saved = await archive.SaveAsync(Capture(0)); Assert.Equal(1ul, saved.Revision); Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task FailedEditReleasesQueueAndDisposeStillFinishes()
    {
        var archive = new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory);
        await archive.SaveAsync(Capture(0));
        await Assert.ThrowsAsync<VocabularyException>(() => archive.RemoveSenseAsync(Guid.NewGuid(), Guid.NewGuid()));
        var saved = await archive.SaveAsync(Capture(1)); Assert.Equal(2ul, saved.Revision); Assert.Equal(2, saved.Document.Entries.Count);
        await archive.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task InvalidLoadedGraphIsNotCachedAndRepairedRevisionIsReloaded()
    {
        var db = VocabularySQLiteDatabase.CreateMemory(); await using var archive = new VocabularyArchive(() => db);
        var valid = VocabularySQLiteParityTests.Document();
        var invalid = VocabularySQLiteParityTests.Sense(valid, sense => sense with
            { Examples = [sense.Examples[0] with { Ranges = [new(999, 1)] }] });
        db.Write(invalid, new(), 0);
        await Assert.ThrowsAsync<VocabularyException>(() => archive.LoadAsync());
        db.Write(valid, invalid, 1);
        var repaired = await archive.LoadAsync(); Assert.Equal(2ul, repaired.Revision); Assert.Equal(valid, repaired.Document);
        var changed = VocabularySQLiteParityTests.Sense(valid, sense => sense with { Note = "external" });
        db.Write(changed, valid, 2);
        var refreshed = await archive.LoadAsync(); Assert.Equal(3ul, refreshed.Revision); Assert.Equal(changed, refreshed.Document);
    }
}
