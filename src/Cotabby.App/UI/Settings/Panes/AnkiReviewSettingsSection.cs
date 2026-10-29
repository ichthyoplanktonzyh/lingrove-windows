using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Cotabby.App.Review;
using Cotabby.App.UI.Controls;
using Cotabby.Core.Review;
using Cotabby.Core.Text;

namespace Cotabby.App.UI.Settings.Panes;

/// <summary>Windows grouped-form adapter of the original LearningReviewSettingsSection. One
/// settings window owns this presentation; the app feature/Store remain shared. Visibility owns
/// key reads/focus saves, while only explicit Test or template-delete actions start owned work.
/// Constructing hidden panes must never read a credential or load the notebook.</summary>
internal sealed class AnkiReviewSettingsSection : StackPanel
{
    private readonly AnkiReviewFeature _feature;
    private readonly AnkiReviewSettingsInteraction _interaction;
    private readonly StackPanel _templates = new();
    private readonly TextBlock _profile = Ui.Text("");
    private readonly FrameworkElement _profileRow;
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Width = 48, Height = 3 };
    private bool _refreshing, _appeared, _closed;
    private string _observedAddress;
    internal TextBox AddressBox { get; } = new TextBox().Styled("Input");
    internal PasswordBox KeyBox { get; } = new PasswordBox().Styled("PasswordInput");
    internal Button TestButton { get; } = new Button { Content = "Test connection" }.Styled("Btn");
    internal TextBox StatusBox { get; } = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent };
    internal TextBox DeckBox { get; } = new TextBox().Styled("Input");
    internal ComboBox DeckPicker { get; } = new ComboBox().Styled("Combo");
    internal FrameworkElement TemplatesView => _templates;
    internal bool IsAppeared => _appeared;

    internal AnkiReviewSettingsSection(AnkiReviewFeature feature)
    {
        _feature = feature; _interaction = new(feature); _observedAddress = feature.Settings.Address;
        Margin = new(0, 8, 0, 10);
        Children.Add(Row("AnkiConnect address", AddressBox));
        Children.Add(Row("API key (optional)", KeyBox));
        Children.Add(Ui.Across(TestButton, _progress));
        StatusBox.Margin = new(0, 6, 0, 6); AutomationProperties.SetName(StatusBox, "Anki connection status"); Children.Add(StatusBox);
        _profileRow = Row("Anki profile", _profile); Children.Add(_profileRow);
        var defaults = Ui.Text("Default cards", "CardHeader"); defaults.Margin = new(0, 14, 0, 2); Children.Add(defaults);
        var deck = new Grid { Children = { DeckBox, DeckPicker } }; Children.Add(Row("Deck", deck));
        Children.Add(Row("Note type", Ui.Text(ReviewLayoutPolicy.Model)));
        var templates = Ui.Text("Card templates", "CardHeader"); templates.Margin = new(0, 14, 0, 2); Children.Add(templates); Children.Add(_templates);
        AddressBox.TextChanged += (_, _) => { if (!_refreshing && _appeared) feature.Settings.Address = AddressBox.Text; };
        KeyBox.PasswordChanged += (_, _) => { if (!_refreshing && _appeared) _interaction.Key = KeyBox.Password; };
        KeyBox.LostKeyboardFocus += (_, _) => { if (_appeared) _interaction.SaveKey(); };
        KeyBox.KeyDown += (_, e) => { if (e.Key == Key.Enter && _appeared) { _interaction.SaveKey(); e.Handled = true; } };
        DeckBox.TextChanged += (_, _) => { if (!_refreshing && _appeared) feature.Settings.Deck = DeckBox.Text; };
        DeckPicker.SelectionChanged += (_, _) => { if (!_refreshing && _appeared && DeckPicker.SelectedItem is string value) feature.Settings.Deck = value; };
        TestButton.Click += async (_, _) =>
        {
            if (!_appeared || !TestButton.IsEnabled) return;
            try { await _interaction.TestConnectionAsync(); }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { }
            Refresh();
        };
        Loaded += (_, _) => ObserveVisibility();
        IsVisibleChanged += (_, _) => ObserveVisibility();
        Unloaded += (_, _) => EndAppearance();
        feature.Controller.PropertyChanged += OnControllerChanged;
        feature.Settings.PropertyChanged += OnSettingsChanged;
        if (feature.Controller.StoreOrNull is { } store) store.Changed += Refresh;
        _interaction.PropertyChanged += OnControllerChanged;
        Refresh();
    }
    private static FrameworkElement Row(string label, FrameworkElement control)
    {
        AutomationProperties.SetName(control, label); control.Margin = new(0, 3, 0, 5);
        return new StackPanel { Children = { Ui.Text(label, "Caption"), control } };
    }
    private void ObserveVisibility()
    {
        if (_closed) return;
        if (IsLoaded && IsVisible)
        {
            if (_appeared) return;
            _appeared = true;
            if (_feature.Controller.StoreOrNull is not null) _ = LoadStore();
            _interaction.ReadKeyOnAppearance();
            Refresh();
        }
        else EndAppearance();
    }
    private async Task LoadStore()
    {
        try { await _feature.RunAsync(_ => _feature.Controller.Store.LoadAsync()); }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { }
    }
    private void EndAppearance()
    {
        if (!_appeared) return;
        _interaction.SaveKey(); _appeared = false;
        // Clear the masked control under its event guard, but retain the original @State-like
        // draft across hides. If Save and the next Read both fail, the user can still retry it.
        // Destroying that draft on hide would silently lose the source's failure recovery.
        _refreshing = true;
        try { KeyBox.Clear(); }
        finally { _refreshing = false; }
    }
    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        string address = _feature.Settings.Address;
        if (!CanonicalStringComparer.Instance.Equals(_observedAddress, address))
        { _observedAddress = address; _feature.Controller.InvalidateConnection(); }
        Refresh();
    }
    private void Refresh()
    {
        if (_closed || _refreshing) return;
        _refreshing = true;
        try
        {
            var settings = _feature.Settings; var controller = _feature.Controller;
            if (!AddressBox.IsKeyboardFocused) AddressBox.Text = settings.Address;
            if (_appeared && !KeyBox.IsKeyboardFocused && KeyBox.Password != _interaction.Key) KeyBox.Password = _interaction.Key;
            TestButton.IsEnabled = !controller.Testing && controller.SendingSenseId is null;
            _progress.Visibility = controller.Testing ? Visibility.Visible : Visibility.Collapsed;
            StatusBox.Text = _interaction.KeyError ?? controller.ConnectionMessage ?? "";
            _profile.Text = controller.Catalog?.Profile ?? "";
            _profileRow.Visibility = controller.Catalog is null ? Visibility.Collapsed : Visibility.Visible;
            if (!DeckBox.IsKeyboardFocused) DeckBox.Text = settings.Deck;
            DeckBox.Visibility = controller.Catalog is null ? Visibility.Visible : Visibility.Collapsed;
            DeckPicker.Visibility = controller.Catalog is null ? Visibility.Collapsed : Visibility.Visible;
            if (controller.Catalog is { } catalog)
            {
                DeckPicker.ItemsSource = catalog.Decks.Append(settings.Deck).Distinct(CanonicalStringComparer.Instance).Order(CanonicalStringComparer.Instance).ToArray();
                DeckPicker.SelectedItem = DeckPicker.Items.Cast<string>().First(value => CanonicalStringComparer.Instance.Equals(value, settings.Deck));
            }
            _templates.Children.Clear();
            if (controller.StoreOrNull is not { } store)
            {
                _templates.Children.Add(new TextBlock { Text = AnkiReviewController.NotebookUnavailableMessage, TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.SystemColors.GrayTextBrush, Margin = new Thickness(0, 4, 0, 4) });
            }
            else foreach (var template in store.ReviewTemplates)
            {
                var delete = new Button { Content = "Delete" }.Styled("Btn"); AutomationProperties.SetName(delete, "Delete template " + template.Name);
                delete.Click += async (_, _) =>
                {
                    try { await _feature.RunAsync(_ => controller.Store.RemoveTemplateAsync(template.Id)); }
                    catch { /* Original settings uses try?: only acknowledged Store state removes the row. */ }
                };
                var name = Ui.Text(template.Name); name.VerticalAlignment = VerticalAlignment.Center;
                var row = new DockPanel { Margin = new(0, 3, 0, 3), LastChildFill = true };
                DockPanel.SetDock(delete, Dock.Right); row.Children.Add(delete); row.Children.Add(name); _templates.Children.Add(row);
            }
        }
        finally { _refreshing = false; }
    }
    internal void Close()
    {
        if (_closed) return;
        EndAppearance(); _closed = true; _interaction.ClearEphemeralKey();
        _feature.Controller.PropertyChanged -= OnControllerChanged;
        _feature.Settings.PropertyChanged -= OnSettingsChanged;
        if (_feature.Controller.StoreOrNull is { } store) store.Changed -= Refresh;
        _interaction.PropertyChanged -= OnControllerChanged;
    }
}
