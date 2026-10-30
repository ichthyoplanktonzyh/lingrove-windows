using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
using Cotabby.Win32.Text;
using Cotabby.App.Review;

namespace Cotabby.App.UI;

/// <summary>Sample notebook browser borrows the actual canonical snapshot and graph canvas.
/// Its full notebook and edits use only this mounted example's isolated in-memory Store.</summary>
internal sealed class LearningShowcaseVocabulary : Grid, IAsyncDisposable
{
    private sealed class Preferences : IVocabularyGraphPreferences
    {
        private byte[]? _value;
        public string BrowserMode { get; set; } = "list";
        public byte[]? ReadSettings() => _value?.ToArray();
        public void WriteSettings(ReadOnlySpan<byte> data) => _value = data.ToArray();
    }
    private sealed class NoFiles : IVocabularyWordListReader
    { public Task<VocabularyWordList> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromException<VocabularyWordList>(new NotSupportedException()); }
    private readonly LearningShowcaseStore _store;
    private readonly Preferences _preferences = new();
    private readonly VocabularyGraphPresentation _presentation;
    private readonly VocabularyGraphView _graph;
    private readonly TreeView _list = new();
    private readonly ContentControl _body = new();
    private readonly IDisposable _subscription;
    private readonly HashSet<Task> _work = [];
    private FrozenVocabularyImportSnapshot? _snapshot;
    private FrozenVocabularyNotebookWindow? _window;
    private bool _closed, _graphMode;
    private VocabularySelection? _selection;
    private readonly Func<AnkiReviewFeature>? _review;
    private readonly ILearningExplanationGenerating? _explanations;
    private readonly ILearningDictionaryProviding? _dictionary;
    internal event Action? Interacted;

    internal LearningShowcaseVocabulary(LearningShowcaseStore store, Func<AnkiReviewFeature>? review = null,
        ILearningExplanationGenerating? explanations = null, ILearningDictionaryProviding? dictionary = null)
    {
        _review = review; _explanations = explanations; _dictionary = dictionary;
        _store = store; _presentation = new(_preferences); _graph = new(_presentation, new WindowsVocabularyTextSearch());
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var bar = new WrapPanel();
        var list = new Button { Content = "List", Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(8, 4, 8, 4) };
        var graph = new Button { Content = "Graph", Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(8, 4, 8, 4) };
        var open = new Button { Content = "Open vocabulary", Margin = new Thickness(0, 0, 0, 5), Padding = new Thickness(8, 4, 8, 4) };
        list.Click += (_, _) => { Interacted?.Invoke(); SetGraph(false); };
        graph.Click += (_, _) => { Interacted?.Invoke(); SetGraph(true); };
        open.Click += (_, _) => { Interacted?.Invoke(); Track(OpenNotebookAsync(null)); };
        bar.Children.Add(list); bar.Children.Add(graph); bar.Children.Add(open); Children.Add(bar);
        Grid.SetRow(_body, 1); _body.MinHeight = 220; Children.Add(_body); _body.Content = _list;
        _graph.PreviewMouseDown += (_, _) => Interacted?.Invoke();
        _graph.PreviewKeyDown += (_, _) => Interacted?.Invoke();
        _subscription = store.Backend.Subscribe(snapshot => Dispatcher.BeginInvoke(() => Apply(snapshot)));
    }
    internal async Task RefreshAsync() { var snapshot = await _store.Backend.ReadAsync(); if (!_closed) Apply(snapshot); }
    private void Apply(FrozenVocabularyImportSnapshot snapshot)
    {
        if (_closed || _snapshot is { } old && old.Revision > snapshot.Revision) return;
        _snapshot = snapshot; _list.Items.Clear();
        foreach (var entry in snapshot.Document.CollectedEntries)
        {
            var head = new TreeViewItem { Header = entry.Expression, IsExpanded = entry.Expression == "bank" };
            foreach (var sense in entry.Senses)
            {
                var selection = new VocabularySelection(entry.Id, sense.Id);
                var button = new Button { Content = FrozenVocabularyNotebookWindow.SenseTitle(sense),
                    HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2) };
                button.Click += (_, _) => { Interacted?.Invoke(); Select(selection); };
                head.Items.Add(button);
            }
            head.Expanded += (_, _) => Interacted?.Invoke(); head.Collapsed += (_, _) => Interacted?.Invoke();
            _list.Items.Add(head);
        }
        UpdateGraph();
    }
    private void UpdateGraph()
    {
        if (_snapshot is not { } snapshot) return;
        var entries = snapshot.Document.CollectedEntries.ToArray(); var search = new WindowsVocabularyTextSearch();
        _graph.UpdateCanonical(expanded => CanonicalVocabularyGraphProjection.Entries(entries, expanded, "", search,
            FrozenVocabularyNotebookWindow.SenseTitle, language => language?.Name() ?? "Unspecified"),
            _selection, selection => { Interacted?.Invoke(); if (selection is { } target) Select(target); }, null, null, _ => { }, _ => { });
    }
    internal void SetGraph(bool value)
    {
        if (_closed) return; _graphMode = value; _preferences.BrowserMode = value ? "graph" : "list";
        _body.Content = value ? _graph : _list; UpdateGraph();
    }
    internal void ShowStep(int step)
    {
        if (_closed) return; SetGraph(step is 2 or 3 || step == 4 && _graphMode);
        if (step == 3 && _snapshot?.Document.CollectedEntries.FirstOrDefault(e => e.Expression == "bank") is { } bank && !_presentation.Expanded.Contains(bank.Id))
            _presentation.Toggle(bank.Id);
        if (step is 1 or 4 && _snapshot?.Document.CollectedEntries.FirstOrDefault(e => e.Expression == "bank") is { } selected && selected.Senses.FirstOrDefault() is { } sense)
            Select(new(selected.Id, sense.Id));
    }
    internal void Select(VocabularySelection selection) { _selection = selection; _presentation.Reveal(selection); UpdateGraph(); Track(OpenNotebookAsync(selection)); }
    private async Task OpenNotebookAsync(VocabularySelection? selection)
    {
        if (_closed) return;
        if (_window is null)
        {
            var window = new FrozenVocabularyNotebookWindow(_store.Backend, new NoFiles(), [LearningLanguage.English],
                pickFile: () => null, exampleNoExternalFiles: true) { Owner = Window.GetWindow(this), Title = "Vocabulary · Sample content" };
            window.AttachGraphPreferences(_preferences); _window = window;
            if (_review is not null) window.AttachReviewFeature(_review(), () => { });
            if (_explanations is not null && _dictionary is not null)
            {
                var lexical = new IcuLearningLexicalText();
                window.AttachLearning(_explanations, _dictionary, lexical, text => lexical.Words(text, LearningLanguage.English), () => { });
            }
            window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
            window.Show();
            if (window.LastOperation is { } load) await load;
        }
        else _window.Activate();
        if (!_closed && selection is { } target) _window?.Reveal(target);
    }
    private void Track(Task task)
    {
        _work.Add(task); _ = task.ContinueWith(done => Dispatcher.BeginInvoke(() => { _ = done.Exception; _work.Remove(done); }));
    }
    public async ValueTask DisposeAsync()
    {
        if (_closed) return; _closed = true; _subscription.Dispose();
        if (_window is { } window) { _window = null; await window.RetireAsync(); }
        if (_work.Count != 0) try { await Task.WhenAll(_work.ToArray()); } catch { }
        await _graph.RetireAsync();
    }
}
