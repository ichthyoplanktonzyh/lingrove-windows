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
using Cotabby.Core.LanguageLearning;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalAnkiReviewSessionTests(WpfDispatcher dispatcher)
{
    internal sealed class HeldStore(F.ICanonicalVocabularyStore inner) : F.ICanonicalVocabularyStore
    {
        internal bool HoldRead, HoldImage, HoldSaveAck;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task Wait(CancellationToken token) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
        public async Task<F.FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default) { var value = await inner.ReadAsync(token); if (HoldRead) { HoldRead = false; await Wait(token); } return value; }
        public async Task<byte[]> ReadImageAsync(VocabularyImageReference image, CancellationToken token = default) { if (HoldImage) { HoldImage = false; await Wait(token); } return await inner.ReadImageAsync(image, token); }
        public async Task<F.FrozenVocabularyImportSnapshot> SaveReviewAsync(Guid e, Guid s, F.VocabularyReview v, F.VocabularyReview? b, Guid? c = null, IReadOnlyDictionary<string, VocabularyPreparedImage>? i = null, CancellationToken token = default) { var value = await inner.SaveReviewAsync(e, s, v, b, c, i, token); if (HoldSaveAck) { HoldSaveAck = false; await Wait(CancellationToken.None); } return value; }
        public IDisposable Subscribe(Action<F.FrozenVocabularyImportSnapshot> changed) => inner.Subscribe(changed);
        public Task<F.FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList t, VocabularyWordListConfiguration c, CancellationToken token = default) => inner.PreviewWordListAsync(t, c, token);
        public Task<F.VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> i, CancellationToken token = default) => inner.AddEntriesAsync(i, token);
        public Task<F.CanonicalWritingReceipt> SaveCaptureAsync(VocabularyCapture c, CancellationToken token = default) => inner.SaveCaptureAsync(c, token);
        public Task<F.CanonicalWritingReceipt> RemoveCaptureAsync(VocabularyCapture c, CancellationToken token = default) => inner.RemoveCaptureAsync(c, token);
        public Task<LearningExplanationRecord?> FindExplanationAsync(LearningExplanationInput i, LearningLanguage l, CancellationToken token = default) => inner.FindExplanationAsync(i, l, token);
        public Task<F.CanonicalWritingReceipt> SaveExplanationAsync(LearningExplanationRecord r, CancellationToken token = default) => inner.SaveExplanationAsync(r, token);
        public Task<F.CanonicalWritingReceipt> AdoptExplanationAsync(LearningExplanationRecord r, VocabularyExplanationDestination d, CancellationToken token = default) => inner.AdoptExplanationAsync(r, d, token);
        public Task<F.FrozenVocabularyImportSnapshot> RecordAnkiBindingAsync(Guid e, Guid s, F.AnkiNoteBinding b, Guid? c = null, CancellationToken token = default) => inner.RecordAnkiBindingAsync(e, s, b, c, token);
        public Task<F.FrozenVocabularyImportSnapshot> RemoveReviewAsync(Guid e, Guid s, Guid c, F.VocabularyReview b, CancellationToken token = default) => inner.RemoveReviewAsync(e, s, c, b, token);
        public Task<F.FrozenVocabularyImportSnapshot> SaveTemplateAsync(ReviewLayoutTemplate t, IReadOnlyDictionary<string, VocabularyPreparedImage>? i = null, CancellationToken token = default) => inner.SaveTemplateAsync(t, i, token);
        public Task<F.FrozenVocabularyImportSnapshot> RemoveTemplateAsync(Guid id, CancellationToken token = default) => inner.RemoveTemplateAsync(id, token);
        public Task<VocabularyPreparedImage> PrepareImageAsync(ReadOnlyMemory<byte> data, CancellationToken token = default) => inner.PrepareImageAsync(data, token);
        public Task<VocabularyPreparedImage> PrepareImageFileAsync(string path, CancellationToken token = default) => inner.PrepareImageFileAsync(path, token);
        public Task<F.CanonicalVocabularyMediaSnapshot> ReadMediaSnapshotAsync(CancellationToken token = default) => inner.ReadMediaSnapshotAsync(token);
        public Task<F.FrozenVocabularyImportSnapshot> UpdateEntryAsync(F.VocabularyEntry e, F.VocabularyEntry b, IReadOnlyDictionary<string, VocabularyPreparedImage>? i = null, CancellationToken token = default) => inner.UpdateEntryAsync(e, b, i, token);
        public Task<F.CanonicalSenseReceipt> CreateSenseAsync(Guid e, CancellationToken token = default) => inner.CreateSenseAsync(e, token);
        public Task<F.FrozenVocabularyImportSnapshot> MergeSensesAsync(Guid e, Guid s, Guid t, CancellationToken token = default) => inner.MergeSensesAsync(e, s, t, token);
        public Task<F.FrozenVocabularyImportSnapshot> RemoveSenseAsync(Guid e, Guid s, CancellationToken token = default) => inner.RemoveSenseAsync(e, s, token);
        public Task<F.CanonicalSenseReceipt> MoveExampleAsync(Guid e, Guid u, Guid s, Guid? t, CancellationToken token = default) => inner.MoveExampleAsync(e, u, s, t, token);
        public Task<F.FrozenVocabularyImportSnapshot> UpdateSemanticsAsync(F.VocabularySemanticIndex e, F.VocabularySemanticIndex b, CancellationToken token = default) => inner.UpdateSemanticsAsync(e, b, token);
        public Task<F.FrozenVocabularyImportSnapshot> UpdateClassificationAsync(Cotabby.Core.Vocabulary.Resources.VocabularyClassificationIndex e, Cotabby.Core.Vocabulary.Resources.VocabularyClassificationIndex b, CancellationToken token = default) => inner.UpdateClassificationAsync(e, b, token);
        public Task BackupAsync(string destination, CancellationToken token = default) => inner.BackupAsync(destination, token);
        public Task<F.FrozenVocabularyImportSnapshot> RestoreAsync(string source, CancellationToken token = default) => inner.RestoreAsync(source, token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class HeldAnki(IAnkiConnecting inner, string target) : IAnkiConnecting
    {
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken token = default)
        {
            var value = await inner.PerformAsync(action, parameters, connection, token);
            if (action == target) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return value;
        }
    }
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
    [Theory]
    [InlineData("audio")]
    [InlineData("overrides")]
    [InlineData("deck")]
    [InlineData("binding")]
    public Task DraftChangeDuringHeldStoreReadPreventsRemoteWrites(string change) => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var backend = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(backend);
            var held = new HeldStore(backend); var server = new SessionTestServer { AfterMetadata = () => held.HoldRead = true };
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), held, entry, sense);
            var send = session.SendAsync();
            try
            {
                await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (change == "audio") session.Plan = session.Plan with { AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression } };
                else if (change == "overrides") session.Plan = session.Plan with { FieldOverrides = new Dictionary<string, string> { ["Meaning"] = "later" } };
                else if (change == "deck") session.Deck = "Other";
                else session.SelectBinding(Guid.NewGuid());
            }
            finally { held.Release.TrySetResult(); await send.WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.False(send.Result);
            var actions = server.Calls.Select(value => value.GetType().GetProperty("action")!.GetValue(value)!.ToString()).ToArray();
            Assert.DoesNotContain("addNote", actions); Assert.DoesNotContain("updateNoteFields", actions); Assert.DoesNotContain("storeMediaFile", actions); Assert.DoesNotContain("createModel", actions);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Theory]
    [InlineData("modelTemplates")]
    [InlineData("modelStyling")]
    public Task MetadataFromOldModelNeverPublishesAfterAwait(string action) => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var settings = Settings(); var held = new HeldAnki(new SessionTestServer(), action);
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(settings, null, held), store, entry, sense);
            string deck = session.Deck; var load = session.LoadMetadataAsync();
            try { await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); settings.Model = "different"; }
            finally { held.Release.TrySetResult(); await load.WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.Null(session.Catalog); Assert.Empty(session.AvailableFields); Assert.False(session.CurrentPresetTemplate); Assert.Equal(deck, session.Deck);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task SaveAcknowledgementDoesNotOverwriteLaterDraftOrImportedImages() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var backend = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(backend);
            var held = new HeldStore(backend) { HoldSaveAck = true }; var image = Image();
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, new SessionTestServer()), held, entry, sense);
            session.ImportedImages = new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = image };
            var save = session.SaveAsync(); var replacement = new VocabularyPreparedImage(image.Reference, [1, 2, 3]);
            try
            {
                await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                session.Plan = session.Plan with { Answer = "later" }; session.Deck = "Other";
                session.ImportedImages = new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = replacement };
            }
            finally { held.Release.TrySetResult(); await save.WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.True(save.Result); Assert.Equal("later", session.Plan.Answer); Assert.Equal("Other", session.Deck);
            Assert.Same(replacement, session.ImportedImages[image.Reference.Filename]);
            Assert.Null((await backend.ReadAsync()).Document.Collections[0].Review!.Plan.Answer);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task AudioAddedDuringTemplateStoreReadPreventsTemplateWrites() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var backend = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(backend);
            var held = new HeldStore(backend); var server = new SessionTestServer(); server.Legacy();
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), held, entry, sense);
            held.HoldRead = true; var update = session.UpdateTemplateAsync();
            try { await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); session.Plan = session.Plan with { AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression } }; }
            finally { held.Release.TrySetResult(); await update.WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.Contains("Speech", session.Message);
            var actions = server.Calls.Select(value => value.GetType().GetProperty("action")!.GetValue(value)!.ToString()).ToArray();
            Assert.DoesNotContain("modelFieldAdd", actions); Assert.DoesNotContain("updateModelTemplates", actions); Assert.DoesNotContain("updateModelStyling", actions); Assert.DoesNotContain("createModel", actions);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task DraftChangedDuringSendSaveAcknowledgementDoesNotStartRemoteWrite() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var backend = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(backend);
            var held = new HeldStore(backend) { HoldSaveAck = true }; var server = new SessionTestServer();
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), held, entry, sense);
            var send = session.SendAsync();
            try { await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); session.Plan = session.Plan with { Answer = "later" }; session.Deck = "Other"; }
            finally { held.Release.TrySetResult(); await send.WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.False(send.Result); Assert.Equal("later", session.Plan.Answer); Assert.Equal("Other", session.Deck);
            Assert.Null((await backend.ReadAsync()).Document.Collections[0].Review!.Plan.Answer);
            var actions = server.Calls.Select(value => value.GetType().GetProperty("action")!.GetValue(value)!.ToString()).ToArray();
            Assert.DoesNotContain("addNote", actions); Assert.DoesNotContain("storeMediaFile", actions); Assert.DoesNotContain("createModel", actions);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task SynchronousDraftChangeDuringPendingSelectionIsNotAcceptedAsSendState() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(path, false); var (entry, sense) = await Seed(store);
            var server = new SessionTestServer(); using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry, sense);
            bool changed = false;
            session.PropertyChanged += (_, _) =>
            {
                if (!changed && session.BindingId is not null) { changed = true; session.Plan = session.Plan with { FieldOverrides = new Dictionary<string, string> { ["Meaning"] = "later" } }; }
            };
            Assert.False(await session.SendAsync()); Assert.True(changed); Assert.Equal(0, server.AddCount);
            Assert.Equal("later", session.Plan.FieldOverrides["Meaning"]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task CancelledHeldImageReadDoesNotBecomeMissingImageError() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-anki-" + Guid.NewGuid().ToString("N")); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var backend = FrozenVocabularyStoreFactory.Create(path, false); var (entryId, senseId) = await Seed(backend);
            var entry = (await backend.ReadAsync()).Document.Entries[0]; var image = Image();
            var edited = entry with { Senses = [entry.Senses[0] with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image.Reference))] }] };
            await backend.UpdateEntryAsync(edited, entry, new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = image });
            var held = new HeldStore(backend) { HoldImage = true }; var server = new SessionTestServer();
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), held, entryId, senseId);
            using var token = new CancellationTokenSource(); var send = session.SendAsync(cancellationToken: token.Token);
            try { await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); token.Cancel(); await send.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { held.Release.TrySetResult(); await send.WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.False(send.Result); Assert.Null(session.Message); Assert.Equal(0, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
