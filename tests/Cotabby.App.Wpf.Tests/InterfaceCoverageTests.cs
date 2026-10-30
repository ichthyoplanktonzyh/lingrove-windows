using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Localization;
using Cotabby.App.Learning;
using Cotabby.App.UI.Learning;
using Cotabby.App.UI.Reading;
using Cotabby.App.UI.Settings;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
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
    [Fact]
    public Task DynamicWritingStatesRemainLocalizedAndKeepCapturedTextLiteral() => dispatcher.RunAsync(() =>
    {
        LearningPreviewWindow? window = null;
        try
        {
            InterfaceText.Initialize("zh-Hans"); window = new LearningPreviewWindow();
            var state = LearningViewProbe.Preview with { Source = "General {0} Cotabby", Status = LearningPreviewStatus.Loading,
                PartialResult = "Translation {0}", Result = null };
            window.ShowState(state, LearningViewProbe.Anchor, false);
            Assert.Equal(state.Source, LearningViewProbe.Find<TextBlock>(window, "SourceText").Text);
            Assert.Equal("正在翻译…", LearningViewProbe.Find<TextBlock>(window, "StatusText").Text);
            Assert.Equal("取消", LearningViewProbe.Find<Button>(window, "CancelButton").Content);
            Assert.Equal(InterfaceText.Get("Sentence translation"), LearningViewProbe.Find<TextBlock>(window, "TitleText").Text);
            Assert.Equal(InterfaceText.Get("Collapse"), LearningViewProbe.Find<Button>(window, "ExpandButton").ToolTip);
            Assert.DoesNotContain("TRANSLATION", LearningViewProbe.Find<TextBlock>(window, "ResultLabel").Text);
            var ready = state with { Status = LearningPreviewStatus.Ready, Result = "Literal {0} Cotabby" };
            window.ShowState(ready, LearningViewProbe.Anchor, false);
            window.ShowReplacementState(true, false, null);
            Assert.Equal(InterfaceText.Get("Replace"), System.Windows.Automation.AutomationProperties.GetName(LearningViewProbe.Find<Button>(window, "ReplaceButton")));
            window.ShowExplanationState(ready.RequestId, LearningViewProbe.Explanation("General {0}") with { IsGenerating = true }, true, LearningViewProbe.Anchor);
            Assert.Equal(InterfaceText.Get("Explaining…"), LearningViewProbe.Find<TextBlock>(window, "ExplanationStatus").Text);
            Assert.Equal(InterfaceText.Get("Stop generating explanation"), LearningViewProbe.Find<Button>(window, "ExplanationButton").ToolTip);
            var copy = new LearningPreviewCopyRequest(ready, LearningPreviewCopyKind.Translation);
            window.ShowCopyOutcome(copy, false);
            Assert.Equal(InterfaceText.Get("Could not copy. The clipboard may be busy; try again."), LearningViewProbe.Find<TextBlock>(window, "CopyStatus").Text);
            window.ShowCopyOutcome(copy, true);
            Assert.Equal(InterfaceText.Get("Copied"), LearningViewProbe.Find<TextBlock>(window, "CopyStatus").Text);
            window.ShowExpanded(false); window.ShowExpanded(true);
            Assert.Equal(InterfaceText.Get("Sentence translation"), LearningViewProbe.Find<TextBlock>(window, "TitleText").Text);
            window.ShowCaptureFailure("General {0} · provider detail", LearningViewProbe.Anchor);
            Assert.Equal("未发送任何文本。", LearningViewProbe.Find<TextBlock>(window, "StatusText").Text);
            Assert.Equal("General {0} · provider detail", LearningViewProbe.Find<TextBlock>(window, "ErrorText").Text);
            VocabularyThemeRender.Capture(window, "learning-localized-failure-zh", 520, 350);
        }
        finally { window?.Close(); InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ArabicCardChromeKeepsEndpointValuesAndStudyTextDirectionsIndependent() => dispatcher.RunAsync(() =>
    {
        LearningPreviewWindow? window = null;
        try
        {
            InterfaceText.Initialize("ar"); window = new LearningPreviewWindow();
            var state = LearningViewProbe.Preview with { Source = "Original {0} Cotabby", Result = "نص عربي",
                Options = new() { SourceLanguage = LearningLanguage.English, TargetLanguage = LearningLanguage.Arabic } };
            window.ShowState(state, LearningViewProbe.Anchor, false);
            const string destination = "https://owned.invalid/v1 · Model {0} Cotabby";
            window.ShowTranslationDestination(destination);
            Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
            Assert.Equal(FlowDirection.LeftToRight, LearningViewProbe.Find<TextBlock>(window, "SourceText").FlowDirection);
            Assert.Equal(FlowDirection.RightToLeft, LearningViewProbe.Find<TextBlock>(window, "ResultText").FlowDirection);
            var translatedDestination = LearningViewProbe.Find<TextBlock>(window, "TranslationDestination");
            Assert.Equal(destination, translatedDestination.Text); Assert.Equal(FlowDirection.LeftToRight, translatedDestination.FlowDirection);
            Assert.Equal(FlowDirection.LeftToRight, LearningViewProbe.Find<TextBlock>(window, "ExplanationDestination").FlowDirection);
            window.UpdateLayout();
            var card = LearningViewProbe.Find<Border>(window, "Card");
            var bounds = card.TransformToAncestor(window).TransformBounds(new Rect(card.RenderSize));
            Assert.InRange(bounds.Left, -1, window.ActualWidth);
            Assert.InRange(bounds.Right, 0, window.ActualWidth + 1);
            VocabularyThemeRender.Capture(window, "learning-localized-ar", (int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight));
        }
        finally { window?.Close(); InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TypedDestinationCopyLocalizesWithoutInterpretingModelValuesOrPublishingSecrets() => dispatcher.RunAsync(() =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans"); const string model = "General {0} Cotabby";
            string destination = LearningModelDestinationText.TranslationSettings("https://owned.invalid/v1", model, false, true);
            Assert.Contains(model, destination); Assert.Contains("https://owned.invalid/v1", destination);
            Assert.Contains("端点发送已关闭", destination); Assert.Contains("已启用草稿参考", destination);
            Assert.DoesNotContain("owned-secret", LearningModelDestinationText.TranslationSettings("https://name:owned-secret@owned.invalid/?key=owned-secret", model, true, false));
            Assert.DoesNotContain("Public endpoints", EndpointInterfaceText.Error(OpenAICompatibleEndpointError.InsecurePublicHttp));
            Assert.Null(EndpointInterfaceText.Privacy(OpenAICompatibleHostScope.Loopback));
            Assert.DoesNotContain("outside your PC", EndpointInterfaceText.Privacy(OpenAICompatibleHostScope.PublicInternet)!);
            var unconfigured = new LearningModelSnapshot { Backend = LearningModelBackend.Endpoint, BaseUrl = "http://127.0.0.1:8888/v1", EndpointModel = "" };
            Assert.Null(LearningModelDestinationText.Endpoint(unconfigured));
            Assert.Contains("127.0.0.1", LearningModelDestinationText.Explanation(unconfigured, allowEmptyModel: true));
            Assert.Contains("本机", LearningModelDestinationText.Explanation(unconfigured, allowEmptyModel: true));
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

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
            var expected = TemplateSlots(item.Name);
            foreach (var locale in InterfaceLanguagePolicy.Supported)
            {
                Assert.True(item.Value.GetProperty("localizations").TryGetProperty(locale.Code, out var translation), item.Name + " / " + locale.Code);
                string value = translation.GetProperty("stringUnit").GetProperty("value").GetString()!;
                Assert.False(string.IsNullOrWhiteSpace(value), item.Name + " / " + locale.Code);
                Assert.Equal(expected, TemplateSlots(value));
                if (item.Name.Contains("{query}", StringComparison.Ordinal)) Assert.Single(Regex.Matches(value, Regex.Escape("{query}")).Cast<Match>());
            }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EverySupplementTemplateFormatsInEveryLocaleAndPreviewMarkersKeepTheirBoundary() => dispatcher.RunAsync(() =>
    {
        try
        {
            using var stream = typeof(InterfaceText).Assembly.GetManifestResourceStream("Cotabby.InterfaceLanguage.Windows.xcstrings")!;
            using var catalog = JsonDocument.Parse(stream);
            foreach (var locale in InterfaceLanguagePolicy.Supported)
            {
                InterfaceText.Initialize(locale.Code);
                foreach (var item in catalog.RootElement.GetProperty("strings").EnumerateObject())
                {
                    if (item.Name.Contains("{query}", StringComparison.Ordinal)) continue;
                    string rendered = InterfaceText.Format(item.Name, Enumerable.Repeat<object?>(17, 8).ToArray());
                    Assert.DoesNotContain("%@", rendered); Assert.DoesNotContain("%lld", rendered);
                    Assert.DoesNotMatch(@"\{\d+(?::[^}]+)?\}", rendered);
                }
                var sentence = InterfaceText.Get("This is how your¦ suggestions will look", "Localizable");
                var split = new CompletionPreviewText(sentence);
                Assert.Equal(1, sentence.Count(c => c == '¦'));
                Assert.Equal(sentence.Replace("¦", "", StringComparison.Ordinal), split.TypedText + split.GhostText);
            }
        }
        finally { InterfaceText.Initialize("en"); }
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
    private static string[] TemplateSlots(string template)
    {
        var slots = Regex.Matches(template, @"\{\d+(?::[^}]+)?\}").Select(match => match.Value).ToList();
        int next = 0;
        foreach (Match match in Regex.Matches(template, @"%(?:(\d+)\$)?(?:ll|l)?([@diu]|\.\d+f|f|%)"))
        {
            if (match.Groups[2].Value == "%") continue;
            int index = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1 : next++;
            slots.Add(index.ToString(CultureInfo.InvariantCulture) + ":" + match.Groups[2].Value);
        }
        return slots.Order(StringComparer.Ordinal).ToArray();
    }

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
