using System.IO;
using System.Windows.Threading;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Exercises the actual AppHost notebook owner against isolated files. No AppHost,
/// keyboard hook, user notebook, model, endpoint or active editor is constructed.</summary>
[Collection("WPF views")]
public sealed class CanonicalVocabularyCompositionTests(WpfDispatcher dispatcher)
{
    private sealed class Preferences : IVocabularyNotebookListPreferences
    { public string Density { get; set; } = "compact"; public bool ShowUnexplored { get; set; } }

    private static CanonicalVocabularyFeature Create(string path) => new(path,
        new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher), Dispatcher.CurrentDispatcher,
        new Preferences(), [LearningLanguage.English]);

    [Fact]
    public Task UnusedAppOwnerDoesNotCreateNotebookOrDirectory() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-app-notebook-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        var feature = Create(path);
        feature.Window.SetPreferredLanguages([LearningLanguage.Chinese, LearningLanguage.English]);
        Assert.False(Directory.Exists(root));
        await feature.DisposeAsync();
        await feature.DisposeAsync();
        Assert.False(Directory.Exists(root));
        Assert.False(feature.Writing.IsLoaded);
    });

    [Fact]
    public Task AppOwnerDrainsAcceptedWritingAndReopensOnlyCurrentFile() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-app-notebook-" + Guid.NewGuid().ToString("N"));
        VocabularyWindowsFile.CreateParentDirectory(root);
        string oldPath = Path.Combine(root, "vocabulary.sqlite");
        byte[] oldBytes = [0x41, 0x7f, 0x00, 0xff, 0x22];
        await File.WriteAllBytesAsync(oldPath, oldBytes);
        string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        var capture = new VocabularyCapture("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
            Example: new(Guid.NewGuid(), "The bank.", "银行", [new(4, 4)], VocabularyExampleCompleteness.Sentence,
                "", DateTimeOffset.UnixEpoch));
        var feature = Create(path);
        try
        {
            Assert.False(File.Exists(path));
            await feature.Writing.LoadAsync();
            Assert.True(feature.Writing.IsLoaded);
            var save = feature.Writing.SaveAsync(capture);
            await feature.DisposeAsync();
            Assert.True(await save);
            Assert.Equal(oldBytes, await File.ReadAllBytesAsync(oldPath));
            await using var reopened = await FrozenVocabularyStoreFactory.OpenAsync(path);
            var snapshot = await reopened.ReadAsync();
            var entry = Assert.Single(snapshot.Document.CollectedEntries);
            Assert.Equal("bank", entry.Expression);
            Assert.Equal(capture.Example!.Id, Assert.Single(snapshot.Document.Interpretations).Id);
            Assert.NotEmpty(snapshot.Document.Classification.Frameworks);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => feature.Writing.SaveAsync(capture));
        }
        finally
        {
            await feature.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    });
}
