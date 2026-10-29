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
    public LearningDictionaryWindow()
    {
        InitializeComponent();
        FavoriteButton.FailureChanged += message =>
        { VocabularyStatus.Text = message ?? ""; VocabularyStatus.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible; };
        InstallSelection();
        WindowTheme.Follow(this);
        SourceInitialized += (_, _) => LearningPanelPlacement.Install(this);
    }
    public event Action<LearningDictionaryWord, Guid>? WordRequested;
    public event Action<Guid>? BackRequested;
    public event Action<Guid>? CloseRequested;
    public event Action<LearningDictionaryPage, Guid>? PageRequested;
    public event Action<Guid, LearningExplanationState>? ExplanationRequested;

    internal void ShowState(LearningDictionaryState state, ScreenRect anchor, LearningContextExampleTranslation? exampleTranslation = null)
    {
        Dispatcher.VerifyAccess();
        if (_state?.TargetIdentity != state.TargetIdentity) FavoriteButton.Clear();
        SetSelectionTarget(state.TargetIdentity, state.Page);
        _state = state;
        _anchor = anchor;
        if (state.Unit is not { } unit) { HideCard(); return; }
        TitleText.Text = state.Title;
        TitleText.FlowDirection = WordButtons.FlowDirection = state.Language == LearningLanguage.Arabic
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        WordSection.Visibility = unit.Kind == LearningUnitKind.Word || state.SelectedWord is not null ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Visibility = state.SelectedWord is null ? Visibility.Collapsed : Visibility.Visible;
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
        if (contextual) DefinitionContent.Children.Clear();
        else PresentDefinitions(state);
        MeasureBody();
        if (!IsVisible) Show();
        UpdateLayout();
        LearningPanelPlacement.Position(this, anchor);
    }

    private void PresentDefinitions(LearningDictionaryState state) => LearningDictionaryDefinitions.Populate(DefinitionContent, AttributionText, state);

    private void MeasureBody()
    {
        if (_state is not { } state) return;
        double contentWidth = Math.Max(1, 410 - 32 - SystemParameters.VerticalScrollBarWidth);
        BodyContent.Measure(new Size(contentWidth, double.PositiveInfinity));
        string query = state.Query + ":" + state.Page;
        _viewport.Measure(BodyContent.DesiredSize.Height, query,
            resolved: state.Page == LearningDictionaryPage.Dictionary
                && state.Status == LearningDictionaryContentStatus.Result && state.Result is LearningDictionaryResult.Entries);
        DefinitionViewport.Height = _viewport.HeightFor(query);
    }

    public void HideCard()
    {
        FavoriteButton.Clear();
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
        Hide();
    }
    internal void BindVocabulary(IWritingVocabularyStore store, VocabularyCapture capture, Guid target, Func<bool> canAct)
    {
        Dispatcher.VerifyAccess();
        if (!IsVisible || _state?.TargetIdentity != target) return;
        FavoriteButton.Bind(store, capture, () => IsVisible && _state?.TargetIdentity == target && canAct());
    }
    private void OnClose(object sender, RoutedEventArgs args) { if (_state is { } state) CloseRequested?.Invoke(state.TargetIdentity); }
    private void OnBack(object sender, RoutedEventArgs args) { if (_state is { } state) BackRequested?.Invoke(state.TargetIdentity); }
}
