using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyReviewSectionTests(WpfDispatcher dispatcher)
{
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var node in Nodes(VisualTreeHelper.GetChild(root, i))) yield return node; }
    private static Button Button(Window owner, string text) { owner.UpdateLayout(); return Nodes(owner).OfType<Button>().Single(b => Equals(b.Content, text)); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static async Task Wait(FrozenVocabularyReviewSection section) { if (section.LastOperation is { } pending) await pending; }
    private static async Task Ready(Cotabby.App.UI.Review.CanonicalAnkiReviewWindow window) { if (window.LastOperation is { } pending) await pending; await window.PreviewOperation; }
    [Fact]
    public Task SectionCreatesEditsAndRemovesIndependentCardThroughSharedOwner() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-section-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => settings, () => { }, null, server, new AnkiTestCredentials());
            var owner = new Window { Width = 700, Height = 500 }; owner.Show(); bool confirmed = false; bool allowed = true;
            var section = new FrozenVocabularyReviewSection(owner, store, feature, () => { }, () => { }, _ => confirmed, (_, _) => allowed);
            owner.Content = section.Render(entry, entry.Senses[0]); var staleAdd = Button(owner, "Add card"); allowed = false; Click(staleAdd); await Wait(section); Assert.Null(section.CardWindow);
            allowed = true; Click(staleAdd); await Wait(section); var cardWindow = Assert.IsType<Cotabby.App.UI.Review.CanonicalAnkiReviewWindow>(section.CardWindow); await Ready(cardWindow);
            cardWindow.CardNameBox.Text = "Stable card"; await cardWindow.PreviewOperation; Click(cardWindow.SaveButton); await Ready(cardWindow); await cardWindow.RetireAsync();
            entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var card = Assert.Single(entry.Senses[0].ReviewCards!); Assert.Null(entry.Senses[0].Review);
            owner.Content = section.Render(entry, entry.Senses[0]); Click(Button(owner, "Edit")); await Wait(section); var edited = Assert.IsType<Cotabby.App.UI.Review.CanonicalAnkiReviewWindow>(section.CardWindow); await Ready(edited); Assert.Equal(card.Plan.Id, edited.CardIdForTest()); await edited.RetireAsync();
            owner.Content = section.Render(entry, entry.Senses[0]); Click(Button(owner, "Remove local card")); await Wait(section); Assert.Single((await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards!);
            confirmed = true; Click(Button(owner, "Remove local card")); await Wait(section); Assert.Empty((await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards!);
            await section.RetireAsync(); owner.Close(); Assert.Equal(0, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}

