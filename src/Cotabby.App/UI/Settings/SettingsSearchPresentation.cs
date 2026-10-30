using Cotabby.App.Localization;
using Cotabby.Core.Settings;
using Cotabby.Win32.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cotabby.Core.Text;

namespace Cotabby.App.UI.Settings;

internal sealed record SettingsSearchHit(SettingsPane Pane, SettingsRow Row, double Score);

/// <summary>The window's existing authored catalog becomes detached search values. The portable
/// ranker sees integer navigation identities, not controls, settings, endpoint or notebook data.
/// English source copy remains an alias only when it differs from the localized visible copy.</summary>
internal static class SettingsSearchPresentation
{
    internal static string Query(string value) => FoundationText.TrimWhitespaceAndNewlines(value);
    /// <summary>Bind once to the window-owned field. Return submits the already-ranked first
    /// result; an empty/unavailable list is inert and keeps the user's query in place.</summary>
    internal static void BindSubmission(TextBox field, Func<Panel?> results)
    {
        field.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            if (Query(field.Text).Length != 0) results()?.Children.OfType<Button>().FirstOrDefault()?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        };
    }
    internal static IReadOnlyList<SettingsSearchHit> Find(string query, IReadOnlyList<SettingsPane> panes)
    {
        var rows = panes.SelectMany(pane => pane.Cards.SelectMany(card => card.Rows)
            .Where(row => row.Title.Length > 0).Select(row => (Pane: pane, Row: row))).ToArray();
        var values = rows.Select((value, index) => Item(index, value.Row, InterfaceText.Get(value.Pane.Title))).ToArray();
        return SettingsSearchRanker.Matches(query, values, new WindowsSettingsSearchFolding(InterfaceText.Locale))
            .Select(match => new SettingsSearchHit(rows[match.Value].Pane, rows[match.Value].Row, match.Score)).ToArray();
    }
    internal static bool Matches(SettingsRow row, string query) => SettingsSearchRanker.Matches(query,
        new[] { Item(0, row, "") }, new WindowsSettingsSearchFolding(InterfaceText.Locale)).Count != 0;
    private static SettingsSearchItem<int> Item(int identity, SettingsRow row, string group)
    {
        var keywords = row.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (row.DisplayTitle != row.Title) keywords.Add(row.Title);
        if (!row.SubtitleIsContent && row.DisplaySubtitle != row.Subtitle) keywords.Add(row.Subtitle);
        return new(identity, row.DisplayTitle, keywords, group, row.SubtitleIsContent ? "" : row.DisplaySubtitle);
    }
}
