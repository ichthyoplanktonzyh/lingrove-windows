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
    internal static void Populate(StackPanel content, TextBlock attributionText, LearningDictionaryState state)
    {
        content.Children.Clear();
        bool contextual = state.Page == LearningDictionaryPage.Contextual;
        if (!contextual) attributionText.Inlines.Clear();
        if (state.Status == LearningDictionaryContentStatus.Loading) AddText(content, "Looking up…", secondary: true);
        else if (state.Status == LearningDictionaryContentStatus.Failed)
            AddText(content, state.ErrorMessage ?? "The dictionary could not complete this lookup.", secondary: true);
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
                AddLink(content, entry.SourceName ?? "Wiktionary / Kaikki", entry.SourceUrl);
                if (entry.LicenseUrl is { } license) AddLink(content, entry.LicenseName ?? "License", license);
                else if (entry.LicenseName is { Length: > 0 } name) AddText(content, name, secondary: true);
            }
            var attribution = Link("Wiktionary contributors / Kaikki · CC BY-SA 4.0",
                "https://creativecommons.org/licenses/by-sa/4.0/");
            if (!contextual && attribution is not null) attributionText.Inlines.Add(attribution);
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
    private static void AddLink(StackPanel content, string title, string value)
    {
        if (Link(title, value) is not { } link) return;
        var block = new TextBlock { FontSize = 10, Margin = new Thickness(0, 0, 0, 7), TextWrapping = TextWrapping.Wrap };
        block.Inlines.Add(link); content.Children.Add(block);
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
}
