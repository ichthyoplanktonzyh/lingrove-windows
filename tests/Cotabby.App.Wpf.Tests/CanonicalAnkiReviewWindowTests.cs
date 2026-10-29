using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Cotabby.Core.Vocabulary;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.UI.Review;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalAnkiReviewWindowTests(WpfDispatcher dispatcher)
{
    private static AnkiReviewSettings Settings() { var values = new AppSettings(); return new(() => values, () => { }, new AnkiTestCredentials()); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Ready(CanonicalAnkiReviewWindow window) { if (window.LastOperation is { } op) await op; await window.PreviewOperation; }
    [Fact]
    public Task DraftSaveAndAudioRemovalUseAcknowledgedCanonicalStore() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money", Example: "bank opens")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var server = new SessionTestServer();
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry.Id, entry.Senses[0].Id);
            session.Plan = session.Plan with { AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression } };
            int admitted = 0; var window = new CanonicalAnkiReviewWindow(session, action => { admitted++; return action(CancellationToken.None); }, () => { });
            window.Show(); await Ready(window);
            Assert.Contains("Speech", window.StateText.Text + await session.ValidationMessageAsync()); Assert.False(window.SendButton.IsEnabled);
            Click(window.ClearAudioButton); await window.PreviewOperation; Assert.True(window.SendButton.IsEnabled);
            window.AnswerBox.Text = "financial institution"; await window.PreviewOperation;
            Click(window.SaveButton); await Ready(window);
            Assert.Equal("financial institution", (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].Review!.Plan.Answer);
            Assert.True(admitted >= 3); Assert.DoesNotContain(server.Calls, c => c.GetType().GetProperty("action")!.GetValue(c)!.ToString() == "addNote");
            await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task ClosingDrainsAcceptedSendAfterRemoteWrite() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new SessionTestServer { AfterAdd = async () => { entered.TrySetResult(); await release.Task; } };
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry.Id, entry.Senses[0].Id);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            Assert.True(window.SendButton.IsEnabled); Click(window.SendButton); await entered.Task;
            Task closing = window.RetireAsync(); Assert.False(closing.IsCompleted); release.SetResult(); await closing;
            Assert.False(window.IsVisible); Assert.Equal(1, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task ConflictRequiresChoiceAndMissingNoteRequiresExplicitRecreate() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var server = new SessionTestServer(); var controller = new AnkiReviewController(Settings(), null, server);
            var initial = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry.Id, entry.Senses[0].Id); Assert.True(await initial.SendAsync()); initial.Dispose();
            long note = (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].Review!.ActiveBinding!.NoteId!.Value;
            server.Edit(note, "Meaning", "remote meaning");
            var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry.Id, entry.Senses[0].Id);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            Click(window.SendButton); await Ready(window); Assert.NotEmpty(session.Conflicts); Assert.False(window.SendButton.IsEnabled);
            var choice = window.ConflictsPanel.Children.OfType<ComboBox>().Single(); choice.SelectedIndex = 2; Assert.True(window.SendButton.IsEnabled);
            Click(window.SendButton); await Ready(window); await window.RetireAsync();
            Assert.Equal("remote meaning", (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].Review!.Plan.FieldOverrides["Meaning"]);
            server.Delete(note); var next = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry.Id, entry.Senses[0].Id);
            var nextWindow = new CanonicalAnkiReviewWindow(next, action => action(CancellationToken.None), () => { }); nextWindow.Show(); await Ready(nextWindow);
            Click(nextWindow.SendButton); await Ready(nextWindow); Assert.True(next.MissingNote); Assert.Equal("Re-add to Anki", nextWindow.SendButton.Content); Assert.Equal(1, server.AddCount);
            Click(nextWindow.SendButton); await Ready(nextWindow); await nextWindow.RetireAsync(); Assert.Equal(2, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task SettingsRunsOnlyAfterSaveAndWindowRetires() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var server = new SessionTestServer();
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry.Id, entry.Senses[0].Id);
            bool configured = false; CanonicalAnkiReviewWindow? window = null;
            window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { Assert.False(window!.IsVisible); configured = true; });
            window.Show(); await Ready(window); window.AnswerBox.Text = "saved before settings"; await window.PreviewOperation;
            Click(window.SettingsButton); await Ready(window); await window.RetireAsync();
            Assert.True(configured); Assert.Equal("saved before settings", (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].Review!.Plan.Answer);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task StoredImagePreviewAndCancelDoNotCommitDraft() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var snapshot = await store.ReadAsync(); var entry = snapshot.Document.Entries[0];
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            byte[] bytes = Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!);
            var image = new VocabularyPreparedImage(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes);
            var updated = entry with { Senses = [entry.Senses[0] with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image.Reference, "caption"))] }] };
            await store.UpdateEntryAsync(updated, entry, new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = image });
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, new SessionTestServer()), store, entry.Id, entry.Senses[0].Id);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            window.SideBox.SelectedIndex = 1; await window.PreviewOperation;
            Assert.Single(window.PreviewPanel.Children.OfType<Image>()); Assert.Contains(window.PreviewPanel.Children.OfType<TextBlock>(), text => text.Text.Contains("caption"));
            window.AnswerBox.Text = "uncommitted"; await window.PreviewOperation; await window.RetireAsync();
            Assert.Null((await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].Review);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task ChangedDestinationRequiresExplicitReload() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var settings = Settings(); var server = new SessionTestServer();
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(settings, null, server), store, entry.Id, entry.Senses[0].Id);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            Assert.True(window.SendButton.IsEnabled); settings.Address = "http://127.0.0.1:8766"; Assert.False(window.SendButton.IsEnabled);
            Assert.Equal(0, server.AddCount); await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

}
