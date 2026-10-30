using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using Cotabby.App.Localization;
using Cotabby.App.Settings;
using Cotabby.App.UI;
using Cotabby.App.UI.Settings;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Settings;
using Cotabby.Win32.Localization;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class SettingsSearchTests(WpfDispatcher dispatcher)
{
    [Fact]
    public void RealIcuFoldingAndScoringUseTheSameMacSourceCases()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "settings-search-3b-v1.json")));
        foreach (var row in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            var items = row.GetProperty("items").EnumerateArray().Select(item => new SettingsSearchItem<string>(
                item.GetProperty("id").GetString()!, item.GetProperty("title").GetString()!,
                item.GetProperty("keywords").EnumerateArray().Select(value => value.GetString()!),
                item.GetProperty("group").GetString()!, item.GetProperty("summary").GetString()!)).ToArray();
            var actual = SettingsSearchRanker.Matches(row.GetProperty("query").GetString()!, items, new WindowsSettingsSearchFolding(row.GetProperty("locale").GetString()!));
            var expected = row.GetProperty("expected").EnumerateArray().Select(value => (Id: value.GetProperty("id").GetString(), Score: value.GetProperty("score").GetDouble())).ToArray();
            Assert.True(expected.SequenceEqual(actual.Select(value => ((string?)value.Value, value.Score))), row.GetProperty("id").GetString() + ": " + string.Join(", ", actual));
        }
        foreach (var row in json.RootElement.GetProperty("folds").EnumerateArray())
        {
            string actual = new WindowsSettingsSearchFolding(row.GetProperty("locale").GetString()!).Fold(row.GetProperty("text").GetString()!);
            Assert.True(row.GetProperty("expected").GetString()!.Normalize() == actual, row.GetProperty("id").GetString() + ": " + actual);
        }
    }
    [Fact]
    public Task LocalizedCatalogRanksAliasesAndExcludesRuntimePathContent() => dispatcher.RunAsync(() =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans");
            var language = new SettingsRow("Interface language", "General", new TextBlock()).Tagged("restart locale");
            var folder = new SettingsRow("Models Folder", "General / private folder", new TextBlock()) { SubtitleIsContent = true };
            var pane = new SettingsPane("General", "", "", new SettingsCard(null, language, folder));
            Assert.Same(language, Assert.Single(SettingsSearchPresentation.Find("界面", [pane])).Row);
            Assert.Same(language, Assert.Single(SettingsSearchPresentation.Find("restart language", [pane])).Row);
            Assert.Empty(SettingsSearchPresentation.Find("private", [pane]));
            Assert.Equal("General / private folder", folder.DisplaySubtitle);
            Assert.Equal("Interface language", language.Title);
            Assert.Equal(2, SettingsSearchPresentation.Find("通用", [pane]).Count);
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task SearchRevealsHiddenRowsWithoutChangingBackendOrDisabledState() => dispatcher.RunAsync(() =>
    {
        var settings = new AppSettings { Engine = EngineKind.Local };
        var callbacks = new List<Action>();
        var field = new TextBox { Text = "unchanged model" };
        var target = new SettingsRow("Model", "Endpoint model", field).VisibleWhen(s => s.Engine == EngineKind.Endpoint).EnabledWhen(_ => false);
        var pane = new SettingsPane("Engine & Model", "", "", new SettingsCard("Endpoint", target).VisibleWhen(s => s.Engine == EngineKind.Endpoint));
        var view = new PaneView(pane, () => settings, callback => { callbacks.Add(callback); callback(); });
        Assert.Equal(Visibility.Collapsed, field.Visibility);
        view.Reveal(target); Assert.Equal(Visibility.Visible, field.Visibility); Assert.False(field.IsEnabled);
        Assert.Equal(EngineKind.Local, settings.Engine); Assert.Equal("unchanged model", field.Text);
        view.ClearReveal(); Assert.Equal(Visibility.Collapsed, field.Visibility);
        settings = new AppSettings { Engine = EngineKind.Endpoint }; foreach (var callback in callbacks) callback();
        Assert.Equal(Visibility.Visible, field.Visibility); Assert.False(field.IsEnabled);
        Assert.Throws<ArgumentException>(() => view.Reveal(new SettingsRow("Foreign", "", new TextBlock())));
        return Task.CompletedTask;
    });
    [Fact]
    public Task ResultIsAKeyboardFocusableAccessibleActionWithOriginalNavigationIdentity() => dispatcher.RunAsync(async () =>
    {
        Window? window = null;
        try
        {
            InterfaceText.Initialize("zh-Hans");
            var row = new SettingsRow("Interface language", "General", new TextBlock());
            var pane = new SettingsPane("General", "", "", new SettingsCard(null, row));
            SettingsPane? selectedPane = null; SettingsRow? selectedRow = null;
            var result = SettingsWindow.BuildSearchResult(pane, row, (p, r) => { selectedPane = p; selectedRow = r; });
            window = new Window { Content = result, Width = 550, Height = 150, ShowActivated = false }; VocabularyNotebookTheme.Follow(window); window.Show(); window.UpdateLayout();
            Assert.True(result.Focusable); Assert.True(result.IsTabStop);
            Assert.Contains("界面语言", AutomationProperties.GetName(result));
            var invoke = Assert.IsAssignableFrom<IInvokeProvider>(new ButtonAutomationPeer(result).GetPattern(PatternInterface.Invoke));
            invoke.Invoke(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Same(pane, selectedPane); Assert.Same(row, selectedRow);
            VocabularyThemeRender.Capture(window, "settings-search-result-zh", 550, 150);
        }
        finally { window?.Close(); InterfaceText.Initialize("en"); }
    });
    [Fact]
    public Task ReturnSubmitsTheTopRankedActionAndEmptyQueriesAreInert() => dispatcher.RunAsync(() =>
    {
        Window? window = null;
        try
        {
            int first = 0, second = 0;
            var field = new TextBox { Text = "model" }; var results = new StackPanel();
            var one = new Button(); one.Click += (_, _) => first++; var two = new Button(); two.Click += (_, _) => second++;
            results.Children.Add(one); results.Children.Add(two);
            window = new Window { Content = field, Width = 250, Height = 100, ShowActivated = false }; window.Show();
            SettingsSearchPresentation.BindSubmission(field, () => results);
            void Return()
            {
                var source = PresentationSource.FromVisual(field)!;
                field.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            }
            Return(); Assert.Equal(1, first); Assert.Equal(0, second);
            field.Text = "\u200b\u00a0 "; Assert.Equal("", SettingsSearchPresentation.Query(field.Text)); Return(); Assert.Equal(1, first);
            field.Text = "no matches"; results.Children.Clear(); Return(); Assert.Equal(1, first);
            Assert.Equal("no matches", field.Text);
        }
        finally { window?.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task ArrivalRetiresStaleWorkAndExpiresWithoutHidingTheRevealedRow() => dispatcher.RunAsync(async () =>
    {
        Window? window = null;
        try
        {
            var settings = new AppSettings { Engine = EngineKind.Local };
            var row = new SettingsRow("Endpoint", "", new TextBox { Text = "unchanged" }).VisibleWhen(_ => false);
            var pane = new SettingsPane("Engine & Model", "", "", new SettingsCard("Endpoint", row));
            var view = new PaneView(pane, () => settings, callback => callback());
            window = new Window { Content = view, Width = 550, Height = 300, ShowActivated = false }; VocabularyNotebookTheme.Follow(window); window.Show();
            view.Reveal(row); view.ClearReveal();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Null(view.HighlightedRow); Assert.Equal(Visibility.Collapsed, row.View.Visibility);
            view.Reveal(row);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Same(row, view.HighlightedRow);
            VocabularyThemeRender.Capture(window, "settings-search-arrival", 550, 300);
            var elapsed = Stopwatch.StartNew();
            while (view.HighlightedRow is not null && elapsed.Elapsed < TimeSpan.FromSeconds(6)) await Task.Delay(25);
            Assert.Null(view.HighlightedRow); Assert.Equal(Visibility.Visible, row.View.Visibility); Assert.Equal(EngineKind.Local, settings.Engine);
            view.Reveal(row); view.ClearReveal(); window.Close();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Null(view.HighlightedRow);
        }
        finally { window?.Close(); }
    });
}
