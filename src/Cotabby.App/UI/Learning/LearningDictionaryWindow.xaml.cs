using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Cotabby.App.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.App.Vocabulary;

namespace Cotabby.App.UI.Learning;

/// <summary>
/// Nonactivating bundled dictionary card adapted from LearningDictionaryCard/DefinitionView.
/// It renders independent contextual meaning and literal attributed dictionary senses. Exact
/// component controls emit intents; the injected session/controller own generation, lookup and
/// editor authority. This view performs no HTML parsing, editor read or vocabulary write.
/// </summary>
public partial class LearningDictionaryWindow : Window
{
    private LearningDictionaryState? _state;
    private LearningDictionaryViewport _viewport = new();
    private ScreenRect _anchor;
    private LearningDictionarySession? _session; private Func<bool>? _permit; private DictionaryArticleView? _article;
    private Guid _articleRequest; private bool _updatingSources; private Task _articleShutdown = Task.CompletedTask;
    private readonly bool _embeddedExample;
    private sealed record SourceChoice(DictionarySource Source, string Name) { public override string ToString() => Name; }
    public LearningDictionaryWindow() : this(false) { }
    internal LearningDictionaryWindow(bool embeddedExample)
    {
        _embeddedExample = embeddedExample;
        InitializeComponent();
        FavoriteButton.FailureChanged += message =>
        { VocabularyStatus.Text = message ?? ""; VocabularyStatus.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible; };
        InstallSelection();
        if (!embeddedExample) { WindowTheme.Follow(this); SourceInitialized += (_, _) => LearningPanelPlacement.Install(this); }
        Closed += (_, _) => { if (_article is not null) _articleShutdown = _article.DisposeAsync().AsTask(); };
    }
    internal FrameworkElement TakeExampleCard()
    {
        if (!_embeddedExample || !ReferenceEquals(Content, Card)) throw new InvalidOperationException();
        Content = null; Card.Width = double.NaN; Card.MaxWidth = 410;
        foreach (var button in new[] { CloseButton, ExplanationButton, BackButton, ContextualPageButton, DictionaryPageButton }) button.Focusable = true;
        SourcePicker.Focusable = true; return Card;
    }
    public event Action<LearningDictionaryWord, Guid>? WordRequested;
    public event Action<Guid>? BackRequested;
    public event Action<Guid>? CloseRequested;
    public event Action<LearningDictionaryPage, Guid>? PageRequested;
    public event Action<Guid, LearningExplanationState>? ExplanationRequested;
    internal event Action<DictionarySource, Guid>? SourceRequested;
    internal event Action<string, Guid>? ReferenceRequested;
    internal void AttachSession(LearningDictionarySession session, Func<bool> permit) { _session = session; _permit = permit; }
    internal Task ArticleShutdown => _articleShutdown;
    internal event Action? ReadingMeaningRequested;
    internal void EnableReadingMeaning() => ReadingMeaningButton.Visibility = Visibility.Visible;
    internal Task FavoriteWork => FavoriteButton.LastOperation;
    private void OnReadingMeaning(object sender, RoutedEventArgs args) => ReadingMeaningRequested?.Invoke();

