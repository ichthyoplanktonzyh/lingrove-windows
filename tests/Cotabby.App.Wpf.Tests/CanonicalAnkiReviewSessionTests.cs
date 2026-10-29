using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalAnkiReviewSessionTests(WpfDispatcher dispatcher)
{
    private static AnkiReviewSettings Settings() { var values = new AppSettings(); return new(() => values, () => { }, new AnkiTestCredentials()); }
    private static async Task<(Guid Entry, Guid Sense)> Seed(Cotabby.Core.Vocabulary.Frozen.ICanonicalVocabularyStore store)
    {
        await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money", Example: "bank opens")]);
        var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; return (entry.Id, entry.Senses[0].Id);
    }
    private static VocabularyPreparedImage Image()
    {
        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
        byte[] bytes = Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!);
        return new(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes);
    }
    [Fact]
    public Task LostReplyRetainsPendingIdentityAndReopenDoesNotDuplicate() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            var server = new SessionTestServer { LoseAdd = true }; var settings = Settings(); Guid entry, sense, pending;
            await using (var store = FrozenVocabularyStoreFactory.Create(path, false))
            {
                (entry, sense) = await Seed(store); var controller = new AnkiReviewController(settings, null, server);
                var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense);
                Assert.False(await session.SendAsync()); Assert.Equal(1, server.AddCount);
                var saved = (await store.ReadAsync()).Document.Collections[0].Review!.ActiveBinding!;
                pending = saved.Id; Assert.Null(saved.NoteId); Assert.Equal(sense, saved.OriginSenseId);
            }
            await using (var store = await FrozenVocabularyStoreFactory.OpenAsync(path, installBundledClassification: false))
            {
                var target = await store.CreateSenseAsync(entry);
                await store.MergeSensesAsync(entry, sense, target.SenseId);
                var controller = new AnkiReviewController(settings, null, server);
                var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, target.SenseId);
                Assert.True(await session.SendAsync()); Assert.Equal(1, server.AddCount);
                var receipt = (await store.ReadAsync()).Document.Collections[0].Review!.ActiveBinding!;
                Assert.Equal(pending, receipt.Id); Assert.Equal(sense, receipt.OriginSenseId); Assert.NotNull(receipt.NoteId);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task AudioFailsBeforeRemoteWritesAndStaleDraftCannotOverwrite() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var server = new SessionTestServer(); var controller = new AnkiReviewController(Settings(), null, server);
            var first = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense);
            var second = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense);
            first.Plan = first.Plan with { AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression } };
            Assert.False(await first.SendAsync()); Assert.Contains("Speech", first.Message); Assert.Empty(server.Calls);
            first.Plan = first.Plan with { AudioKinds = null }; Assert.True(await first.SaveAsync());
            second.Plan = second.Plan with { Answer = "conflicting" }; Assert.False(await second.SaveAsync());
            Assert.Equal("money", (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].Meaning);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task ImageUploadPrecedesNoteWriteAndMissingFileNeverSends() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entryId, senseId) = await Seed(store);
            var snapshot = await store.ReadAsync(); var entry = snapshot.Document.Entries[0]; var image = Image();
            var edited = entry with { Senses = [entry.Senses[0] with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image.Reference))] }] };
            await store.UpdateEntryAsync(edited, entry, new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = image });
            var server = new SessionTestServer(); var controller = new AnkiReviewController(Settings(), null, server);
            var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entryId, senseId);
            Assert.True(await session.SendAsync()); Assert.Equal(1, server.AddCount);
            var actions = server.Calls.Select(value => value.GetType().GetProperty("action")!.GetValue(value)!.ToString()).ToArray();
            Assert.True(Array.IndexOf(actions, "storeMediaFile") < Array.IndexOf(actions, "addNote"));
            File.Delete(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName, image.Reference.Filename));
            var next = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entryId, senseId);
            Assert.False(await next.SendAsync()); Assert.Equal(1, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task RemoteConflictRequiresExplicitChoiceAndPreservesRemoteValue() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var server = new SessionTestServer(); var controller = new AnkiReviewController(Settings(), null, server);
            var first = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense); Assert.True(await first.SendAsync());
            var id = (await store.ReadAsync()).Document.Collections[0].Review!.ActiveBinding!.NoteId!.Value;
            server.Edit(id, "Meaning", "user remote");
            var second = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense);
            Assert.False(await second.SendAsync()); Assert.Contains(second.Conflicts, value => value.Id == "Meaning");
            second.SetKeepRemote("Meaning", true); Assert.True(await second.SendAsync());
            Assert.Equal("user remote", (await store.ReadAsync()).Document.Collections[0].Review!.Plan.FieldOverrides["Meaning"]);
            Assert.Equal(1, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task AudioAddedDuringMetadataReadPreventsEveryRemoteWrite() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var server = new SessionTestServer(); var controller = new AnkiReviewController(Settings(), null, server);
            var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense);
            server.AfterMetadata = () => session.Plan = session.Plan with { AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression } };
            Assert.False(await session.SendAsync()); Assert.Contains("Speech", session.Message);
            var actions = server.Calls.Select(value => value.GetType().GetProperty("action")!.GetValue(value)!.ToString()).ToArray();
            Assert.DoesNotContain("addNote", actions); Assert.DoesNotContain("createModel", actions); Assert.DoesNotContain("storeMediaFile", actions);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task ChangedSourceDuringMetadataRejectsRemoteWrite() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var server = new SessionTestServer(); var controller = new AnkiReviewController(Settings(), null, server);
            var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense);
            server.AfterMetadataAsync = async () =>
            {
                var current = (await store.ReadAsync()).Document.Entries[0];
                var edit = current with { Senses = [current.Senses[0] with { Meaning = "new source" }] };
                await store.UpdateEntryAsync(edit, current);
            };
            Assert.False(await session.SendAsync());
            var actions = server.Calls.Select(value => value.GetType().GetProperty("action")!.GetValue(value)!.ToString()).ToArray();
            Assert.DoesNotContain("addNote", actions); Assert.DoesNotContain("createModel", actions); Assert.DoesNotContain("storeMediaFile", actions);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task CancellationAfterVerifiedNoteStillAcknowledgesReceipt() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            using var cancellation = new CancellationTokenSource(); var server = new SessionTestServer { AfterNote = cancellation.Cancel };
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry, sense);
            Assert.True(await session.SendAsync(cancellationToken: cancellation.Token));
            Assert.True(cancellation.IsCancellationRequested); Assert.NotNull((await store.ReadAsync()).Document.Collections[0].Review!.ActiveBinding!.NoteId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task HeldSendUsesExistingControllerLeaseWithoutEnablingLegacyNotebook() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new SessionTestServer { AfterAdd = async () => { entered.TrySetResult(); await release.Task; } };
            var controller = new AnkiReviewController(Settings(), null, server);
            var first = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense);
            var second = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense);
            var send = first.SendAsync();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(controller.NotebookAvailable);
                Assert.False(await second.SendAsync()); Assert.Contains("current Anki operation", second.Message);
                Assert.Equal(sense, controller.SendingSenseId); Assert.Equal(1, server.AddCount);
            }
            finally { release.TrySetResult(); await send.WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.True(send.Result); Assert.Null(controller.SendingSenseId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task StoreNotificationsMarshalAndRetirementDetachesWithoutClosingStore() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, new SessionTestServer()), store, entry, sense);
            var dispatcherThread = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.PropertyChanged += (_, _) => { Assert.True(dispatcherThread.CheckAccess()); seen.TrySetResult(); };
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "other")]);
            await seen.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal((await store.ReadAsync()).Revision, session.Snapshot.Revision);
            var revision = session.Snapshot.Revision; session.Dispose();
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "third")]);
            Assert.True((await store.ReadAsync()).Revision > revision); Assert.Equal(revision, session.Snapshot.Revision);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task RetiredPresentationStillJoinsAcceptedSendAndAcknowledgesReceipt() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new SessionTestServer { AfterAdd = async () => { entered.TrySetResult(); await release.Task; } };
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry, sense);
            var send = session.SendAsync(); int notifications = 0;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); session.Dispose();
                session.PropertyChanged += (_, _) => Interlocked.Increment(ref notifications);
            }
            finally { release.TrySetResult(); await send.WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.True(send.Result); Assert.Equal(0, notifications);
            Assert.NotNull((await store.ReadAsync()).Document.Collections[0].Review!.ActiveBinding!.NoteId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task FlexibleCardSavesImportedImageBeforeExplicitSend() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var server = new SessionTestServer(); var image = Image(); var card = Guid.NewGuid();
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry, sense, card);
            var part = new ReviewCardPart(Guid.NewGuid(), ReviewContentSource.Custom, content: [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image.Reference))]);
            session.Plan = session.Plan with { Layout = new ReviewLayout([part], [ReviewCardPart.Create(ReviewContentSource.Meaning)]) };
            session.ImportedImages = new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = image };
            Assert.True(await session.SaveAsync()); Assert.Empty(server.Calls);
            Assert.Equal(image.CopyBytes(), await store.ReadImageAsync(image.Reference));
            Assert.True(await session.SendAsync()); Assert.Equal(1, server.AddCount);
            Assert.NotNull((await store.ReadAsync()).Document.Collections[0].ReviewCards![0].ActiveBinding!.NoteId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task ExplicitTemplateUpgradeUsesSharedControllerWithoutLegacyStore() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var server = new SessionTestServer(); server.Legacy(); var controller = new AnkiReviewController(Settings(), null, server);
            using var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry, sense);
            await session.LoadMetadataAsync(); Assert.True(session.LegacyPresetTemplate);
            await session.UpdateTemplateAsync(); Assert.Null(session.Message); Assert.True(session.CurrentPresetTemplate);
            Assert.False(controller.NotebookAvailable); Assert.False(controller.UpdatingTemplate);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task VerifiedReceiptFollowsSenseMergedDuringRemoteWrite() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, source) = await Seed(store);
            var target = (await store.CreateSenseAsync(entry)).SenseId;
            var server = new SessionTestServer { AfterAdd = async () => { await store.MergeSensesAsync(entry, source, target); } };
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry, source);
            Assert.True(await session.SendAsync());
            var receipt = (await store.ReadAsync()).Document.CollectedEntries[0].Senses.Single().Review!.ActiveBinding!;
            Assert.Equal(source, receipt.OriginSenseId); Assert.NotNull(receipt.NoteId); Assert.Equal(1, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task ProfileChangeDuringMetadataPreventsRemoteMutation() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var server = new SessionTestServer { ProfileAfterMetadata = "other" };
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry, sense);
            Assert.False(await session.SendAsync());
            var actions = server.Calls.Select(value => value.GetType().GetProperty("action")!.GetValue(value)!.ToString()).ToArray();
            Assert.DoesNotContain("addNote", actions); Assert.DoesNotContain("createModel", actions); Assert.DoesNotContain("storeMediaFile", actions);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
