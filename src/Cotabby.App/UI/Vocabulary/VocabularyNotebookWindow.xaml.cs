using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// Activating, reusable notebook browser adapted from Mac's list/details view. It borrows the
/// app Store, owns only filters/expanded rows/selected sense, and renders acknowledged literal
/// values. Filtering runs away from WPF with latest-wins cancellation; visual rows virtualize.
/// Explicit show-this-sense resets filters, ordinary reopen preserves browsing. No model starts.
/// </summary>
public partial class VocabularyNotebookWindow : Window
{
    private readonly VocabularyStore _store;
    private readonly IVocabularyClipboardImageTransfer? _clipboard;
    private readonly IVocabularyTextSearching _textSearch;
    private readonly HashSet<Guid> _expanded = [];
    private readonly HashSet<Task> _work = [];
    private CancellationTokenSource? _filterWork;
    private VocabularySelection? _requested;
    private Guid? _selection;
    private bool _newestFirst = true, _presenting, _retired, _closing;
    private VocabularyEntryEditorWindow? _editor;
    private long _version;
    private string _selectedTag = "";
    private sealed record BrowseRequest(VocabularyDocument Document, string Query, LearningLanguage? Language, string Tag,
        bool Newest, bool Loaded, string? Error, ulong Revision, long Version, CancellationTokenSource Work);
    private sealed record BrowseResult(IReadOnlyList<VocabularyEntry> Entries, IReadOnlyList<string> Tags, string Tag);
    private sealed record DetailKey(Guid Entry, string Expression, string Form, LearningLanguage Language, VocabularyEntryKind Kind, VocabularySense Sense);
    private DetailKey? _renderedDetail;
    private bool _hasRenderedDetail;
    private readonly List<VocabularyImageView> _images = [];
    internal Task LastRefresh { get; private set; } = Task.CompletedTask;
    internal VocabularySelection? Selection => VocabularyNotebookPolicy.ResolveSelection(_store.Snapshot.Document, _selection);
    private sealed record LanguageOption(LearningLanguage? Value, string Label);
    private sealed record TagOption(string Value, string Label);

