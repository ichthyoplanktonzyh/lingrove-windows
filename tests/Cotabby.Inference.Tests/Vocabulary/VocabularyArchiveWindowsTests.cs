using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests.Vocabulary;

/// <summary>Actual archive-to-private-file composition, separate from portable actor observations.</summary>
public sealed class VocabularyArchiveWindowsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-archive-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "vocabulary.sqlite");
    private static VocabularyCapture Capture(string word) => new(word, word, LearningLanguage.English, VocabularyEntryKind.Word, "meaning");
    private static void SameSnapshot(VocabularySnapshot expected, VocabularySnapshot actual)
    { Assert.Equal(expected.Revision, actual.Revision); Assert.Equal(expected.Document, actual.Document); }

    [Fact]
    public async Task SaveIsAcknowledgedAndRetainsIdentityAfterArchiveRestart()
    {
        VocabularySnapshot saved;
        await using (var archive = new VocabularyArchive(FilePath)) saved = await archive.SaveAsync(Capture("bank"));
        await using (var reopened = new VocabularyArchive(FilePath))
        {
            var loaded = await reopened.LoadAsync(); SameSnapshot(saved, loaded);
            Assert.True(VocabularyPolicy.Contains(Capture("bank"), loaded.Document.Entries));
            var removed = await reopened.RemoveAsync(Capture("bank")); Assert.Equal(2ul, removed.Revision); Assert.Empty(removed.Document.Entries);
        }
        await using var final = new VocabularyArchive(FilePath); Assert.Empty((await final.LoadAsync()).Document.Entries);
    }

    [Fact]
    public async Task TwoArchiveOwnersRejectStaleCandidateAndReloadWinner()
    {
        await using var first = new VocabularyArchive(FilePath); await using var second = new VocabularyArchive(FilePath);
        var baseline = await first.LoadAsync(); await second.LoadAsync();
        var winner = await second.SaveAsync(Capture("bank"));
        var candidate = VocabularyPolicy.Save(Capture("river"), baseline.Document, DateTimeOffset.UnixEpoch).Document;
        var error = await Assert.ThrowsAsync<VocabularyException>(() => first.CommitAsync(candidate));
        Assert.Equal(VocabularyFailure.EditConflict, error.Failure);
        SameSnapshot(winner, await first.LoadAsync());
        var next = await first.SaveAsync(Capture("river")); Assert.Equal(2ul, next.Revision); Assert.Equal(2, next.Document.Entries.Count);
        SameSnapshot(next, await second.LoadAsync());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
