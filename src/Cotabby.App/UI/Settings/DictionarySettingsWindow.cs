using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Cotabby.App.Dictionaries;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Dictionary;
using Microsoft.Win32;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Settings;

/// <summary>Explicit local source management. Import validation runs in the shared service;
/// deleting a row changes settings only, never the selected file.</summary>
internal sealed class DictionarySettingsWindow : Window, IAsyncDisposable
{
    private readonly DictionarySettings _settings; private readonly MDictLibraryService _service;
    private readonly StackPanel _list = new(); private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly Button _import = new() { Content = InterfaceText.Get("Import MDict…", "Learning"), Padding = new(12, 6, 12, 6) };
    private readonly CancellationTokenSource _stop = new(); private readonly HashSet<Task> _work = []; private readonly object _gate = new();
    private readonly ComboBox _definition = new() { Margin = new(0, 4, 0, 10) };
    private bool _updatingDefinition;
    private bool _closed, _busy; private Task? _dispose, _cancellation;
    internal DictionarySettingsWindow(DictionarySettings settings, MDictLibraryService service)
    {
        _settings = settings; _service = service; Title = InterfaceText.Get("Dictionary sources", "Learning"); Width = 650; Height = 560; MinWidth = 480; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI");
        ConfigureTheme(this);
        var body = new StackPanel { Margin = new(20) }; body.Children.Add(new TextBlock { Text = InterfaceText.Get("Dictionary sources", "Learning"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = InterfaceText.Get("Choose source priority and languages. Imported files stay in their original location.", "Learning"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 12) });
        body.Children.Add(new TextBlock { Text = InterfaceText.Get("Preferred definition language", "Learning") });
        _definition.ItemsSource = new[] { "System language" }.Concat(LearningLanguages.All.Select(x => x.Label())).ToArray();
        _definition.SelectionChanged += (_, _) => { if (!_updatingDefinition && _definition.SelectedIndex >= 0) Try(() => _settings.SetDefinitionLanguage(_definition.SelectedIndex == 0 ? null : LearningLanguages.All[_definition.SelectedIndex - 1])); };
        body.Children.Add(_definition); body.Children.Add(_list); body.Children.Add(_status); body.Children.Add(_import);
        body.Children.Add(Button("Add lookup website…", () => EditWebsite(new(Guid.NewGuid(), "", "https://"))));
        var recommended = new ComboBox { Margin = new(0, 8, 0, 0), DisplayMemberPath = "Name", ItemsSource = DictionaryWebsiteCatalog.Websites };
        recommended.ToolTip = InterfaceText.Get("Add a recommended website (it remains disabled until enabled).", "Learning");
        recommended.SelectionChanged += (_, _) =>
        {
            if (recommended.SelectedItem is not DictionaryWebsite site) return;
            recommended.SelectedItem = null;
            if (!_settings.Configuration.Websites.Any(x => x.Id == site.Id)) Try(() => _settings.SaveWebsite(site));
        };
        body.Children.Add(new TextBlock { Text = InterfaceText.Get("Recommended websites", "Learning"), Margin = new(0, 8, 0, 0) }); body.Children.Add(recommended);
        body.Children.Add(new TextBlock { Text = InterfaceText.Get("System Dictionary is macOS-only. Online lookup sends only the selected term after you choose Look up online. Websites open in your browser only when clicked.", "Learning"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0), FontSize = 11 });
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(BackgroundProperty, "WindowBg"); Content = scroll;
        _import.Click += Import; _settings.Changed += Changed; Closed += (_, _) => Retire(); Render();
    }
    private void Changed(DictionaryConfiguration _) { if (!_closed && Dispatcher.CheckAccess()) Render(); }
    private void Render()
    {
        _list.Children.Clear(); var config = _settings.Configuration;
        _updatingDefinition = true; _definition.SelectedIndex = config.DefinitionLanguage is { } language ? LearningLanguages.All.ToList().IndexOf(language) + 1 : 0; _updatingDefinition = false;
        foreach (var source in config.Order)
        {
            var row = new StackPanel();
            var header = new DockPanel();
            var enabled = new CheckBox { IsChecked = config.Enabled.Contains(source), VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !config.Enabled.SetEquals([source]), Margin = new(0, 0, 8, 0) };
            AutomationProperties.SetName(enabled, "Enable " + config.Name(source));
            enabled.Checked += (_, _) => Try(() => _settings.SetEnabled(source, true)); enabled.Unchecked += (_, _) => Try(() => _settings.SetEnabled(source, false));
            DockPanel.SetDock(enabled, Dock.Left); header.Children.Add(enabled);
            var controls = new WrapPanel { Margin = new(22, 8, 0, 0) };
            var book = config.Libraries.FirstOrDefault(x => x.Source == source);
            var site = config.Websites.FirstOrDefault(x => x.Source == source);
            if (book is not null)
            {
                controls.Children.Add(Button("Languages…", () => EditLanguages(book)));
                var resources = Button("Resources…", () => Attach(book)); resources.IsEnabled = !_busy; controls.Children.Add(resources);
                controls.Children.Add(Button("Remove", () => Try(() => _settings.Remove(source))));
            }
            if (site is not null)
            {
                controls.Children.Add(Button("Edit…", () => EditWebsite(site)));
                controls.Children.Add(Button("Remove", () => Try(() => _settings.Remove(source))));
            }
            var up = Button("↑", () => Try(() => _settings.Move(source, -1)));
            var down = Button("↓", () => Try(() => _settings.Move(source, 1)));
            up.IsEnabled = source != config.Order[0]; down.IsEnabled = source != config.Order[^1];
            up.ToolTip = InterfaceText.Get("Move source up", "Learning"); down.ToolTip = InterfaceText.Get("Move source down", "Learning");
            AutomationProperties.SetName(up, "Move " + config.Name(source) + " up");
            AutomationProperties.SetName(down, "Move " + config.Name(source) + " down");
            controls.Children.Add(up); controls.Children.Add(down);
            string description = book is null ? source.Kind switch { "system" => "Unavailable on Windows", "online" => "api.dictionaryapi.dev · Sends only the lookup term after explicit action", "website" => "Opens in your browser when clicked", _ => "Local bundled expressions" }
                : Path.GetFileName(book.MdxPath) + " · " + book.EntryCount + " entries · " + (book.Languages?.Entry?.Label() ?? "Unspecified language");
            var labels = new StackPanel(); labels.Children.Add(new TextBlock { Text = config.Name(source), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var detail = new TextBlock { Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 3, 0, 0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); labels.Children.Add(detail);
            header.Children.Add(labels); row.Children.Add(header); row.Children.Add(controls);
            var card = new Border { Child = row, Padding = new(12), Margin = new(0, 4, 0, 4), BorderThickness = new(1), CornerRadius = new(8) };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg"); card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            _list.Children.Add(card);
        }
    }
    private static Button Button(string text, Action action)
    { var button = new Button { Content = text, Padding = new(7, 3, 7, 3), Margin = new(3, 0, 0, 0) }; button.Click += (_, _) => action(); return button; }
    private void Try(Func<bool> action) { try { if (!action()) _status.Text = InterfaceText.Get("This dictionary setting could not be saved.", "Learning"); else _status.Text = ""; } catch { _status.Text = InterfaceText.Get("This dictionary setting could not be saved.", "Learning"); } }
    private void EditWebsite(DictionaryWebsite site)
    {
        var window = CreateWebsiteEditor(site); window.ShowDialog();
    }
    // The disposable form edits only local controls until its validated Save succeeds.
    internal Window CreateWebsiteEditor(DictionaryWebsite site)
    {
        var window = new Window { Title = InterfaceText.Get("Lookup website", "Learning"), Owner = this, Width = 480, Height = 440, MinWidth = 380, MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily };
        ConfigureTheme(window);
        var panel = new StackPanel { Margin = new(18) };
        var name = new TextBox { Text = site.Name, Margin = new(0, 3, 0, 8) };
        var template = new TextBox { Text = site.Template, Margin = new(0, 3, 0, 8) };
        AutomationProperties.SetName(name, "Website name"); AutomationProperties.SetName(template, "HTTPS URL with {query}");
        var options = new[] { "Unspecified" }.Concat(LearningLanguages.All.Select(x => x.Label())).ToArray();
        ComboBox Pick(LearningLanguage? value) => new() { ItemsSource = options, SelectedIndex = value is null ? 0 : LearningLanguages.All.ToList().IndexOf(value.Value) + 1, Margin = new(0, 3, 0, 8) };
        var entry = Pick(site.Languages?.Entry); var definition = Pick(site.Languages?.Definition);
        AutomationProperties.SetName(entry, "Entry language"); AutomationProperties.SetName(definition, "Definition language");
        var multi = new CheckBox { Content = InterfaceText.Get("Multilingual entries", "Learning"), IsChecked = site.Languages?.Multilingual == true, Margin = new(0, 0, 0, 8) };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(new TextBlock { Text = InterfaceText.Get("Name", "Learning") }); panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = InterfaceText.Get("HTTPS URL with {query}", "Learning") }); panel.Children.Add(template);
        panel.Children.Add(new TextBlock { Text = InterfaceText.Get("Entry language", "Learning") }); panel.Children.Add(entry);
        panel.Children.Add(new TextBlock { Text = InterfaceText.Get("Definition language", "Learning") }); panel.Children.Add(definition); panel.Children.Add(multi); panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = Button("Cancel", window.Close); cancel.IsCancel = true; actions.Children.Add(cancel);
        var save = Button("Save", () =>
        {
            LearningLanguage? Get(ComboBox c) => c.SelectedIndex <= 0 ? null : LearningLanguages.All[c.SelectedIndex - 1];
            try
            {
                if (_settings.SaveWebsite(site with { Name = name.Text.Trim(), Template = template.Text, Languages = new(Get(entry), Get(definition), multi.IsChecked == true) })) window.Close();
                else error.Text = InterfaceText.Get("Enter a name and valid HTTPS URL containing one {query}.", "Learning");
            }
            catch { error.Text = InterfaceText.Get("This dictionary setting could not be saved.", "Learning"); }
        });
        save.IsDefault = true; actions.Children.Add(save);
        void Validate() => save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text)
            && System.Text.Encoding.UTF8.GetByteCount(name.Text) <= 200 && DictionaryWebsitePolicy.Url(template.Text, "example") is not null;
        name.TextChanged += (_, _) => Validate(); template.TextChanged += (_, _) => Validate(); Validate();
        panel.Children.Add(actions);
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(BackgroundProperty, "WindowBg"); window.Content = scroll;
        return window;
    }
    private static void ConfigureTheme(Window window)
    {
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        window.Resources[typeof(Button)] = new Style(typeof(Button), (Style)window.Resources["Btn"]);
        window.Resources[typeof(TextBox)] = new Style(typeof(TextBox), (Style)window.Resources["Input"]);
        window.Resources[typeof(ComboBox)] = new Style(typeof(ComboBox), (Style)window.Resources["Combo"]);
        var check = new Style(typeof(CheckBox));
        check.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        window.Resources[typeof(CheckBox)] = check;
        window.SetResourceReference(BackgroundProperty, "WindowBg"); window.SetResourceReference(ForegroundProperty, "TextPrimary");
        WindowTheme.Follow(window);
    }
    private void Import(object sender, RoutedEventArgs e)
    {
        if (_busy || _closed) return;
        var dialog = new OpenFileDialog { Filter = "MDict dictionary (*.mdx)|*.mdx", Multiselect = false, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) Track(ImportAsync(dialog.FileName));
    }
    private async Task ImportAsync(string path)
    {
        _busy = true; _import.IsEnabled = false; _status.Text = InterfaceText.Get("Validating dictionary…", "Learning"); Render();
        try
        {
            var book = await _service.ImportAsync(path, _stop.Token);
            if (!_closed && !_stop.IsCancellationRequested)
            {
                if (_settings.Add(book))
                {
                    _status.Text = "";
                    var saved = _settings.Configuration.Libraries.FirstOrDefault(x => string.Equals(x.MdxPath, book.MdxPath, StringComparison.OrdinalIgnoreCase));
                    if (saved is not null) EditLanguages(saved);
                }
                else _status.Text = InterfaceText.Get("This dictionary setting could not be saved.", "Learning");
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!_closed) _status.Text = InterfaceText.Get("The selected dictionary could not be imported.", "Learning"); }
        finally { _busy = false; if (!_closed) { _import.IsEnabled = true; Render(); } }
    }
    private void Attach(MDictLibraryDescriptor book)
    {
        if (_busy || _closed) return;
        var dialog = new OpenFileDialog { Filter = "MDict resources (*.mdd)|*.mdd", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) Track(AttachAsync(book, dialog.FileNames));
    }
    private async Task AttachAsync(MDictLibraryDescriptor book, string[] paths)
    {
        _busy = true; _import.IsEnabled = false; _status.Text = InterfaceText.Get("Validating resources…", "Learning"); Render();
        try { var updated = await _service.AttachAsync(book, paths, _stop.Token);
            if (!_closed && !_stop.IsCancellationRequested && _settings.Configuration.Libraries.Any(x => x.Id == book.Id && x.Revision == book.Revision)) Try(() => _settings.Add(updated)); }
        catch (OperationCanceledException) { }
        catch { if (!_closed) _status.Text = InterfaceText.Get("The selected resource files could not be attached.", "Learning"); }
        finally { _busy = false; if (!_closed) { _import.IsEnabled = true; Render(); } }
    }
    private void EditLanguages(MDictLibraryDescriptor book)
    {
        var window = new Window { Title = InterfaceText.Get("Dictionary languages", "Learning"), Owner = this, Width = 380, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ConfigureTheme(window);
        var panel = new StackPanel { Margin = new(18) }; var options = new[] { "Unspecified" }.Concat(LearningLanguages.All.Select(x => x.Label())).ToArray();
        ComboBox Pick(LearningLanguage? value) { var combo = new ComboBox { ItemsSource = options, SelectedIndex = value is null ? 0 : LearningLanguages.All.ToList().IndexOf(value.Value) + 1, Margin = new(0, 3, 0, 8) }; return combo; }
        var entry = Pick(book.Languages?.Entry); var definition = Pick(book.Languages?.Definition);
        var multi = new CheckBox { Content = InterfaceText.Get("Multilingual entries", "Learning"), IsChecked = book.Languages?.Multilingual == true, Margin = new(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock { Text = InterfaceText.Get("Entry language", "Learning") }); panel.Children.Add(entry); panel.Children.Add(new TextBlock { Text = InterfaceText.Get("Definition language", "Learning") }); panel.Children.Add(definition); panel.Children.Add(multi);
        panel.Children.Add(Button("Save", () => { LearningLanguage? Get(ComboBox c) => c.SelectedIndex <= 0 ? null : LearningLanguages.All[c.SelectedIndex - 1];
            Try(() => _settings.SetLanguages(book.Source, new(Get(entry), Get(definition), multi.IsChecked == true))); window.Close(); }));
        window.Content = panel; window.ShowDialog();
    }
    private void Track(Task task) { lock (_gate) _work.Add(task); _ = task.ContinueWith(t => { _ = t.Exception; lock (_gate) _work.Remove(t); }, TaskScheduler.Default); }
    private void Retire() { if (_closed) return; _closed = true; _settings.Changed -= Changed; _cancellation = _stop.CancelAsync(); }
    public ValueTask DisposeAsync() { Dispatcher.VerifyAccess(); return new(_dispose ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        Retire(); if (IsVisible) Close();
        if (_cancellation is not null) try { await _cancellation; } catch { }
        while (true) { Task[] pending; lock (_gate) pending = _work.ToArray(); if (pending.Length == 0) break; try { await Task.WhenAll(pending); } catch { } }
        _stop.Dispose();
    }
}