    internal VocabularyNotebookWindow(VocabularyStore store, IVocabularyTextSearching textSearch,
        VocabularyNotebookSession? learning = null, Action? configureExplanation = null, IVocabularyClipboardImageTransfer? clipboard = null)
    {
        _store = store; _textSearch = textSearch; _learning = learning; _configureExplanation = configureExplanation; _clipboard = clipboard;
        InitializeComponent(); WindowTheme.Follow(this);
        _presenting = true;
        LanguageFilter.ItemsSource = new[] { new LanguageOption(null, "All languages") }
            .Concat(LearningLanguages.All.Select(language => new LanguageOption(language, language.Label()))).ToArray();
        LanguageFilter.SelectedIndex = 0; _presenting = false;
        _store.Changed += OnStoreChanged;
        Closing += OnClosing;
        Closed += (_, _) => { _retired = true; _store.Changed -= OnStoreChanged; CancelFiltering(); RetireActions(); RetireLearning(); RetireExport(); RetireBackupPresentation(); ClearImages(); };
        IsVisibleChanged += (_, _) => { if (IsInitialized) { if (!IsVisible) { RetireExport(); RetireBackupPresentation(); } UpdateExportAdmission(); UpdateBackupAdmission(); } };
        RequestRefresh();
    }
    internal void Reveal(VocabularySelection? selection)
    {
        Dispatcher.VerifyAccess();
        if (_retired) return;
        if (selection is { } value)
        {
            ClearLearningRows(); _hasRenderedDetail = false;
            _presenting = true; SearchText.Text = ""; LanguageFilter.SelectedIndex = 0; _selectedTag = ""; _newestFirst = true;
            _presenting = false; _requested = value; _expanded.Add(value.EntryId);
            // Explicit navigation cannot leave an old generation alive while list filtering
            // runs. Resolve the acknowledged detail synchronously; filtering still runs away
            // from WPF and a cold Store keeps the request for its eventual loaded snapshot.
            var document = _store.Snapshot.Document;
            var entry = document.Entries.FirstOrDefault(candidate => candidate.Id == value.EntryId);
            _selection = entry is null ? null : VocabularySensePolicy.Index(value.SenseId, entry) is { } index
                ? entry.Senses[index].Id : entry.Senses.FirstOrDefault()?.Id;
            RenderDetail(document);
        }
        else RenderDetail(_store.Snapshot.Document);
        RequestRefresh();
        foreach (var row in _explanationRows.Values) row.Refresh();
        _dictionarySection?.ResumeExpanded();
        _ = _learning?.Resume();
    }
    private void OnStoreChanged() { if (!_retired) RequestRefresh(); }
    private void OnFiltersChanged(object sender, RoutedEventArgs args)
    {
        if (_presenting || !IsInitialized || _retired) return;
        if (ReferenceEquals(sender, TagFilter) && TagFilter.SelectedItem is TagOption tag) _selectedTag = tag.Value;
        RequestRefresh();
    }
    private void OnSort(object sender, RoutedEventArgs args) { _newestFirst = !_newestFirst; RequestRefresh(); }
    private void RequestRefresh()
    {
        Dispatcher.VerifyAccess(); if (_retired) return;
        UpdateBackupAdmission();
        CancelFiltering(); var source = new CancellationTokenSource(); _filterWork = source; long version = ++_version;
        InvalidateExportOffer();
        var state = _store.ReadState(); var document = state.Snapshot.Document; string query = SearchText.Text;
        var language = (LanguageFilter.SelectedItem as LanguageOption)?.Value;
        var request = new BrowseRequest(document, query, language, _selectedTag, _newestFirst, state.IsLoaded, state.ErrorMessage, state.Snapshot.Revision, version, source);
        var work = RefreshAsync(request); LastRefresh = work;
        Track(work);
    }
    private void Track(Task work)
    {
        lock (_work) _work.Add(work);
        _ = work.ContinueWith(completed => { lock (_work) _work.Remove(completed); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private async Task RefreshAsync(BrowseRequest request)
    {
        var token = request.Work.Token;
        try
        {
            var result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); var tags = VocabularyNotebookPolicy.Tags(request.Document);
                string tag = VocabularyNotebookPolicy.ResolveTag(tags, request.Tag);
                return new BrowseResult(VocabularyNotebookPolicy.VisibleEntries(request.Document, _textSearch, request.Query,
                    request.Language, tag, request.Newest, token), tags, tag);
            }, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(() => ApplyBrowse(request, result), System.Windows.Threading.DispatcherPriority.DataBind, token).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
        catch (Exception)
        {
            if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!_retired && request.Version == _version)
                    { StorageError.Text = "The vocabulary view could not be updated."; StorageError.Visibility = Visibility.Visible; }
                }, System.Windows.Threading.DispatcherPriority.DataBind, token).Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
        }
        finally { request.Work.Dispose(); }
    }
    private void ApplyBrowse(BrowseRequest request, BrowseResult result)
    {
        if (_retired || request.Work.IsCancellationRequested || request.Version != _version) return;
        var document = request.Document; var visible = result.Entries; _selectedTag = result.Tag;
        if (_requested is { } requested)
        {
            if (document.Entries.FirstOrDefault(entry => entry.Id == requested.EntryId) is { } target)
                _selection = VocabularySensePolicy.Index(requested.SenseId, target) is { } index ? target.Senses[index].Id : target.Senses.FirstOrDefault()?.Id;
            if (request.Loaded) _requested = null;
        }
        // Resolve retired aliases after merge. A list filter does not discard global selection;
        // Mac's detail remains available while its row is hidden by the current query.
        _selection = VocabularyNotebookPolicy.ResolveSelection(document, _selection)?.SenseId;
        if (request.Query.Length > 0) _expanded.UnionWith(visible.Select(entry => entry.Id));
        _presenting = true;
        try
        {
            TagFilter.ItemsSource = new[] { new TagOption("", "All tags") }.Concat(result.Tags.Select(value => new TagOption(value, value))).ToArray();
            TagFilter.SelectedItem = TagFilter.Items.Cast<TagOption>().First(value => value.Value == result.Tag);
            TagFilter.Visibility = result.Tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            SortButton.Content = request.Newest ? "Newest first" : "Oldest first";
            EntriesTree.ItemsSource = visible.Select(entry => new VocabularyEntryRow(entry, _expanded.Contains(entry.Id), _selection,
                expanded => { if (expanded) _expanded.Add(entry.Id); else _expanded.Remove(entry.Id); }, _selectingSenses, _batchSelection,
                (selection, selected) => SetBatch(selection, selected, request.Version), request.Version)).ToArray();
            EmptyText.Text = !request.Loaded ? "Loading vocabulary…" : request.Query.Length > 0 ? "No matching expressions" : "No saved expressions";
            EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StorageError.Text = request.Error ?? ""; StorageError.Visibility = StorageError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _presenting = false; }
        _exportOffer = new(request, visible); UpdateExportAdmission();
        RenderDetail(document);
    }
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (_presenting || _retired || args.NewValue is not VocabularySenseRow row) return;
        if (_selection != row.Selection.SenseId) DetailViewport.ScrollToTop();
        _selection = row.Selection.SenseId; RenderDetail(_store.Snapshot.Document);
    }
    private void RenderDetail(VocabularyDocument document)
    {
        var selection = VocabularyNotebookPolicy.ResolveSelection(document, _selection);
        var entry = selection is null ? null : document.Entries.Single(value => value.Id == selection.Value.EntryId);
        var sense = entry?.Senses.Single(value => value.Id == selection!.Value.SenseId);
        var key = entry is null || sense is null ? null : new DetailKey(entry.Id, entry.Expression, entry.DictionaryForm, entry.Language, entry.Kind, sense);
        // Unrelated saves/filter changes keep the same literal controls, scroll and selection.
        if (_hasRenderedDetail && key == _renderedDetail) { UpdateActions(); return; }
        SelectLearning(entry, sense);
        UpdateActions();
        CloseActionMenus(); ++_detailVersion;
        _hasRenderedDetail = true; _renderedDetail = key; ClearImages(); DetailContent.Children.Clear();
        if (entry is null || sense is null) { DetailContent.Children.Add(Label("Vocabulary", 22, bold: true)); return; }
        DetailContent.Children.Add(Literal(entry.Expression, 22, entry.Language, bold: true));
        DetailContent.Children.Add(Label(entry.Language.Label() + " · " + entry.Kind, 12));
        if (entry.DictionaryForm != entry.Expression) DetailContent.Children.Add(Literal(entry.DictionaryForm, 14, entry.Language));
        if (sense.IsSavedWithoutContext && ExplanationRow(entry, sense, null) is { } general) DetailContent.Children.Add(general);
        else if (sense.Meaning.Length > 0) Section(sense.IsSavedWithoutContext ? "Meaning" : "Core meaning", Literal(sense.Meaning, 14, sense.MeaningLanguage));
        if (sense.Content is { Count: > 0 })
        {
            var content = new StackPanel();
            foreach (var block in sense.Content)
            {
                if (block.Content is VocabularyBlockContent.Text text)
                    content.Children.Add(Literal((text.Value.Style == VocabularyTextStyle.Bullet ? "• " : "") + text.Value.Text,
                        text.Value.Style == VocabularyTextStyle.Heading ? 17 : 14, bold: text.Value.Bold || text.Value.Style == VocabularyTextStyle.Heading, italic: text.Value.Italic));
                else if (block.Content is VocabularyBlockContent.Image image)
                {
                    var view = new VocabularyImageView(_store, image.Reference, caption: image.Caption); _images.Add(view); content.Children.Add(view);
                    if (image.Caption.Length > 0) content.Children.Add(Literal(image.Caption, 13));
                }
            }
            Section("Sense explanation", content);
        }
        foreach (var meaning in sense.StandaloneMeanings.Where(value => value.Text.Length > 0 && value.Language != sense.MeaningLanguage))
            Section(meaning.Language?.Label() ?? "Meaning", Literal(meaning.Text, 14, meaning.Language));
        if (sense.AlternateMeanings.Count > 0)
        {
            var alternatives = new StackPanel(); foreach (var meaning in sense.AlternateMeanings) alternatives.Children.Add(Literal(meaning.Text, 13, meaning.Language));
            DetailContent.Children.Add(new Expander { Header = "Other wordings", Content = alternatives, Margin = new Thickness(0, 12, 0, 0) });
        }
        if (sense.Tags.Count > 0) DetailContent.Children.Add(Label(string.Join(" · ", sense.Tags), 12));
        if (sense.Note.Length > 0) Section("Notes", Literal(sense.Note, 14));
        foreach (var example in sense.Examples)
        {
            var content = new StackPanel(); var paragraph = new Paragraph { Margin = new Thickness(0) };
            foreach (var slice in VocabularyNotebookPolicy.UsageRuns(example))
            {
                var run = new Run(slice.Text); if (slice.IsTarget) { run.FontWeight = FontWeights.Bold; run.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush"); }
                paragraph.Inlines.Add(run);
            }
            var rich = new RichTextBox { IsReadOnly = true, BorderThickness = new Thickness(0), Padding = new Thickness(0), Background = Brushes.Transparent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontFamily = FontFamily, FontSize = 14 },
                FlowDirection = entry.Language == LearningLanguage.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            rich.SetResourceReference(Control.ForegroundProperty, "TextPrimary"); content.Children.Add(rich);
            if (example.Translation.Length > 0) content.Children.Add(Literal(example.Translation, 13, example.TranslationLanguage));
            foreach (var variant in example.TranslationVariants.Where(value => value.Text != example.Translation || value.Language != example.TranslationLanguage))
                content.Children.Add(Literal(variant.Text, 13, variant.Language));
            content.Children.Add(MoveUsageButton(new(entry.Id, sense.Id), example.Id));
            if (ExplanationRow(entry, sense, example) is { } explanation) content.Children.Add(explanation);
            else if (example.Meaning.Length > 0) content.Children.Add(Literal(example.Meaning, 13, example.MeaningLanguage));
            Section(example.Completeness == VocabularyExampleCompleteness.Sentence ? "Example sentence" : "Context fragment", content);
        }
        AddDictionarySection(entry, sense);
        DetailContent.Children.Add(Label(sense.CreatedAt.ToLocalTime().ToString("d"), 11));
    }
    private static TextBlock Label(string text, double size, bool bold = false) => new()
    { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private void ClearImages() { foreach (var image in _images) image.Dispose(); _images.Clear(); }
    private static TextBox Literal(string text, double size, LearningLanguage? language = null, bool bold = false, bool italic = false)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = size, BorderThickness = new Thickness(0),
            Padding = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0, 6, 0, 4),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            FlowDirection = language == LearningLanguage.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        box.SetResourceReference(Control.ForegroundProperty, "TextPrimary"); return box;
    }
    private void Section(string title, UIElement content)
    {
        var panel = new StackPanel(); panel.Children.Add(Label(title, 12, bold: true)); panel.Children.Add(content);
        var border = new Border { Child = panel, Padding = new Thickness(14), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 12, 0, 0) };
        border.SetResourceReference(Border.BackgroundProperty, "CardBg"); DetailContent.Children.Add(border);
    }
    private void OnEdit(object sender, RoutedEventArgs args)
    {
        if (_retired || _mutating || _editor is not null || Selection is not { } selected) return;
        // Resolve against the full acknowledged graph. A filtered row intentionally contains
        // fewer senses and would delete hidden ownership if it became an editor baseline.
        var entry = _store.Snapshot.Document.Entries.FirstOrDefault(value => value.Id == selected.EntryId);
        if (entry is null) return;
        var editor = new VocabularyEntryEditorWindow(_store, entry, selected.SenseId, _clipboard) { Owner = this }; _editor = editor;
        editor.OperationStarted += Track;
        try { editor.ShowDialog(); }
        finally { editor.OperationStarted -= Track; Track(editor.LastOperation); Track(editor.LastImport); _editor = null; }
    }
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_closing) return; args.Cancel = true; CloseActionMenus(); RetireExport(); RetireBackupPresentation(); _learning?.Cancel(); Hide(); CancelFiltering(); ++_version; InvalidateExportOffer();
    }
    private void CancelFiltering()
    {
        var work = _filterWork; _filterWork = null;
        try { work?.Cancel(); } catch (ObjectDisposedException) { /* The last worker already finished and released its source. */ }
    }
    internal Task DrainAsync() { lock (_work) return Task.WhenAll(_work.ToArray()); }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess();
        if (!_retired)
        {
            _retired = true; _store.Changed -= OnStoreChanged; CancelFiltering(); ++_version;
            RetireActions(); RetireLearning(); RetireExport(); RetireBackupPresentation();
            if (_editor is { } editor) Track(editor.RetireAsync());
            _closing = true; Close();
        }
        // Work finishes independently of dispatcher continuations, so application shutdown can
        // join it even when WPF has stopped accepting presentation callbacks.
        return DrainAsync();
    }
}