    internal void ShowState(LearningDictionaryState state, ScreenRect anchor, LearningContextExampleTranslation? exampleTranslation = null, LearningModelSnapshot? profile = null)
    {
        Dispatcher.VerifyAccess();
        if (_state?.TargetIdentity != state.TargetIdentity) FavoriteButton.Clear();
        SetSelectionTarget(state.TargetIdentity, state.Page);
        _state = state;
        _anchor = anchor;
        ExplanationDestination.Text = LearningModelDestinationText.Explanation(profile);
        ExplanationDestinationSection.Visibility = state.HasContextualExplanation ? Visibility.Visible : Visibility.Collapsed;
        if (state.Unit is not { } unit) { HideCard(); return; }
        TitleText.Text = state.Title;
        _updatingSources = true;
        try
        {
            var choices = state.AvailableSources.Select(source => new SourceChoice(source, state.SourceLabels.GetValueOrDefault(source, source.RawValue))).ToArray();
            SourcePicker.ItemsSource = choices; SourcePicker.SelectedItem = choices.FirstOrDefault(x => x.Source == state.SelectedSource);
            SourcePicker.Visibility = choices.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updatingSources = false; }
        TitleText.FlowDirection = WordButtons.FlowDirection = state.Language == LearningLanguage.Arabic
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        WordSection.Visibility = unit.Kind == LearningUnitKind.Word || state.SelectedWord is not null ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Visibility = state.SelectedWord is null && state.ReferenceQuery is null ? Visibility.Collapsed : Visibility.Visible;
        QueryText.Text = "Dictionary form: " + state.Query;
        QueryText.Visibility = state.Query.Normalize() == state.Title.Normalize() ? Visibility.Collapsed : Visibility.Visible;
        WordButtons.Children.Clear();
        foreach (var word in unit.Words)
        {
            var button = new Button { Content = word.Text, Focusable = false, Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 0, 6, 5), FontWeight = state.SelectedWord == word ? FontWeights.SemiBold : FontWeights.Normal };
            button.SetResourceReference(Control.BackgroundProperty, "KeycapBg");
            button.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            button.SetResourceReference(Control.BorderBrushProperty, "CardBorder");
            button.Click += (_, _) => WordRequested?.Invoke(word, state.TargetIdentity);
            WordButtons.Children.Add(button);
        }
        PresentContextual(state, exampleTranslation);
        bool contextual = state.Page == LearningDictionaryPage.Contextual;
        DefinitionContent.Visibility = contextual ? Visibility.Collapsed : Visibility.Visible;
        if (contextual) { _article?.Retire(); _articleRequest = Guid.Empty; DefinitionContent.Children.Clear(); }
        else PresentDefinitions(state);
        AttributionText.Visibility = new TextRange(AttributionText.ContentStart, AttributionText.ContentEnd).IsEmpty
            ? Visibility.Collapsed : Visibility.Visible;
        MeasureBody();
        if (!_embeddedExample) { if (!IsVisible) Show(); UpdateLayout(); LearningPanelPlacement.Position(this, anchor); }
    }

    private void PresentDefinitions(LearningDictionaryState state)
    {
        if (state.Result is not LearningDictionaryResult.Article article || article.ResourceLibrary is null || _session is null)
        {
            _article?.Retire(); _articleRequest = Guid.Empty;
            bool Current() => (_embeddedExample ? Card.IsVisible : IsVisible) && ReferenceEquals(_state, state) && _session?.State.RequestIdentity == state.RequestIdentity
                && (_permit?.Invoke() ?? false);
            LearningDictionaryDefinitions.Populate(DefinitionContent, AttributionText, state,
                () => { if (Current()) _ = _session!.ConfirmOnlineAsync(state.RequestIdentity); }, Current);
            return;
        }
        if (_article is null)
        {
            _article = new DictionaryArticleView { Height = 200 };
            _article.ReferenceRequested += query => { if (_state is { } current) ReferenceRequested?.Invoke(query, current.TargetIdentity); };
        }
        DefinitionContent.Children.Clear(); DefinitionContent.Children.Add(_article); AttributionText.Text = article.SourceName;
        if (_articleRequest == state.RequestIdentity) return; _articleRequest = state.RequestIdentity;
        Guid request = state.RequestIdentity;
        _ = _article.ShowAsync(article, (path, token) => _session.LoadResourceAsync(path, article.ResourceLibrary!.Value, request, token),
            () => _state?.RequestIdentity == request && _session.State.RequestIdentity == request && (_permit?.Invoke() ?? false), CancellationToken.None);
    }
    private void OnSourceChanged(object sender, SelectionChangedEventArgs args)
    { if (!_updatingSources && _state is { } state && SourcePicker.SelectedItem is SourceChoice choice) SourceRequested?.Invoke(choice.Source, state.TargetIdentity); }

    private void MeasureBody()
    {
        if (_state is not { } state) return;
        double contentWidth = Math.Max(1, 410 - 32 - SystemParameters.VerticalScrollBarWidth);
        BodyContent.Measure(new Size(contentWidth, double.PositiveInfinity));
        string query = state.Query + ":" + state.Page;
        _viewport.Measure(BodyContent.DesiredSize.Height, query,
            resolved: state.Page == LearningDictionaryPage.Dictionary
                && state.Status == LearningDictionaryContentStatus.Result && state.Result is LearningDictionaryResult.Entries or LearningDictionaryResult.Article);
        DefinitionViewport.Height = _viewport.HeightFor(query);
    }

    public void HideCard()
    {
        FavoriteButton.Clear();
        _article?.Retire(); _articleRequest = Guid.Empty;
        _state = null; _viewport = new(); _contextInput = null;
        ContextualMeaning.DocumentIdentity = ContextualExampleText.DocumentIdentity =
            ContextualExampleTranslation.DocumentIdentity = ExplanationProse.DocumentIdentity = null;
        CopyStatus.Text = string.Empty; CopyStatus.Visibility = Visibility.Collapsed;
        TitleText.Text = QueryText.Text = string.Empty;
        DefinitionContent.Children.Clear(); WordButtons.Children.Clear(); AttributionText.Inlines.Clear();
        ContextualMeaning.Text = ExplanationProse.Text = ExplanationStatus.Text = string.Empty;
        ContextualExampleTranslation.Text = DictionaryExampleTranslation.Text = string.Empty;
        ContextualExampleText.Inlines.Clear(); DictionaryExampleText.Inlines.Clear();
        ExplanationDetails.IsExpanded = false;
        if (!_embeddedExample) Hide();
    }
    internal void ClearVocabulary() => FavoriteButton.Clear();
    internal void BindVocabulary(IWritingVocabularyStore store, VocabularyCapture capture, Guid target, Func<bool> canAct, Func<Task<bool>>? preflight = null)
    {
        Dispatcher.VerifyAccess();
        if ((!IsVisible && !_embeddedExample) || _state?.TargetIdentity != target) return;
        FavoriteButton.Bind(store, capture, () => (_embeddedExample ? Card.IsVisible : IsVisible) && _state?.TargetIdentity == target && canAct(), preflight);
    }
    private void OnClose(object sender, RoutedEventArgs args) { if (_state is { } state) CloseRequested?.Invoke(state.TargetIdentity); }
    private void OnBack(object sender, RoutedEventArgs args) { if (_state is { } state) BackRequested?.Invoke(state.TargetIdentity); }
}
