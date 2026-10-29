using System.Text.Json;
using Cotabby.App.Localization;
using Cotabby.App.Settings;
using Cotabby.App.Runtime;
using Cotabby.App.Speech;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Core.Speech;
using Cotabby.Core.Suggestions;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using Cotabby.App.UI.Settings;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class InterfaceAndHistoryTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task RestartControlRequiresAnApplicationCallbackAndInvokesItOnlyOnClick() => dispatcher.RunAsync(async () =>
    {
        int calls = 0; Func<Task>? callback = null;
        var panel = new InterfaceLanguageRestartPanel(() => callback); var button = panel.Children.OfType<Button>().Single();
        panel.Update(true); Assert.False(button.IsEnabled); Assert.Equal(0, calls);
        callback = () => { calls++; return Task.CompletedTask; }; panel.Update(true);
        Assert.True(button.IsEnabled); Assert.Equal(0, calls);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Yield(); Assert.Equal(1, calls);
        panel.Update(false); Assert.Equal(Visibility.Collapsed, panel.Visibility);
    });
    [Fact]
    public Task CompletionCollectorBoundsBlockedDispatcherAndRetirementRejectsLateEvents() => dispatcher.RunAsync(async () =>
    {
        var settings = new AppSettings();
        var history = new PerformanceMetricsStore(() => settings, () => { }, (_, _) => Assert.Fail("Unexpected save failure"));
        var collector = new PerformanceHistoryCollector(System.Windows.Threading.Dispatcher.CurrentDispatcher, history, true);
        Task.Run(() =>
        {
            for (int i = 0; i < 150; i++) collector.Observe(new CompletionObservation($"req-{i}", "test model", DateTimeOffset.UnixEpoch, i, ModelRequestOutcome.Completed));
        }).GetAwaiter().GetResult(); // The dispatcher is intentionally blocked during the burst.
        Assert.Empty(history.Entries);
        collector.Dispose();
        Assert.Equal(100, history.Entries.Count);
        Assert.Equal("req-50", history.Entries[0].RequestId);
        Assert.Equal("req-149", history.Entries[^1].RequestId);
        await Task.Run(() => collector.Observe(new CompletionObservation("late", "test model", DateTimeOffset.UnixEpoch, 1, ModelRequestOutcome.Completed)));
        Assert.Equal(100, history.Entries.Count);
    });
    [Fact]
    public Task CatalogUsesSelectedLocaleAndSystemFallbackWithoutChangingStoredValue() => dispatcher.RunAsync(() =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans", "en-US");
            Assert.Equal("zh-Hans", InterfaceText.Locale);
            Assert.Equal("跟随系统", InterfaceText.Get("Follow System"));
            Assert.NotEqual("General", InterfaceText.Get("General"));
            Assert.Equal("Unmatched Windows sentence", InterfaceText.Get("Unmatched Windows sentence"));
            var label = new TextBlock { Text = "General" };
            var input = new TextBox { Text = "General" };
            var bound = new TextBlock(); bound.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = new { Value = "General" } });
            var panel = new StackPanel { Children = { label, input, bound } };
            InterfaceText.TranslateInitialTree(panel);
            Assert.NotEqual("General", label.Text); Assert.Equal("General", input.Text); Assert.Equal("General", bound.Text);
            InterfaceText.Initialize("future-XX", "ja-JP");
            Assert.Equal("future-XX", InterfaceText.LaunchSelection);
            Assert.Equal("ja", InterfaceText.Locale);
            Assert.NotEqual("General", InterfaceText.Get("General"));
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SharedHistoryRecordsOnlyAfterExplicitOptInAndResetsPreferenceNotHistory() => dispatcher.RunAsync(() =>
    {
        var settings = new AppSettings(); int saves = 0;
        var history = new PerformanceMetricsStore(() => settings, () => saves++, (_, _) => Assert.Fail("Unexpected save failure"));
        var events = new List<string>();
        var telemetry = new SpeechTelemetry((_, phase, _) => events.Add(phase), history, () => settings.PerformanceTrackingEnabled);
        var first = new SpeechPlaybackToken(); telemetry.Begin(first, "test model", "system", LearningLanguage.English); telemetry.Finish(first, ModelRequestOutcome.Completed);
        Assert.Empty(history.Entries); Assert.NotEmpty(events);
        settings.PerformanceTrackingEnabled = true;
        var second = new SpeechPlaybackToken(); telemetry.Begin(second, "test model", "system", LearningLanguage.English); telemetry.Finish(second, ModelRequestOutcome.Completed);
        Assert.Single(history.Entries); Assert.Equal(ModelRequestSource.Speech, history.Entries[0].Source); Assert.Equal(1, saves);
        settings = settings.ResetGeneralPreferences(0);
        Assert.False(settings.PerformanceTrackingEnabled);
        Assert.Single(new PerformanceMetricsStore(() => settings, () => { }, (_, _) => Assert.Fail("Unexpected save failure")).Entries);
        return Task.CompletedTask;
    });
}
