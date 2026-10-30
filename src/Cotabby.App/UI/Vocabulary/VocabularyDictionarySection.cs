using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.App.UI.Learning;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// A notebook sense owns this explicit dictionary disclosure, adapting Mac DictionarySection.
/// It borrows the window's dictionary session; opening requests lookup, collapse/disappear retires
/// it, and ordinary window hide keeps expansion for the owner's Resume. The shared literal
/// renderer and viewport are the same ones used by floating cards. No explanation is requested.
/// </summary>
internal sealed class VocabularyDictionarySection : Expander, IDisposable
{
    private readonly VocabularyNotebookSession _owner;
    private readonly Func<bool> _canAct;
    private readonly StackPanel _body = new(), _definitions = new();
    private readonly WrapPanel _words = new();
    private readonly Button _back = new() { Content = "Back to expression", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _query = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 7) };
    private readonly TextBlock _attribution = new() { FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) };
    private readonly ScrollViewer _viewport;
    private readonly LearningDictionaryViewport _measurement = new();
    private readonly CancellationTokenSource _presentation = new();
    private readonly CancellationToken _token;
    private VocabularyEntry _entry;
    private readonly Guid? _senseId;
    private LearningDictionaryState? _displayed;
    private bool _needsLatestQuery;
    private volatile bool _disposed;
    internal Task LastLookup => _owner.LastDictionaryOperation;
    internal Task LastNavigation { get; private set; } = Task.CompletedTask;

    internal VocabularyDictionarySection(VocabularyNotebookSession owner, VocabularyEntry entry, Func<bool> canAct)
    {
        _owner = owner; _entry = entry; _senseId = owner.SenseId; _canAct = canAct; _token = _presentation.Token;
        Header = "Dictionary"; Margin = new Thickness(0, 14, 0, 0);
        var source = new TextBlock { Text = "Bundled expression dictionary · definitions in English", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        source.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); _attribution.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        _viewport = new ScrollViewer { Content = _definitions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 120 };
        _body.Children.Add(source); _body.Children.Add(_query); _body.Children.Add(_back); _body.Children.Add(_words); _body.Children.Add(_viewport); _body.Children.Add(_attribution); Content = _body;
        Expanded += OnExpanded; Collapsed += OnCollapsed; _back.Click += OnBack;
        owner.Changed += OnOwnerChanged; owner.Dictionary.StateChanged += OnDictionaryChanged;
    }
    private bool Current() => !_disposed && _canAct() && _owner.EntryId == _entry.Id;
    internal void Update(VocabularyEntry entry)
    {
        Dispatcher.VerifyAccess();
        bool formChanged = !VocabularyNotebookTargetPolicy.SameQuery(_entry.DictionaryForm, entry.DictionaryForm);
        bool queryChanged = !VocabularyNotebookTargetPolicy.SameQuery(VocabularyNotebookTargetPolicy.DictionaryQuery(_entry), VocabularyNotebookTargetPolicy.DictionaryQuery(entry));
        _entry = entry;
        // The pinned disclosure observes form changes; language changes only invalidate the
        // session. Preserve that rule rather than broadening automatic provider requests.
        if (IsExpanded && (formChanged || queryChanged && _owner.Dictionary.State.Unit is null))
        {
            _needsLatestQuery = true;
            if (Current()) { _needsLatestQuery = false; _ = _owner.ShowDictionaryAsync(entry); }
        }
        Render(_owner.Dictionary.State);
    }
    internal void ResumeExpanded()
    {
        Dispatcher.VerifyAccess();
        // Store updates may have rendered while hidden, when user actions are gated. The
        // retained disclosure is the explicit intent; resume its latest acknowledged entry,
        // rather than the older value remembered before form/expression editing.
        if (!_disposed && IsExpanded && _needsLatestQuery && _owner.EntryId == _entry.Id && _owner.SenseId == _senseId)
        { _needsLatestQuery = false; _ = _owner.ShowDictionaryAsync(_entry); }
    }
    private void OnExpanded(object sender, RoutedEventArgs args)
    { if (ReferenceEquals(args.OriginalSource, this) && Current()) { _needsLatestQuery = false; _ = _owner.ShowDictionaryAsync(_entry); } }
    private void OnCollapsed(object sender, RoutedEventArgs args)
    { if (ReferenceEquals(args.OriginalSource, this) && Current()) { _needsLatestQuery = false; _owner.HideDictionary(); } }
    private void OnOwnerChanged() { if (!_disposed && Dispatcher.CheckAccess() && Current()) Render(_owner.Dictionary.State); }
    private void OnDictionaryChanged(LearningDictionaryState state)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) { if (Current() && ReferenceEquals(state, _owner.Dictionary.State)) Render(state); return; }
        try { _ = Dispatcher.InvokeAsync(() => { if (Current() && ReferenceEquals(state, _owner.Dictionary.State)) Render(state); }, DispatcherPriority.DataBind, _token); }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
    private void Render(LearningDictionaryState state)
    {
        if (_disposed) return; _displayed = state; _definitions.Children.Clear(); _words.Children.Clear(); _attribution.Inlines.Clear();
        _query.Text = state.Unit is null ? _owner.DictionaryError ?? (_owner.IsPreparingDictionary ? "Looking up…" : "") : "";
        _query.Visibility = _query.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _back.Visibility = state.SelectedWord is null ? Visibility.Collapsed : Visibility.Visible;
        if (state.Unit is { } unit)
        {
            _body.FlowDirection = state.Language == LearningLanguage.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            if (unit.Kind != LearningUnitKind.Word)
                foreach (var word in unit.Words)
                {
                    var button = new Button { Content = word.Text, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 0, 6, 5),
                        FontWeight = state.SelectedWord == word ? FontWeights.SemiBold : FontWeights.Normal };
                    button.Click += (_, _) => { if (Current() && IsExpanded && ReferenceEquals(state, _owner.Dictionary.State)) LastNavigation = _owner.Dictionary.SelectWordAsync(word, state.TargetIdentity); };
                    _words.Children.Add(button);
                }
            LearningDictionaryDefinitions.Populate(_definitions, _attribution, state,
                () => LastNavigation = _owner.Dictionary.ConfirmOnlineAsync(state.RequestIdentity),
                () => Current() && IsExpanded && ReferenceEquals(state, _owner.Dictionary.State));
        }
        double width = Math.Max(1, ActualWidth > 0 ? ActualWidth - SystemParameters.VerticalScrollBarWidth : 400);
        _definitions.Measure(new Size(width, double.PositiveInfinity));
        _measurement.Measure(_definitions.DesiredSize.Height, state.Query, state.Status == LearningDictionaryContentStatus.Result && state.Result is LearningDictionaryResult.Entries);
        _viewport.Height = _measurement.HeightFor(state.Query);
    }
    private void OnBack(object sender, RoutedEventArgs args)
    { if (Current() && IsExpanded && _displayed is { } state && ReferenceEquals(state, _owner.Dictionary.State)) LastNavigation = _owner.Dictionary.ReturnToExpressionAsync(state.TargetIdentity); }
    public void Dispose()
    {
        Dispatcher.VerifyAccess(); if (_disposed) return;
        bool ownsDictionary = _owner.Dictionary.State.DocumentId == VocabularyNotebookTargetPolicy.DocumentId(_entry)
            || IsExpanded && _owner.EntryId == _entry.Id && _owner.SenseId == _senseId;
        _disposed = true; _owner.Changed -= OnOwnerChanged; _owner.Dictionary.StateChanged -= OnDictionaryChanged;
        Expanded -= OnExpanded; Collapsed -= OnCollapsed; _back.Click -= OnBack; _presentation.Cancel(); _presentation.Dispose();
        if (ownsDictionary) _owner.HideDictionary();
    }
}
