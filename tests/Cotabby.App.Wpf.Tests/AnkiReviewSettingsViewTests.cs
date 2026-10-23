using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Review;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Actual settings component, compiled theme, shared controller/Store and isolated
/// SQLite run on STA. Only credentials and Anki calls are synthetic. Routed events prove wiring,
/// not physical keyboard focus, external Anki activation or native credential integration.</summary>
[Collection("WPF views")]
public sealed class AnkiReviewSettingsViewTests(WpfDispatcher dispatcher)
{
    private static Window Window(AnkiReviewSettingsSection section)
    {
        var window = new Window { Width = 520, Height = 700, ShowActivated = false, ShowInTaskbar = false,
            Content = new ScrollViewer { Content = section } };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        return window;
    }
    private static VocabularyStore Store(Action? open = null) => new(new VocabularyArchive(() =>
    { open?.Invoke(); return VocabularySQLiteDatabase.CreateMemory(); }), SynchronizationContext.Current!);
    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!condition()) { Assert.True(DateTime.UtcNow < limit, "Review settings state did not arrive."); await Task.Delay(5); }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject value) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++)
        {
            var child = VisualTreeHelper.GetChild(value, i); if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    [Fact]
    public Task HiddenConstructionDoesNotReadOrLoadAndVisibleLifetimeSavesWithoutDeletingOnClear() => dispatcher.RunAsync(async () =>
    {
        int opens = 0; await using var store = Store(() => Interlocked.Increment(ref opens));
        var client = new AnkiTestClient(); var key = new AnkiTestCredentials(); var settings = new AppSettings();
        await using var feature = new AnkiReviewFeature(() => settings, () => { }, store, client, key);
        var section = new AnkiReviewSettingsSection(feature) { Visibility = Visibility.Collapsed }; var window = Window(section);
        try
        {
            Assert.Equal(0, opens); Assert.Equal(0, key.Reads); window.Show(); window.UpdateLayout();
            Assert.False(section.IsAppeared); Assert.Equal(0, opens); Assert.Equal(0, key.Reads); Assert.Empty(client.Calls);
            section.Visibility = Visibility.Visible; await Until(() => store.IsLoaded); Assert.True(section.IsAppeared);
            Assert.Equal(1, opens); Assert.Equal(1, key.Reads); Assert.Equal("fixture-key", section.KeyBox.Password); Assert.Equal(0, key.Writes);
            section.KeyBox.Password = "  edited key  "; section.Visibility = Visibility.Collapsed;
            Assert.Equal("  edited key  ", key.Key); Assert.Equal(1, key.Writes); Assert.Equal("", section.KeyBox.Password); Assert.Empty(client.Calls);
            section.Visibility = Visibility.Visible; Assert.Equal(2, key.Reads); Assert.Equal("  edited key  ", section.KeyBox.Password);
            section.Close(); section.Close(); Assert.Equal(1, key.Writes); Assert.Equal("", section.KeyBox.Password);
            string address = section.AddressBox.Text; feature.Settings.Address = "http://localhost:8765"; Assert.Equal(address, section.AddressBox.Text);
        }
        finally { section.Close(); window.Close(); }
    });

    [Fact]
    public Task SubmitFocusLossAndHideShareTheChangedKeyGuardAndAddressInvalidatesTheCatalog() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(); var key = new AnkiTestCredentials(); var client = new AnkiTestClient(); var settings = new AppSettings(); int saves = 0;
        await using var feature = new AnkiReviewFeature(() => settings, () => saves++, store, client, key);
        var section = new AnkiReviewSettingsSection(feature); var window = Window(section);
        try
        {
            window.Show(); window.UpdateLayout(); await Until(() => store.IsLoaded);
            section.KeyBox.Password = "submitted";
            section.KeyBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal("submitted", key.Key); Assert.Equal(1, key.Writes);
            section.KeyBox.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, section.KeyBox, section.AddressBox) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.Equal(1, key.Writes);
            section.KeyBox.Password = "focus saved";
            section.KeyBox.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, section.KeyBox, section.AddressBox) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.Equal("focus saved", key.Key); Assert.Equal(2, key.Writes); Assert.Equal(0, saves);
            Click(section.TestButton); await Until(() => feature.Controller.Catalog is not null);
            Assert.Equal("Connected", section.StatusBox.Text); Assert.Equal(Visibility.Visible, section.DeckPicker.Visibility);
            section.AddressBox.Text = "http://localhost:8765"; Assert.Null(feature.Controller.Catalog); Assert.Equal("", section.StatusBox.Text); Assert.Equal(1, saves);
            Assert.Equal(Visibility.Visible, section.DeckBox.Visibility); section.DeckBox.Text = "Manual deck"; Assert.Equal("Manual deck", feature.Settings.Deck);
            section.Visibility = Visibility.Collapsed; Assert.Equal(2, key.Writes);
        }
        finally { section.Close(); window.Close(); }
    });

    [Fact]
    public Task TestBlocksOnKeyErrorThenSavesBeforeRequestsAndKeepsSelectedDeckInCatalog() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(); var key = new AnkiTestCredentials(); var client = new AnkiTestClient { Decks = ["Other", "Default", "café", "Other"] }; var settings = new AppSettings();
        await using var feature = new AnkiReviewFeature(() => settings, () => { }, store, client, key);
        feature.Settings.Model = "Legacy custom"; feature.Settings.Deck = "Retained deck";
        var section = new AnkiReviewSettingsSection(feature); var window = Window(section);
        try
        {
            window.Show(); window.UpdateLayout(); await Until(() => store.IsLoaded);
            section.KeyBox.Password = "replacement"; key.FailWrite = true; Click(section.TestButton);
            Assert.Equal("fixture write failure", section.StatusBox.Text); Assert.Empty(client.Calls);
            key.FailWrite = false; Click(section.TestButton); await Until(() => feature.Controller.Catalog is not null);
            Assert.Equal("replacement", key.Key); Assert.All(client.Calls, c => Assert.Equal("replacement", c.Key));
            Assert.Equal(ReviewLayoutPolicy.Model, Assert.Single(client.Calls, c => c.Action == "modelFieldNames").Parameters["modelName"].String);
            Assert.Equal("Legacy custom", feature.Settings.Model); Assert.Equal(new[] { "Default", "Other", "Retained deck", "café" }, section.DeckPicker.Items.Cast<string>());

            Assert.Equal("Retained deck", section.DeckPicker.SelectedItem);
            feature.Settings.Deck = "cafe\u0301";
            Assert.Equal("café", section.DeckPicker.SelectedItem); Assert.Equal("cafe\u0301", feature.Settings.Deck); Assert.Equal(4, section.DeckPicker.Items.Count);
            section.DeckPicker.SelectedItem = "Other"; Assert.Equal("Other", feature.Settings.Deck);
        }
        finally { section.Close(); window.Close(); }
    });

    [Fact]
    public Task BusyStateDisablesDuplicateTestAndFeatureShutdownCancelsItBeforeStoreClose() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(); var client = new AnkiTestClient { ProfileGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var settings = new AppSettings(); var feature = new AnkiReviewFeature(() => settings, () => { }, store, client, new AnkiTestCredentials());
        var section = new AnkiReviewSettingsSection(feature); var window = Window(section);
        try
        {
            window.Show(); window.UpdateLayout(); await Until(() => store.IsLoaded);
            feature.Controller.Begin(Guid.Empty); Assert.False(section.TestButton.IsEnabled); Click(section.TestButton); Assert.Empty(client.Calls); feature.Controller.Finish(Guid.Empty);
            Assert.True(section.TestButton.IsEnabled); Click(section.TestButton); Assert.True(feature.Controller.Testing); Assert.False(section.TestButton.IsEnabled);
            Click(section.TestButton); Assert.Single(client.Calls); section.Close();
            await feature.DisposeAsync(); Assert.Equal(1, client.Disposals); Assert.False(feature.Controller.Testing); Assert.True(store.IsLoaded);
        }
        finally { section.Close(); window.Close(); await feature.DisposeAsync(); }
    });

    [Fact]
    public Task FailedHideSaveAndFailedReappearanceReadRetainTheTypedDraftForRetry() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(); var key = new AnkiTestCredentials(); var settings = new AppSettings();
        await using var feature = new AnkiReviewFeature(() => settings, () => { }, store, new AnkiTestClient(), key);
        var section = new AnkiReviewSettingsSection(feature); var window = Window(section);
        try
        {
            window.Show(); window.UpdateLayout(); await Until(() => store.IsLoaded);
            section.KeyBox.Password = "unsaved draft"; key.FailWrite = true; section.Visibility = Visibility.Collapsed;
            Assert.Equal("fixture-key", key.Key); Assert.Equal("", section.KeyBox.Password); Assert.Equal("fixture write failure", section.StatusBox.Text);
            key.FailRead = true; section.Visibility = Visibility.Visible;
            Assert.Equal("unsaved draft", section.KeyBox.Password); Assert.Equal("fixture read failure", section.StatusBox.Text);
            key.FailWrite = false; section.Visibility = Visibility.Collapsed; Assert.Equal("unsaved draft", key.Key);
            Assert.Equal("", section.StatusBox.Text); Assert.Equal(2, key.Writes);
        }
        finally { section.Close(); window.Close(); }
    });

    [Fact]
    public Task TemplateDeleteUsesAcknowledgedStoreIdentityAndARejectedDeleteKeepsItsRow() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store();
        var template = new ReviewLayoutTemplate(Guid.NewGuid(), "Fixture template", new()); await store.SaveTemplateAsync(template);
        var settings = new AppSettings(); await using var feature = new AnkiReviewFeature(() => settings, () => { }, store, new AnkiTestClient(), new AnkiTestCredentials());
        var section = new AnkiReviewSettingsSection(feature); var window = Window(section);
        try
        {
            window.Show(); window.UpdateLayout(); Assert.Single(store.ReviewTemplates);
            Click(Assert.Single(Descendants<Button>(section.TemplatesView))); await Until(() => store.ReviewTemplates.Count == 0); Assert.Empty(Descendants<Button>(section.TemplatesView));
            await store.SaveTemplateAsync(template); await Until(() => Descendants<Button>(section.TemplatesView).Any());
            await store.DisposeAsync(); Click(Assert.Single(Descendants<Button>(section.TemplatesView))); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(template.Id, Assert.Single(store.ReviewTemplates).Id); Assert.Single(Descendants<Button>(section.TemplatesView));
        }
        finally { section.Close(); window.Close(); }
    });
}
