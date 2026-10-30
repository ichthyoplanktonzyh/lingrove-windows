using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Localization;
using Cotabby.App.UI.Reading;
using Cotabby.App.UI.Settings;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Focus;
using Cotabby.Core.Settings;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Win32.Localization;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class InterfaceCoverageTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData("en", 1, "one")]
    [InlineData("en", 2, "other")]
    [InlineData("zh-Hans", 1, "other")]
    [InlineData("ja", 0, "other")]
    [InlineData("ko", 1, "other")]
    [InlineData("ru", 1, "one")]
    [InlineData("ru", 2, "few")]
    [InlineData("ru", 5, "many")]
    [InlineData("ru", 11, "many")]
    [InlineData("ru", 21, "one")]
    [InlineData("ar", 0, "zero")]
    [InlineData("ar", 1, "one")]
    [InlineData("ar", 2, "two")]
    [InlineData("ar", 3, "few")]
    [InlineData("ar", 11, "many")]
    [InlineData("ar", 100, "other")]
    [InlineData("fr", 0, "one")]
    [InlineData("fr", 2, "other")]
    [InlineData("hi", 0, "one")]
    [InlineData("id", 1, "other")]
    [InlineData("tr", 1, "one")]
    public void InstalledWindowsIcuSelectsRealCardinalCategories(string locale, int count, string expected) =>
        Assert.Equal(expected, WindowsPluralRules.Cardinal(locale, count));

    [Fact]
    public Task CatalogGrammarFallbackAndEndpointDetailsHaveSeparateAuthorities() => dispatcher.RunAsync(() =>
    {
        try
        {
            InterfaceText.Initialize("ru");
            Assert.Equal("Подключено · 2 модели", EndpointSection.ConnectionSummary(new OpenAICompatibleConnectionState.Ready(2)));
            Assert.Equal("Подключено · 5 моделей", EndpointSection.ConnectionSummary(new OpenAICompatibleConnectionState.Ready(5)));
            Assert.Equal("Connected · 1 model", InterfaceText.QuantityWithCategory("Connected · %lld models", 1, "Localizable", null));
            Assert.Equal("Connected · 2 models", InterfaceText.QuantityWithCategory("Connected · %lld models", 2, "Localizable", null));
            const string remote = "General {0} · Cotabby / not connected";
            Assert.Equal(remote, EndpointSection.ConnectionSummary(new OpenAICompatibleConnectionState.Failed(remote)));
            Assert.Null(WindowsPluralRules.Cardinal("unsupported", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => WindowsPluralRules.Cardinal("en", -1));
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SupplementProvidesEveryLocaleAndPreservesTemplateSlots() => dispatcher.RunAsync(() =>
    {
        using var stream = typeof(InterfaceText).Assembly.GetManifestResourceStream("Cotabby.InterfaceLanguage.Windows.xcstrings")!;
        using var json = JsonDocument.Parse(stream);
        foreach (var item in json.RootElement.GetProperty("strings").EnumerateObject())
        {
            var expected = Regex.Matches(item.Name, @"\{\d+(?::[^}]+)?\}").Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();
            foreach (var locale in InterfaceLanguagePolicy.Supported)
            {
                Assert.True(item.Value.GetProperty("localizations").TryGetProperty(locale.Code, out var translation), item.Name + " / " + locale.Code);
                string value = translation.GetProperty("stringUnit").GetProperty("value").GetString()!;
                Assert.False(string.IsNullOrWhiteSpace(value), item.Name + " / " + locale.Code);
                Assert.Equal(expected, Regex.Matches(value, @"\{\d+(?::[^}]+)?\}").Select(match => match.Value).Order(StringComparer.Ordinal).ToArray());
                if (item.Name.Contains("{query}", StringComparison.Ordinal)) Assert.Single(Regex.Matches(value, Regex.Escape("{query}")).Cast<Match>());
            }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SettingsSearchUsesLocalizedCopyAndKeepsEnglishAliasesAndRawQueries() => dispatcher.RunAsync(() =>
    {
        var culture = CultureInfo.CurrentCulture; var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            InterfaceText.Initialize("zh-Hans");
            var row = new SettingsRow("Interface language", "General", new TextBlock()).Tagged("restart locale");
            Assert.True(row.Matches("界面")); Assert.True(row.Matches("interface")); Assert.True(row.Matches("restart"));
            Assert.Equal("Interface language", row.Title); Assert.Equal("界面语言", row.DisplayTitle);
            const string query = "General {0} Cotabby";
            string empty = InterfaceText.Format("No settings match “%@”", query);
            Assert.Contains(query, empty); Assert.DoesNotContain("No settings match", empty);
            Assert.Equal("en-US", CultureInfo.CurrentCulture.Name); Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);
            InterfaceText.Initialize("fr"); Assert.NotEqual("12,345", InterfaceText.Format("{0:N0}", 12345));
        }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EnumLabelsDoNotReplaceTheTypedSelectionOrInterpretModelNames() => dispatcher.RunAsync(async () =>
    {
        Window? window = null;
        try
        {
            InterfaceText.Initialize("zh-Hans");
            var box = new ComboBox { ItemsSource = Enum.GetValues<VocabularyTextStyle>(), SelectedItem = VocabularyTextStyle.Heading,
                ItemTemplate = InterfaceEnumLabels.Create<VocabularyTextStyle>(value => value.ToString(), "Vocabulary") };
            window = new Window { Content = box, Width = 300, Height = 100, ShowActivated = false };
            VocabularyNotebookTheme.Follow(window); window.Show(); window.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Contains(VisualDescendants(box).OfType<TextBlock>(), label => label.Text == "标题");
            Assert.Equal(VocabularyTextStyle.Heading, box.SelectedItem);
            const string filename = "General {0}.gguf";
            Assert.Contains(filename, ModelDisplayText.Unavailable(filename, Cotabby.Core.Models.LocalModelTask.Translation));
        }
        finally { window?.Close(); InterfaceText.Initialize("en"); }
    });

    [Fact]
    public Task ArabicRegionInstructionsDoNotMirrorThePhysicalCanvas() => dispatcher.RunAsync(() =>
    {
        try
        {
            InterfaceText.Initialize("ar"); using var picker = new ReadingRegionPicker(new ScreenRect(-100, 20, 300, 200));
            var canvas = Assert.IsType<Canvas>(picker.Content);
            var instructions = canvas.Children.OfType<Border>().Single(border => border.Child is TextBlock);
            Assert.Equal(FlowDirection.RightToLeft, instructions.FlowDirection);
            Assert.Equal(FlowDirection.LeftToRight, picker.FlowDirection); Assert.Equal(FlowDirection.LeftToRight, canvas.FlowDirection);
            Assert.DoesNotContain("Select a region", ((TextBlock)instructions.Child).Text);
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    private sealed class NoFiles : IVocabularyWordListReader
    { public Task<VocabularyWordList> ReadAsync(string path, CancellationToken token) => throw new InvalidOperationException("No file reads are authorized by this test."); }
    [Fact]
    public Task CurrentNotebookLocalizesFiltersButRetainsTagQueryAndEntryValues() => dispatcher.RunAsync(async () =>
    {
        FrozenVocabularyNotebookWindow? window = null;
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        try
        {
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "Close", Meaning: "General", Tags: ["General"])]);
            var before = await store.ReadAsync();
            InterfaceText.Initialize("zh-Hans"); window = new(store, new NoFiles()); window.Show(); await window.LastOperation!;
            var query = Descendants(window).OfType<TextBox>().Single(box => box.Name == "frozenNotebookSearch");
            var tags = Descendants(window).OfType<ComboBox>().Single(box => box.Name == "frozenNotebookTag");
            Assert.Equal("搜索生词本", query.Tag);
            Assert.Contains(tags.Items.Cast<object>(), item => item.ToString() == "General");
            tags.SelectedItem = tags.Items.Cast<object>().Single(item => item.ToString() == "General");
            query.Text = "Close"; Assert.Equal("Close", window.Session.Query); Assert.Equal("General", window.Session.Tag);
            Assert.Equal("Close", Assert.Single(window.Session.Visible).Expression);
            VocabularyThemeRender.Capture(window, "interface-notebook-zh", 1060, 680);
            await window.RetireAsync();
            Assert.Equal(before.Revision, (await store.ReadAsync()).Revision);
        }
        finally { if (window is not null) await window.RetireAsync(); InterfaceText.Initialize("en"); }
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in VisualDescendants(child)) yield return nested;
        }
    }
}
