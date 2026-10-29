using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyEntryEditorWindowTests(WpfDispatcher dispatcher)
{
    private sealed class HeldEditor : ICanonicalVocabularyEditorStore
    {
        internal readonly TaskCompletionSource<FrozenVocabularyImportSnapshot> Receipt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<FrozenVocabularyImportSnapshot> UpdateEntryAsync(Cotabby.Core.Vocabulary.Frozen.VocabularyEntry edited, Cotabby.Core.Vocabulary.Frozen.VocabularyEntry baseline, IReadOnlyDictionary<string, VocabularyPreparedImage>? images = null, CancellationToken cancellationToken = default) => Receipt.Task;
    }
    private sealed class HeldMedia : ICanonicalVocabularyMediaStore
    {
        internal readonly TaskCompletionSource<VocabularyPreparedImage> Preparation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<VocabularyPreparedImage> PrepareImageFileAsync(string path, CancellationToken cancellationToken = default) => Preparation.Task;
        public Task<VocabularyPreparedImage> PrepareImageAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadImageAsync(VocabularyImageReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CanonicalVocabularyMediaSnapshot> ReadMediaSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    [Fact]
    public Task ClosedSheetJoinsLateImagePreparationWithoutPublishingDraft() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(123));
        await store.AddEntriesAsync([new(Expression: "hello")]); var entry = (await store.ReadAsync()).Document.Entries.Single();
        var held = new HeldMedia(); var window = new FrozenVocabularyEntryEditorWindow(store, held, entry, entry.Senses.Single().Id); window.Show(); window.ImportImage("ignored"); window.Close();
        var retirement = window.RetireAsync(); Assert.False(retirement.IsCompleted);
        held.Preparation.SetResult(new VocabularyPreparedImage(new("ignored", 1, 1, 1), [1])); await retirement;
        Assert.Null(window.Draft.Senses.Single().Content); Assert.Null((await store.ReadAsync()).Document.Entries.Single().Senses.Single().Content);
    });
    [Fact]
    public Task NotebookEditUsesSelectedSenseAndRefreshesAfterAcknowledgement() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(123));
        var result = await store.AddEntriesAsync([new(Expression: "hello", Meaning: "greeting")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!;
        await window.Session.RevealSelectionAsync(new(result.Selection!.Value.EntryId, result.Selection.Value.SenseId)); window.UpdateLayout();
        _ = window.Dispatcher.BeginInvoke(() => { var sheet = Application.Current.Windows.OfType<FrozenVocabularyEntryEditorWindow>().Single(); FindByLabel<TextBox>(sheet, "Core meaning").Text = "welcome"; Find<Button>(sheet, "canonicalEditSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        Find<Button>(window, "frozenNotebookEdit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
        Assert.Equal("welcome", window.Session.Entries.Single().Senses.Single().Meaning); Assert.Equal(result.Selection.Value.SenseId, window.Session.Selection!.Value.SenseId); window.Close();
    });
    [Fact]
    public Task AcceptedSavePreventsDismissalAndRetirementJoinsReceipt() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(123));
        await store.AddEntriesAsync([new(Expression: "hello")]); var snapshot = await store.ReadAsync(); var entry = snapshot.Document.Entries.Single();
        var held = new HeldEditor(); var window = new FrozenVocabularyEntryEditorWindow(held, store, entry, entry.Senses.Single().Id); window.Show();
        Find<Button>(window, "canonicalEditSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.Close(); Assert.True(window.IsVisible);
        var retirement = window.RetireAsync(); Assert.False(retirement.IsCompleted); held.Receipt.SetResult(snapshot); await retirement;
        Assert.False(window.IsVisible); Assert.True(window.Saved);
    });
    [Fact]
    public Task RealNamedStoreImportsAndAcknowledgesImage() => dispatcher.RunAsync(async () =>
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "canonical-ui-image-" + Guid.NewGuid().ToString("N"));
        string file = System.IO.Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        string input = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "canonical-ui-image-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            System.IO.File.WriteAllBytes(input, Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!));
            await using var store = FrozenVocabularyStoreFactory.Create(file, false); await store.AddEntriesAsync([new(Expression: "image")]);
            var entry = (await store.ReadAsync()).Document.Entries.Single(); var window = new FrozenVocabularyEntryEditorWindow(store, store, entry, entry.Senses.Single().Id); window.Show(); window.ImportImage(input);
            await Until(() => window.Draft.Senses.Single().Content?.Count == 1 && Find<Button>(window, "canonicalEditSave").IsEnabled);
            FindByLabel<TextBox>(window, "Caption").Text = "A caption"; await Until(() => Descendants(window).OfType<Image>().Any(i => i.Source is not null)); VocabularyThemeRender.Capture(window, "canonical-edit-image", 680, 660);
            Find<Button>(window, "canonicalEditSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; await window.RetireAsync();
            var image = Assert.IsType<VocabularyBlockContent.Image>((await store.ReadAsync()).Document.Entries.Single().Senses.Single().Content!.Single().Content);
            Assert.Equal("A caption", image.Caption); Assert.NotEmpty(await store.ReadImageAsync(image.Reference));
        }
        finally { if (System.IO.File.Exists(input)) System.IO.File.Delete(input); if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    });
    [Fact]
    public Task CancelAndAcknowledgedSaveRetainIdentityAndConcurrentEvidence() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(123));
        await store.AddEntriesAsync([new(Expression: "hello", Meaning: "greeting", Example: "hello there")]);
        var baseline = (await store.ReadAsync()).Document.Entries.Single(); var sense = baseline.Senses.Single(); var example = sense.Examples.Single();
        var cancel = new FrozenVocabularyEntryEditorWindow(store, store, baseline, sense.Id); cancel.Show(); Find<TextBox>(cancel, "canonicalEditExpression").Text = "abandoned"; cancel.Close(); await cancel.RetireAsync(); Assert.Equal("hello", (await store.ReadAsync()).Document.Entries.Single().Expression);
        var window = new FrozenVocabularyEntryEditorWindow(store, store, baseline, sense.Id); window.Show();
        FindByLabel<TextBox>(window, "Core meaning").Text = "welcome";
        FindByLabel<TextBox>(window, "Original text").Text = "hello friend";
        await Until(() => Find<Button>(window, "canonicalEditSave").IsEnabled);
        VocabularyThemeRender.Capture(window, "canonical-edit", 680, 660);
        window.Width = 540; window.Height = 500; window.UpdateLayout(); VocabularyThemeRender.Capture(window, "canonical-edit-narrow", 540, 500);
        window.Width = 680; window.Height = 660; window.UpdateLayout();
        await store.AddEntriesAsync([new(Expression: "hello", Meaning: "greeting", Example: "hello again")]);
        Find<Button>(window, "canonicalEditSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; await window.RetireAsync();
        var saved = (await store.ReadAsync()).Document.Entries.Single(); Assert.True(window.Saved); Assert.Equal(baseline.Id, saved.Id); Assert.Equal(sense.Id, saved.Senses.Single().Id); Assert.Equal("welcome", saved.Senses.Single().Meaning);
        Assert.Equal(2, saved.Senses.Single().Examples.Count); var changed = saved.Senses.Single().Examples.Single(e => e.Id == example.Id); Assert.Equal("hello friend", changed.Text); Assert.NotEmpty(changed.Ranges); Assert.NotEqual(example.SourceId, changed.SourceId); Assert.Equal(baseline.CreatedAt, saved.CreatedAt);
    });
    [Fact]
    public Task ConflictKeepsDraftAndAllowsCancel() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(123));
        await store.AddEntriesAsync([new(Expression: "hello", Meaning: "greeting")]); var baseline = (await store.ReadAsync()).Document.Entries.Single(); var sense = baseline.Senses.Single();
        var window = new FrozenVocabularyEntryEditorWindow(store, store, baseline, sense.Id); window.Show(); FindByLabel<TextBox>(window, "Core meaning").Text = "mine";
        await store.UpdateEntryAsync(baseline with { Senses = [sense with { Meaning = "theirs" }] }, baseline);
        Find<Button>(window, "canonicalEditSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
        Assert.False(window.Saved); Assert.True(window.IsVisible); Assert.Equal("mine", window.Draft.Senses.Single().Meaning); Assert.Contains(Descendants(window).OfType<TextBlock>(), t => t.Text.Length > 0 && t.Text.Contains("changed while", StringComparison.OrdinalIgnoreCase));
        VocabularyThemeRender.Capture(window, "canonical-edit-error", 680, 660); window.Close(); await window.RetireAsync(); Assert.Equal("theirs", (await store.ReadAsync()).Document.Entries.Single().Senses.Single().Meaning);
    });
    private sealed class ClipboardFixture(byte[]? bytes) : IVocabularyClipboardImageTransfer
    {
        internal VocabularyClipboardImageOffer Offer = new(41, true);
        internal uint ReadSequence;
        internal TaskCompletionSource<byte[]?>? Held;
        public VocabularyClipboardImageOffer Capture() => Offer;
        public Task<byte[]?> ReadAsync(uint expectedSequence, CancellationToken token = default) { ReadSequence = expectedSequence; return Held?.Task ?? Task.FromResult(bytes); }
    }
    [Fact]
    public Task ExplicitPasteUsesCapturedSequenceAndPersistsMedia() => dispatcher.RunAsync(async () =>
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "canonical-paste-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            var clipboard = new ClipboardFixture(Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!));
            await using var store = FrozenVocabularyStoreFactory.Create(System.IO.Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new(Expression: "image")]); var entry = (await store.ReadAsync()).Document.Entries.Single();
            var window = new FrozenVocabularyEntryEditorWindow(store, store, entry, entry.Senses.Single().Id, clipboard: clipboard); window.Show();
            Assert.True(window.PasteImage()); clipboard.Offer = new(42, true);
            await Until(() => window.Draft.Senses.Single().Content?.Count == 1 && Find<Button>(window, "canonicalEditSave").IsEnabled);
            Assert.Equal(41u, clipboard.ReadSequence); await Until(() => Descendants(window).OfType<CanonicalVocabularyImageView>().Any(v => v.Bitmap is not null));
            VocabularyThemeRender.Capture(window, "canonical-paste-image", 680, 660);
            Find<Button>(window, "canonicalEditSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; await window.RetireAsync();
            var image = Assert.IsType<VocabularyBlockContent.Image>((await store.ReadAsync()).Document.Entries.Single().Senses.Single().Content!.Single().Content);
            Assert.NotEmpty(await store.ReadImageAsync(image.Reference));
            var notebook = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); notebook.Show(); await notebook.LastOperation!;
            await notebook.Session.RevealSelectionAsync(new(entry.Id, entry.Senses.Single().Id));
            await Until(() => Descendants(notebook).OfType<CanonicalVocabularyImageView>().Any(v => v.Bitmap is not null));
            VocabularyThemeRender.Capture(notebook, "canonical-detail-image", 1100, 720);
            var view = Descendants(notebook).OfType<CanonicalVocabularyImageView>().Single();
            _ = notebook.Dispatcher.BeginInvoke(() => { var preview = Application.Current.Windows.OfType<Window>().Single(x => x.Title == "Image"); Assert.Same(notebook, preview.Owner); preview.Close(); });
            FindByLabel<Button>(view, "Enlarge image").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await notebook.RetireAsync();
        }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    });
    [Fact]
    public Task ExplanationPasteCommandConsumesImageBeforeTextbox() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(123));
        await store.AddEntriesAsync([new(Expression: "hello")]); var entry = (await store.ReadAsync()).Document.Entries.Single();
        var clipboard = new ClipboardFixture(null); var window = new FrozenVocabularyEntryEditorWindow(store, store, entry, entry.Senses.Single().Id, clipboard: clipboard); window.Show();
        var add = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "Add text")); add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var text = FindByLabel<TextBox>(window, "Explanation text"); int fallback = 0;
        CommandManager.AddPreviewExecutedHandler(text, (_, args) => { if (args.Command == ApplicationCommands.Paste) { fallback++; args.Handled = true; } });
        Assert.True(ApplicationCommands.Paste.CanExecute(null, text)); ApplicationCommands.Paste.Execute(null, text);
        await Until(() => clipboard.ReadSequence == 41); Assert.Equal(0, fallback); Assert.Single(window.Draft.Senses.Single().Content!);
        window.Close(); await window.RetireAsync();
    });
    [Fact]
    public Task InvalidPasteKeepsDraftAndShowsFixedError() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(123));
        await store.AddEntriesAsync([new(Expression: "hello")]); var entry = (await store.ReadAsync()).Document.Entries.Single();
        var clipboard = new ClipboardFixture([1, 2, 3]); var window = new FrozenVocabularyEntryEditorWindow(store, store, entry, entry.Senses.Single().Id, clipboard: clipboard); window.Show();
        Assert.True(window.PasteImage()); await Until(() => Descendants(window).OfType<TextBlock>().Any(t => t.Text == "Unable to add this image. Try a smaller image."));
        Assert.Null(window.Draft.Senses.Single().Content); Assert.True(Find<Button>(window, "canonicalEditSave").IsEnabled); window.Close(); await window.RetireAsync();
    });
    [Fact]
    public Task CancelJoinsLateClipboardWithoutPublishing() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(123));
        await store.AddEntriesAsync([new(Expression: "hello")]); var entry = (await store.ReadAsync()).Document.Entries.Single();
        var clipboard = new ClipboardFixture(null) { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var window = new FrozenVocabularyEntryEditorWindow(store, store, entry, entry.Senses.Single().Id, clipboard: clipboard); window.Show();
        Assert.True(window.PasteImage()); await Until(() => clipboard.ReadSequence == 41); window.Close(); var retirement = window.RetireAsync(); Assert.False(retirement.IsCompleted);
        clipboard.Held.SetResult([1, 2, 3]); await retirement; Assert.Null(window.Draft.Senses.Single().Content); Assert.Null((await store.ReadAsync()).Document.Entries.Single().Senses.Single().Content);
    });
    private static async Task Until(Func<bool> predicate) { for (int i = 0; i < 100 && !predicate(); i++) await Task.Delay(10); Assert.True(predicate()); }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants(root).OfType<T>().Single(x => x.Name == name);
    private static T FindByLabel<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants(root).OfType<T>().Single(x => AutomationProperties.GetName(x) == name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
