using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI.Learning;

/// <summary>
/// Nonactivating bundled dictionary card adapted from LearningDictionaryCard/DefinitionView.
/// It renders literal attributed source senses and exact component occurrences; the injected
/// session owns lookup/lifetime. No HTML, model request, editor read or vocabulary write occurs here.
/// </summary>
public partial class LearningDictionaryWindow : Window
{
    private LearningDictionaryState? _state;
    private LearningDictionaryViewport _viewport = new();
    public LearningDictionaryWindow()
    {
        InitializeComponent();
        WindowTheme.Follow(this);
        SourceInitialized += (_, _) => LearningPanelPlacement.Install(this);
    }
    public event Action<LearningDictionaryWord, Guid>? WordRequested;
    public event Action<Guid>? BackRequested;
    public event Action<Guid>? CloseRequested;

    public void ShowState(LearningDictionaryState state, ScreenRect anchor)
    {
        Dispatcher.VerifyAccess();
        _state = state;
        if (state.Unit is not { } unit) { Hide(); return; }
        TitleText.Text = state.Title;
        TitleText.FlowDirection = WordButtons.FlowDirection = state.Language == LearningLanguage.Arabic
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        WordSection.Visibility = unit.Kind == LearningUnitKind.Word ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Visibility = state.SelectedWord is null ? Visibility.Collapsed : Visibility.Visible;
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
        DefinitionContent.Children.Clear();
        AttributionText.Inlines.Clear();
        if (state.Status == LearningDictionaryContentStatus.Loading) AddText("Looking up…", secondary: true);
        else if (state.Status == LearningDictionaryContentStatus.Failed)
            AddText(state.ErrorMessage ?? "The dictionary could not complete this lookup.", secondary: true);
        else if (state.Result is LearningDictionaryResult.Entries entries)
        {
            foreach (var entry in entries.Values)
            {
                if (entry.Headword.Normalize() != state.Query.Normalize()) AddText(entry.Headword, bold: true);
                if (entry.Phonetics is { Count: > 0 }) AddText(string.Join(" · ", entry.Phonetics), secondary: true);
                foreach (var sense in entry.Senses)
                {
                    AddText(sense.PartOfSpeech + (sense.Labels.Count == 0 ? "" : " · " + string.Join(", ", sense.Labels)), secondary: true);
                    for (int index = 0; index < sense.Definitions.Count; index++) AddText($"{index + 1}. {sense.Definitions[index]}");
                }
                foreach (var example in entry.Examples ?? []) AddText(example, secondary: true);
                AddLink(entry.SourceName ?? "Wiktionary / Kaikki", entry.SourceUrl);
                if (entry.LicenseUrl is { } license) AddLink(entry.LicenseName ?? "License", license);
                else if (entry.LicenseName is { Length: > 0 } name) AddText(name, secondary: true);
            }
            var attribution = Link("Wiktionary contributors / Kaikki · CC BY-SA 4.0",
                "https://creativecommons.org/licenses/by-sa/4.0/");
            if (attribution is not null) AttributionText.Inlines.Add(attribution);
        }
        else AddText(state.IsExpression
            ? "No dictionary entry was found for this expression. Select a word below to look it up."
            : "No definition for this word was found in the available dictionary.", secondary: true);
        double contentWidth = Math.Max(1, 410 - 32 - SystemParameters.VerticalScrollBarWidth);
        DefinitionContent.Measure(new Size(contentWidth, double.PositiveInfinity));
        _viewport.Measure(DefinitionContent.DesiredSize.Height, state.Query,
            resolved: state.Status == LearningDictionaryContentStatus.Result && state.Result is LearningDictionaryResult.Entries);
        DefinitionViewport.Height = _viewport.HeightFor(state.Query);
        if (!IsVisible) Show();
        UpdateLayout();
        LearningPanelPlacement.Position(this, anchor);
    }

    public void HideCard() { _state = null; _viewport = new(); Hide(); }
    private void AddText(string text, bool secondary = false, bool bold = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = secondary ? 11 : 13,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 7) };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextSecondary" : "TextPrimary");
        DefinitionContent.Children.Add(block);
    }
    private void AddLink(string title, string value)
    {
        if (Link(title, value) is not { } link) return;
        var block = new TextBlock { FontSize = 10, Margin = new Thickness(0, 0, 0, 7), TextWrapping = TextWrapping.Wrap };
        block.Inlines.Add(link); DefinitionContent.Children.Add(block);
    }
    private static Hyperlink? Link(string title, string value)
    {
        if (DictionaryLinkPolicy.ExternalUrl(value) is not { } url) return null;
        var link = new Hyperlink(new Run(title)) { Focusable = false, ToolTip = url.AbsoluteUri };
        link.Click += (_, args) =>
        {
            args.Handled = true;
            try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
            catch { /* An unavailable browser cannot fault the dictionary session or expose paths. */ }
        };
        return link;
    }
    private void OnClose(object sender, RoutedEventArgs args) { if (_state is { } state) CloseRequested?.Invoke(state.TargetIdentity); }
    private void OnBack(object sender, RoutedEventArgs args) { if (_state is { } state) BackRequested?.Invoke(state.TargetIdentity); }
}
