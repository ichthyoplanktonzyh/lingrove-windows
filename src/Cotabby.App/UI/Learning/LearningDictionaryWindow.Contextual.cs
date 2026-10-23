using System.Windows;
using System.Windows.Documents;
using Cotabby.App.Learning;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI.Learning;

/// <summary>
/// Plain contextual meaning/example/details presentation from Mac LearningExplanationView. The
/// window borrows immutable snapshots and emits target-bound intents; its App controller validates
/// the original field. It owns neither generation nor saved vocabulary, and parses no model HTML.
/// </summary>
public partial class LearningDictionaryWindow
{
    private LearningExplanationInput? _contextInput;
    private bool _presentingContext;

    private void PresentContextual(LearningDictionaryState state, LearningContextExampleTranslation? exampleTranslation)
    {
        _presentingContext = true;
        try
        {
            var explanation = state.Explanation;
            if (_contextInput != explanation?.Input)
            {
                ExplanationDetails.IsExpanded = false;
                DefinitionViewport.ScrollToTop();
            }
            _contextInput = explanation?.Input;
            bool contextual = state.Page == LearningDictionaryPage.Contextual;
            ContextualContent.Visibility = contextual ? Visibility.Visible : Visibility.Collapsed;
            PageButtons.Visibility = state.HasContextualExplanation ? Visibility.Visible : Visibility.Collapsed;
            ContextualPageButton.Content = LearningDictionaryCardPolicy.ContextualLabel(_contextInput);
            ContextualPageButton.FontWeight = contextual ? FontWeights.SemiBold : FontWeights.Normal;
            DictionaryPageButton.FontWeight = contextual ? FontWeights.Normal : FontWeights.SemiBold;
            System.Windows.Automation.AutomationProperties.SetItemStatus(ContextualPageButton, contextual ? "Selected" : "");
            System.Windows.Automation.AutomationProperties.SetItemStatus(DictionaryPageButton, contextual ? "" : "Selected");
            SubtitleText.Text = contextual ? state.ExplanationLanguage is { } language && language != state.Language
                ? $"{state.Language.Label()} → {language.Label()}" : state.Language.Label()
                : "Bundled expression dictionary · definitions in English";
            var action = explanation is null ? LearningExplanationCardAction.None
                : LearningExplanationCardPolicy.Action(explanation, state.ExplanationConfigured);
            ExplanationButton.Visibility = state.HasContextualExplanation ? Visibility.Visible : Visibility.Collapsed;
            ExplanationButton.IsEnabled = action != LearningExplanationCardAction.None;
            ExplanationButton.Content = explanation?.IsGenerating == true ? "■" : "✦";
            string label = explanation is null ? "AI explanation" : LearningExplanationCardPolicy.Label(explanation, state.ExplanationConfigured);
            ExplanationButton.ToolTip = explanation?.ErrorMessage is not null && state.ExplanationConfigured && !explanation.IsGenerating
                ? label + "\n" + explanation.ErrorMessage : label;
            System.Windows.Automation.AutomationProperties.SetName(ExplanationButton, label);
            ContextualMeaning.Text = explanation?.Record?.Result.Meaning ?? string.Empty;
            ContextualMeaning.Visibility = explanation?.Record is null ? Visibility.Collapsed : Visibility.Visible;
            ExplanationProse.Text = explanation?.Record?.Result.Explanation ?? string.Empty;
            ExplanationDetails.Visibility = explanation?.Record is null ? Visibility.Collapsed : Visibility.Visible;
            ContextualMeaning.FlowDirection = ExplanationProse.FlowDirection = state.ExplanationLanguage == LearningLanguage.Arabic
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            ExplanationProgress.Visibility = explanation?.IsGenerating == true ? Visibility.Visible : Visibility.Collapsed;
            ExplanationStatus.Text = explanation?.IsGenerating == true ? "Explaining…" : explanation?.ErrorMessage ?? string.Empty;
            ExplanationStatus.Visibility = ExplanationStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            PresentExample();
            ContextualExampleTranslation.Text = DictionaryExampleTranslation.Text = exampleTranslation?.Text ?? string.Empty;
            ContextualExampleTranslation.FlowDirection = DictionaryExampleTranslation.FlowDirection = exampleTranslation?.Language == LearningLanguage.Arabic
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            ContextualExampleTranslation.Visibility = DictionaryExampleTranslation.Visibility = exampleTranslation is null
                ? Visibility.Collapsed : Visibility.Visible;
            if (contextual) AttributionText.Text = explanation?.Record?.Model ?? "AI explanation";
        }
        finally { _presentingContext = false; }
    }

    private void PresentExample()
    {
        var runs = LearningDictionaryCardPolicy.ContextRuns(_contextInput);
        ContextualExampleText.Inlines.Clear(); DictionaryExampleText.Inlines.Clear();
        foreach (var run in runs)
        {
            Run Slice()
            {
                var slice = new Run(run.Text);
                if (run.IsTarget)
                {
                    slice.FontWeight = FontWeights.SemiBold;
                    slice.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                }
                return slice;
            }
            ContextualExampleText.Inlines.Add(Slice()); DictionaryExampleText.Inlines.Add(Slice());
        }
        ContextualExampleText.FlowDirection = DictionaryExampleText.FlowDirection = _contextInput?.Language == LearningLanguage.Arabic
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ContextualExample.Visibility = runs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FragmentLabel.Visibility = _contextInput?.ContextKind == LearningExplanationContext.Fragment ? Visibility.Visible : Visibility.Collapsed;
        DictionaryExample.Visibility = runs.Count > 0 && _state?.Page == LearningDictionaryPage.Dictionary ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDetailsChanged(object sender, RoutedEventArgs args)
    {
        if (_presentingContext || _state is not { } state) return;
        MeasureBody(); UpdateLayout(); LearningPanelPlacement.Position(this, _anchor);
        if (!ExplanationDetails.IsExpanded)
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (_state?.TargetIdentity == state.TargetIdentity && !ExplanationDetails.IsExpanded) DefinitionViewport.ScrollToTop();
            });
    }
    private void OnContextualPage(object sender, RoutedEventArgs args)
    { if (_state is { } state) PageRequested?.Invoke(LearningDictionaryPage.Contextual, state.TargetIdentity); }
    private void OnDictionaryPage(object sender, RoutedEventArgs args)
    { if (_state is { } state) PageRequested?.Invoke(LearningDictionaryPage.Dictionary, state.TargetIdentity); }
    private void OnExplanation(object sender, RoutedEventArgs args)
    { if (_state is { Explanation: not null } state) ExplanationRequested?.Invoke(state.TargetIdentity, state.Explanation); }
}
