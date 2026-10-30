using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Localization;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.UI.Review;
using Cotabby.Core.Review;
using Cotabby.Core.Settings;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalReviewPreparationAlignmentTests(WpfDispatcher dispatcher)
{
    private static AnkiReviewSettings Settings() { var values = new AppSettings(); return new(() => values, () => { }, new AnkiTestCredentials()); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Ready(CanonicalAnkiReviewWindow window) { if (window.LastOperation is { } operation) await operation; await window.PreviewOperation; }
    private static ReviewLayout Layout() => new(
        [ReviewCardPart.Create(ReviewContentSource.Custom) with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("Close")))] }],
        [ReviewCardPart.Create(ReviewContentSource.Custom) with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("General")))] }]);

    [Fact]
    public Task DefaultSavedLayoutReopensPreviewsAndSendsWithoutChangingItsIdentityOrModel() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-default-layout-" + Guid.NewGuid().ToString("N"));
        CanonicalAnkiReviewWindow? window = null;
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money", Example: "bank opens")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var sense = entry.Senses[0];
            var plan = ReviewPlan.Create() with { Layout = Layout(), ExampleId = sense.Examples[0].Id };
            await store.SaveReviewAsync(entry.Id, sense.Id, new F.VocabularyReview(plan), null);
            var server = new SessionTestServer();
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, server), store, entry.Id, sense.Id);
            window = new(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            Assert.Null(session.CardId); Assert.Equal(plan.Id, session.Plan.Id); Assert.Equal(AnkiReviewPolicy.PresetName, session.Model);
            Assert.Contains(window.PreviewPanel.Children.OfType<TextBlock>(), block => block.Text == "Close");
            Assert.DoesNotContain(window.PreviewPanel.Children.OfType<TextBlock>(), block => block.Text.Contains("bank opens", StringComparison.Ordinal));
            window.SideBox.SelectedIndex = 1; await window.PreviewOperation;
            Assert.Contains(window.PreviewPanel.Children.OfType<TextBlock>(), block => block.Text == "General");
            Assert.True(window.SendButton.IsEnabled); Click(window.SendButton); await Ready(window); await window.RetireAsync();
            Assert.Equal(1, server.AddCount);
            var saved = (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0];
            Assert.Equal(plan.Id, saved.Review!.Plan.Id); Assert.Equal(plan.Layout, saved.Review.Plan.Layout); Assert.Empty(saved.ReviewCards ?? []);
            Assert.Equal(sense.Id, saved.Review.ActiveBinding!.OriginSenseId);
            using var result = JsonDocument.Parse(JsonSerializer.Serialize(server.Snapshot()));
            var note = result.RootElement.GetProperty("notes")[0]; Assert.Equal(AnkiReviewPolicy.PresetName, note.GetProperty("model").GetString());
            var fields = note.GetProperty("fields"); Assert.Contains("Close", fields.GetProperty("Expression").GetString()); Assert.Contains("General", fields.GetProperty("Meaning").GetString());
        }
        finally { if (window is not null) await window.RetireAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task PreservedLayoutRestoresAsAnIsolatedDraftAndCommitsOnlyAfterSave() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-preserved-layout-" + Guid.NewGuid().ToString("N"));
        CanonicalAnkiReviewWindow? window = null;
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var sense = entry.Senses[0];
            var original = ReviewPlan.Create(); var preserved = ReviewPlan.Create() with { Layout = Layout(), Deck = "Other", Answer = "retained" };
            await store.SaveReviewAsync(entry.Id, sense.Id, new F.VocabularyReview(original, preservedPlans: [preserved]), null);
            var server = new SessionTestServer(); var controller = new AnkiReviewController(Settings(), null, server);
            async Task<CanonicalAnkiReviewSession> Open()
            {
                var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry.Id, sense.Id);
                window = new(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
                window.PreservedPlanBox.SelectedIndex = 0; await window.PreviewOperation; return session;
            }
            var cancelled = await Open();
            Assert.NotEqual(original.Id, cancelled.Plan.Id); Assert.NotEqual(preserved.Id, cancelled.Plan.Id);
            Assert.Equal(preserved.Layout, cancelled.Plan.Layout); Assert.Equal("Other", cancelled.Deck);
            Assert.Contains(window!.PreviewPanel.Children.OfType<TextBlock>(), block => block.Text == "Close");
            await window.RetireAsync();
            var unchanged = (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].Review!;
            Assert.Equal(original, unchanged.Plan); Assert.Equal(preserved, Assert.Single(unchanged.PreservedPlans));
            var accepted = await Open(); var expected = accepted.Plan; Click(window!.SaveButton); await Ready(window); await window.RetireAsync();
            var saved = (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].Review!;
            Assert.Equal(expected.Id, saved.Plan.Id); Assert.Equal(preserved.Layout, saved.Plan.Layout); Assert.Equal("Other", saved.Plan.Deck);
            Assert.Equal(preserved, Assert.Single(saved.PreservedPlans)); Assert.Equal(0, server.AddCount);
        }
        finally { if (window is not null) await window.RetireAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task ReviewChromeReusesMacLabelsWhileAuthoredNamesAndFormatArgumentsRemainExact() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-locale-layout-" + Guid.NewGuid().ToString("N"));
        CanonicalAnkiReviewWindow? window = null;
        try
        {
            InterfaceText.Initialize("zh-Hans");
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "Close", Meaning: "General")]);
            var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(Settings(), null, new SessionTestServer()), store, entry.Id, entry.Senses[0].Id, Guid.NewGuid());
            session.Plan = session.Plan with { Name = "General" };
            window = new(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            Assert.Equal("准备复习", window.Title); Assert.Equal("General", window.CardNameBox.Text);
            Assert.Equal(new[] { "正面", "背面" }, window.SideBox.Items.Cast<string>());
            Assert.Equal("卡片正反面", System.Windows.Automation.AutomationProperties.GetName(window.SideBox));
            VocabularyThemeRender.Capture(window, "canonical-review-localized", 700, 710);
            Assert.Contains(window.AddPartChoice.Items.Cast<object>(), choice => choice.ToString() == "核心含义");
            Assert.Contains(window.PreviewPanel.Children.OfType<TextBlock>(), block => block.Text == "Close");
            foreach (var locale in InterfaceLanguagePolicy.Supported)
            {
                InterfaceText.Initialize(locale.Code);
                string label = InterfaceText.Get("Template saved.", "Vocabulary"); Assert.False(string.IsNullOrWhiteSpace(label));
                if (locale.Code != "en") Assert.NotEqual("Template saved.", label);
                const string destination = "Cotabby {General} / https://localhost:1234";
                string disclosure = InterfaceText.Format("Speech for review audio: {0}", destination);
                Assert.Contains(destination, disclosure); Assert.DoesNotContain("{0}", disclosure);
            }
        }
        finally { if (window is not null) await window.RetireAsync(); InterfaceText.Initialize("en"); if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
