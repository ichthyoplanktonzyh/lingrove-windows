using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalVocabularyEditorStoreTests(WpfDispatcher dispatcher)
{
    private static VocabularyPreparedImage Image()
    {
        byte[] bytes = Convert.FromBase64String(JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json"))).RootElement.GetProperty("input").GetString()!);
        return new(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes);
    }
    private static F.VocabularyEntry Edit(F.VocabularyEntry entry, Func<F.VocabularySense, F.VocabularySense> edit) => entry with { Senses = [edit(entry.Senses[0])] };
    [Fact]
    public Task SaveAcknowledgesDraftAndRetainsConcurrentCaptureAcrossReopen() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-editor-" + Guid.NewGuid().ToString("N")); string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            var image = Image(); F.FrozenVocabularyImportSnapshot saved;
            await using (var store = FrozenVocabularyStoreFactory.Create(file, false))
            {
                await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello there")]);
                var before = await store.ReadAsync(); var baseline = before.Document.Entries[0];
                var draft = Edit(baseline, sense => sense with { Meaning = "welcome", Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image.Reference))] });
                await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello again")]);
                var submitted = new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = image };
                saved = await store.UpdateEntryAsync(draft, baseline, submitted);
                Assert.Equal("welcome", saved.Document.CollectedEntries[0].Senses[0].Meaning);
                Assert.Equal(2, saved.Document.CollectedEntries[0].Senses[0].Examples.Count);
                Assert.Equal(image.CopyBytes(), await store.ReadImageAsync(image.Reference));
                await Assert.ThrowsAsync<VocabularyException>(() => store.UpdateEntryAsync(Edit(baseline, sense => sense with { Meaning = "conflict" }), baseline));
                Assert.Equal(saved, await store.ReadAsync());
            }
            await using var reopened = await FrozenVocabularyStoreFactory.OpenAsync(file, installBundledClassification: false);
            Assert.Equal(saved, await reopened.ReadAsync()); Assert.Equal(image.CopyBytes(), await reopened.ReadImageAsync(image.Reference));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task FailedSqlAfterImageStageLeavesDraftAndEvidenceUntouched() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-editor-" + Guid.NewGuid().ToString("N")); string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(file, false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello there")]);
            var before = await store.ReadAsync(); var baseline = before.Document.Entries[0]; var image = Image();
            var draft = Edit(baseline, sense => sense with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image.Reference))] });
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpdateEntryAsync(draft, baseline, cancellationToken: cancelled.Token));
            Assert.Equal(before, await store.ReadAsync());
            using var connection = VocabularySQLiteConnection.OpenNamed(file);
            connection.Execute("CREATE TRIGGER reject_editor BEFORE UPDATE ON state BEGIN SELECT RAISE(ABORT,'injected'); END");
            await Assert.ThrowsAnyAsync<VocabularyException>(() => store.UpdateEntryAsync(draft, baseline, new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = image }));
            Assert.Equal(before, await store.ReadAsync()); Assert.NotNull(draft.Senses[0].Content);
            Assert.False(File.Exists(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName, image.Reference.Filename)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
