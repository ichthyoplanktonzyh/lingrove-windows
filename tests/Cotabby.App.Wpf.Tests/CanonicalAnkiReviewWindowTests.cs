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
    internal sealed class ThrowingCancellationAnki(IAnkiConnecting inner) : IAnkiConnecting
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? OnCancellation;
        public async Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken token = default)
        {
            if (action != "addNote") return await inner.PerformAsync(action, parameters, connection, token);
            using var registration = token.Register(() => { OnCancellation?.Invoke(); throw new InvalidOperationException("cancel callback failure"); });
            Entered.TrySetResult(); await Release.Task;
            return await inner.PerformAsync(action, parameters, connection, token);
        }
    }
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

    [Fact]
    public Task ThrowingAndReentrantCancellationStillDrainsAndRetiresOnce() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var client = new ThrowingCancellationAnki(new SessionTestServer());
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, client), store, entry.Id, entry.Senses[0].Id);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            int changed = 0, callbacks = 0; session.PropertyChanged += (_, _) => changed++;
            Task? reentered = null; client.OnCancellation = () => { callbacks++; reentered = window.RetireAsync(); };
            Click(window.SendButton); await client.Entered.Task;
            Task retirement = window.RetireAsync(); Assert.Same(retirement, reentered); Assert.Same(retirement, window.RetireAsync());
            Assert.Equal(1, callbacks); Assert.False(retirement.IsCompleted); Assert.True(window.IsVisible);
            int before = changed; session.Plan = session.Plan with { Answer = "still observed" }; Assert.True(changed > before);
            client.Release.SetResult(); var error = await Assert.ThrowsAnyAsync<Exception>(async () => await retirement);
            Assert.Contains("cancel callback failure", error.ToString()); Assert.False(window.IsVisible);
            before = changed; session.Plan = session.Plan with { Answer = "retired" }; Assert.Equal(before, changed);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task IndependentCardPresetsTemplateSaveReopenAndSendUseItsId() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-card-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money", Example: "bank opens")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var server = new SessionTestServer(); var controller = new AnkiReviewController(Settings(), null, server); Guid card = Guid.NewGuid();
            var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry.Id, entry.Senses[0].Id, card);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            for (int i = 0; i < 4; i++) { window.LayoutPreset.SelectedIndex = i; await window.PreviewOperation; Assert.Equal(ReviewLayoutPolicy.Preset(i).Parts.Select(p => p.Source), session.Plan.Layout!.Parts.Select(p => p.Source)); }
            window.LayoutPreset.SelectedIndex = 0; await window.PreviewOperation; window.CardNameBox.Text = "My card"; await window.PreviewOperation;
            window.TemplateNameBox.Text = "My template"; Click(window.SaveTemplateButton); await Ready(window);
            Assert.Single((await store.ReadAsync()).Document.ReviewTemplates!); Assert.Null((await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards);
            var oldPart = session.Plan.Layout!.Front.Single().Id; window.LayoutPreset.SelectedIndex = 4; await window.PreviewOperation;
            Assert.NotEqual(oldPart, session.Plan.Layout!.Front.Single().Id); Assert.Equal(ReviewContentSource.Expression, session.Plan.Layout.Front.Single().Source);
            Click(window.SaveButton); await Ready(window); await window.RetireAsync();
            var saved = (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards!.Single(); Assert.Equal(card, saved.Plan.Id); Assert.Equal("My card", saved.Plan.Name);
            var reopened = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry.Id, entry.Senses[0].Id, card);
            var next = new CanonicalAnkiReviewWindow(reopened, action => action(CancellationToken.None), () => { }); next.Show(); await Ready(next); Assert.Equal("My card", next.CardNameBox.Text);
            Assert.True(next.SendButton.IsEnabled); Click(next.SendButton); await Ready(next); await next.RetireAsync();
            var receipt = (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards!.Single(); Assert.Equal(card, receipt.Plan.Id); Assert.Equal(card, receipt.ActiveBinding!.OriginSenseId); Assert.Equal(1, server.AddCount);
            Assert.Null((await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].Review);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task CardImageAndAudioSlotRemainExplicitDrafts() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-card-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var server = new SessionTestServer(); Guid card = Guid.NewGuid();
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry.Id, entry.Senses[0].Id, card);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            window.AddPartChoice.SelectedValue = ReviewContentSource.ExpressionAudio; Click(window.AddPartButton); await window.PreviewOperation; Assert.False(window.SendButton.IsEnabled); Assert.Contains(ReviewContentSource.ExpressionAudio, session.Plan.Layout!.Front.Select(p => p.Source));
            var audioPanel = window.LayoutParts.Children.OfType<Border>().Last(); var panel = (StackPanel)((Grid)audioPanel.Child).Children[1]; var header = (WrapPanel)panel.Children[0]; Click(header.Children.OfType<Button>().Last()); await window.PreviewOperation;
            window.AddPartChoice.SelectedValue = ReviewContentSource.Custom; Click(window.AddPartButton); await window.PreviewOperation; var custom = session.Plan.Layout!.Front.Last();
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            byte[] bytes = Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!); Directory.CreateDirectory(root); string path = Path.Combine(root, "input.png"); await File.WriteAllBytesAsync(path, bytes);
            window.ImportLayoutImage(custom.Id, path); await Ready(window); await window.PreviewOperation;
            var reference = ((VocabularyBlockContent.Image)session.Plan.Layout!.Front.Last().Content.Single().Content).Reference; byte[] prepared = session.ImportedImages[reference.Filename].CopyBytes(); Assert.Contains(window.PreviewPanel.Children.OfType<Image>(), _ => true);
            Click(window.SaveButton); await Ready(window); await window.RetireAsync();
            var saved = (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards!.Single(); Assert.Equal(card, saved.Plan.Id); Assert.Equal(prepared, await store.ReadImageAsync(reference)); Assert.Equal(0, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task CardCustomBlockFormattingAndPartOrderPersist() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-card-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var card = Guid.NewGuid();
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, new SessionTestServer()), store, entry.Id, entry.Senses[0].Id, card);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            window.AddPartChoice.SelectedValue = ReviewContentSource.Custom; Click(window.AddPartButton); await window.PreviewOperation;
            var cardPanel = (StackPanel)((Grid)((Border)window.LayoutParts.Children[1]).Child).Children[1]; var add = cardPanel.Children.OfType<WrapPanel>().Last(); Click(add.Children.OfType<Button>().First()); await window.PreviewOperation;
            cardPanel = (StackPanel)((Grid)((Border)window.LayoutParts.Children[1]).Child).Children[1]; var style = cardPanel.Children.OfType<ComboBox>().Single(); style.SelectedItem = VocabularyTextStyle.Heading;
            var text = cardPanel.Children.OfType<TextBox>().Single(); text.Text = "remember this"; await window.PreviewOperation;
            var header = cardPanel.Children.OfType<WrapPanel>().First(); Click(header.Children.OfType<Button>().First()); await window.PreviewOperation;
            var expected = session.Plan.Layout!.Front[0]; Assert.Equal(ReviewContentSource.Custom, expected.Source); Assert.Equal("remember this", ((VocabularyBlockContent.Text)expected.Content.Single().Content).Value.Text);
            Click(window.SaveButton); await Ready(window); await window.RetireAsync();
            var persisted = (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards!.Single().Plan.Layout!.Front[0]; Assert.Equal(expected, persisted);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task ImagePresetBindsAndAllowsSelectingStableSenseBlock() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-card-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var snapshot = await store.ReadAsync(); var entry = snapshot.Document.Entries[0];
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            byte[] bytes = Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!); var image = new VocabularyPreparedImage(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes);
            var first = VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image.Reference, "first")); var second = VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image.Reference, "second"));
            await store.UpdateEntryAsync(entry with { Senses = [entry.Senses[0] with { Content = [first, second] }] }, entry, new Dictionary<string, VocabularyPreparedImage> { [image.Reference.Filename] = image });
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, new SessionTestServer()), store, entry.Id, entry.Senses[0].Id, Guid.NewGuid());
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            window.LayoutPreset.SelectedIndex = 2; await window.PreviewOperation; Assert.Equal(first.Id, session.Plan.Layout!.Front.Single().BlockId);
            var panel = (StackPanel)((Grid)((Border)window.LayoutParts.Children[0]).Child).Children[1]; var source = panel.Children.OfType<ComboBox>().Single(); source.SelectedIndex = 2; await window.PreviewOperation;
            Assert.Equal(second.Id, session.Plan.Layout!.Front.Single().BlockId); Click(window.SaveButton); await Ready(window); await window.RetireAsync();
            Assert.Equal(second.Id, (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards!.Single().Plan.Layout!.Front.Single().BlockId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task CardFrontBackAndAudioErrorRender() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-render-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money", Example: "bank opens")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, new SessionTestServer()), store, entry.Id, entry.Senses[0].Id, Guid.NewGuid());
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            VocabularyThemeRender.Capture(window, "canonical-card-front", 720, 1400);
            window.PreviewPanel.BringIntoView(); await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            VocabularyThemeRender.Capture(window, "canonical-card-front-preview", 720, 760);
            window.SideBox.SelectedIndex = 1; await window.PreviewOperation; window.PreviewPanel.BringIntoView(); await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); VocabularyThemeRender.Capture(window, "canonical-card-back-preview", 720, 760);
            window.AddPartChoice.SelectedValue = ReviewContentSource.ExpressionAudio; Click(window.AddPartButton); await window.PreviewOperation;
            Assert.Contains("Speech", window.ValidationText.Text); Assert.False(window.SendButton.IsEnabled);
            VocabularyThemeRender.Capture(window, "canonical-card-audio-error", 720, 1400); await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

}
