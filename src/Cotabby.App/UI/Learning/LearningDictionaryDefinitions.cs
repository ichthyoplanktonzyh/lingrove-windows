using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI.Learning;

/// <summary>
/// Shared literal bundled-definition rendering adapted from Mac LearningDictionaryDefinitionView.
/// Floating cards and the notebook supply their own containers, target/navigation and viewport;
/// this helper owns no query/session. Links retain the existing explicit external-URL policy.
/// </summary>
internal static class LearningDictionaryDefinitions
{
    internal static void Populate(StackPanel content, TextBlock attributionText, LearningDictionaryState state, Action? confirmOnline = null, Func<bool>? current = null)
    {
        content.Children.Clear();
        bool contextual = state.Page == LearningDictionaryPage.Contextual;
        if (!contextual) attributionText.Inlines.Clear();
        if (state.Status == LearningDictionaryContentStatus.AwaitingOnlineConfirmation)
        {
            AddText(content, "Online English dictionary · https://api.dictionaryapi.dev", bold: true);
            AddText(content, "Only this lookup term is sent to this destination.", secondary: true);
            var button = new Button { Content = "Look up online", HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = confirmOnline is not null };
            button.SetResourceReference(FrameworkElement.StyleProperty, "Btn");
            button.Click += (_, _) => { if (current?.Invoke() != false) confirmOnline?.Invoke(); }; content.Children.Add(button);
        }
        else if (state.Status == LearningDictionaryContentStatus.Loading)
        {
            if (state.SelectedSource == DictionarySource.Online) AddText(content, "Online English dictionary · https://api.dictionaryapi.dev", secondary: true);
            AddText(content, "Looking up…", secondary: true);
        }
        else if (state.Status == LearningDictionaryContentStatus.Failed)
        {
            if (state.SelectedSource == DictionarySource.Online)
                AddText(content, "Online English dictionary · https://api.dictionaryapi.dev · Sends only this lookup term", secondary: true);
            AddText(content, state.ErrorMessage ?? "The dictionary could not complete this lookup.", secondary: true);
            if (state.SelectedSource == DictionarySource.Online)
            {
                var retry = new Button { Content = "Retry online", HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = confirmOnline is not null };
                retry.SetResourceReference(FrameworkElement.StyleProperty, "Btn");
                retry.Click += (_, _) => { if (current?.Invoke() != false) confirmOnline?.Invoke(); }; content.Children.Add(retry);
            }
        }
        else if (state.Result is LearningDictionaryResult.Article article)
        {
            AddText(content, article.Text);
            AddText(content, "Rich dictionary display is unavailable.", secondary: true);
        }
        else if (state.Result is LearningDictionaryResult.Website website)
        {
            AddText(content, website.Url.AbsoluteUri, secondary: true);
            AddText(content, "This opens in your browser and sends the lookup term to the website.", secondary: true);
            var link = Link("Open lookup website", website.Url.AbsoluteUri, current);
            if (link is not null) { var block = new TextBlock(); block.Inlines.Add(link); content.Children.Add(block); }
        }
        else if (state.Result is LearningDictionaryResult.Entries entries)
        {
            foreach (var entry in entries.Values)
            {
                if (entry.Headword.Normalize() != state.Query.Normalize()) AddText(content, entry.Headword, bold: true);
                if (entry.Phonetics is { Count: > 0 }) AddText(content, string.Join(" · ", entry.Phonetics), secondary: true);
                foreach (var sense in entry.Senses)
                {
                    AddText(content, sense.PartOfSpeech + (sense.Labels.Count == 0 ? "" : " · " + string.Join(", ", sense.Labels)), secondary: true);
                    for (int index = 0; index < sense.Definitions.Count; index++) AddText(content, $"{index + 1}. {sense.Definitions[index]}");
                }
                foreach (var example in entry.Examples ?? []) AddText(content, example, secondary: true);
                AddLink(content, entry.SourceName ?? "Wiktionary / Kaikki", entry.SourceUrl, current);
                if (entry.LicenseUrl is { } license) AddLink(content, entry.LicenseName ?? "License", license, current);
                else if (entry.LicenseName is { Length: > 0 } name) AddText(content, name, secondary: true);
            }
            var attribution = Link("Wiktionary contributors / Kaikki · CC BY-SA 4.0",
                "https://creativecommons.org/licenses/by-sa/4.0/", current);
            if (!contextual && (state.SelectedSource is null || state.SelectedSource == DictionarySource.Bundled) && attribution is not null)
                attributionText.Inlines.Add(attribution);
            else if (!contextual) attributionText.Text = state.SourceLabels.GetValueOrDefault(state.SelectedSource ?? default, "Dictionary");
        }
        else AddText(content, state.IsExpression
            ? "No dictionary entry was found for this expression. Select a word below to look it up."
            : "No definition for this word was found in the available dictionary.", secondary: true);
    }

    private static void AddText(StackPanel content, string text, bool secondary = false, bool bold = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = secondary ? 11 : 13,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 7) };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextSecondary" : "TextPrimary");
        content.Children.Add(block);
    }
    private static void AddLink(StackPanel content, string title, string value, Func<bool>? current)
    {
        if (Link(title, value, current) is not { } link) return;
        var block = new TextBlock { FontSize = 10, Margin = new Thickness(0, 0, 0, 7), TextWrapping = TextWrapping.Wrap };
        block.Inlines.Add(link); content.Children.Add(block);
    }
    private static Hyperlink? Link(string title, string value, Func<bool>? current = null)
    {
        if (DictionaryLinkPolicy.ExternalUrl(value) is not { } url) return null;
        var link = new Hyperlink(new Run(title)) { Focusable = false, ToolTip = url.AbsoluteUri };
        link.Click += (_, args) =>
        {
            args.Handled = true;
            if (current?.Invoke() == false) return;
            try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
            catch { /* An unavailable browser cannot fault the dictionary session or expose paths. */ }
        };
        return link;
    }
}
