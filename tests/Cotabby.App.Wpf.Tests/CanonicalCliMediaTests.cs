using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalCliMediaTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ImageAndReceiptRollbackAndRetryUseSameStore() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-cli-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            byte[] bytes = Convert.FromBase64String(JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json"))).RootElement.GetProperty("input").GetString()!);
            var batch = JsonNode.Parse("""{"version":1,"idempotencyKey":"image","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"money","content":[{"image":{"data":"PLACEHOLDER"}}]}}]}""".Replace("PLACEHOLDER", Convert.ToBase64String(bytes)))!.AsObject();
            var request = new CliRequest { Command = "apply", ExpiresAt = CliWire.Now() + 60, Batch = batch };
            await using var store = FrozenVocabularyStoreFactory.Create(file, false); var before = await store.ReadAsync();
            var cli = Assert.IsAssignableFrom<F.ICanonicalVocabularyCliStore>(store);
            using var connection = VocabularySQLiteConnection.OpenNamed(file);
            connection.Execute("CREATE TRIGGER cli_media_abort BEFORE UPDATE ON state BEGIN SELECT RAISE(ABORT,'injected'); END");
            await Assert.ThrowsAnyAsync<VocabularyException>(() => cli.ExecuteCliAsync(request));
            Assert.Equal(before, await store.ReadAsync());
            string media = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName);
            Assert.Empty(Directory.EnumerateFiles(media, "lg-image-*.png"));
            connection.Execute("DROP TRIGGER cli_media_abort");
            var result = await cli.ExecuteCliAsync(request); Assert.False(result["replayed"]!.GetValue<bool>());
            var snapshot = await store.ReadMediaSnapshotAsync(); Assert.Single(snapshot.Images);
            Assert.Equal(1UL, snapshot.Snapshot.Revision); Assert.True(File.Exists(Path.Combine(media, snapshot.Images.Single().Reference.Filename)));
            Assert.True((await cli.ExecuteCliAsync(request))["replayed"]!.GetValue<bool>());
            Assert.Equal(1UL, (await store.ReadAsync()).Revision);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
