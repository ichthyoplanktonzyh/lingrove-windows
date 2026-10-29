using System.IO;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Dictionaries;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Dictionary;
using Microsoft.Win32;

namespace Cotabby.App.UI.Settings;

/// <summary>Explicit local source management. Import validation runs in the shared service;
/// deleting a row changes settings only, never the selected file.</summary>
internal sealed class DictionarySettingsWindow : Window, IAsyncDisposable
{
    private readonly DictionarySettings _settings; private readonly MDictLibraryService _service;
    private readonly StackPanel _list = new(); private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly Button _import = new() { Content = "Import MDict…", Padding = new(12, 6, 12, 6) };
    private readonly CancellationTokenSource _stop = new(); private readonly HashSet<Task> _work = []; private readonly object _gate = new();
    private readonly ComboBox _definition = new() { Margin = new(0, 4, 0, 10) };
    private bool _updatingDefinition;
    private bool _closed, _busy; private Task? _dispose, _cancellation;
    internal DictionarySettingsWindow(DictionarySettings settings, MDictLibraryService service)
    {
        _settings = settings; _service = service; Title = "Dictionary sources"; Width = 650; Height = 560; MinWidth = 480; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI");
        var body = new StackPanel { Margin = new(20) }; body.Children.Add(new TextBlock { Text = "Dictionary sources", FontSize = 20, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "Choose source priority and languages. Imported files stay in their original location.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 12) });
        body.Children.Add(new TextBlock { Text = "Preferred definition language" });
        _definition.ItemsSource = new[] { "System language" }.Concat(LearningLanguages.All.Select(x => x.Label())).ToArray();
        _definition.SelectionChanged += (_, _) => { if (!_updatingDefinition && _definition.SelectedIndex >= 0) Try(() => _settings.SetDefinitionLanguage(_definition.SelectedIndex == 0 ? null : LearningLanguages.All[_definition.SelectedIndex - 1])); };
        body.Children.Add(_definition); body.Children.Add(_list); body.Children.Add(_status); body.Children.Add(_import);
        body.Children.Add(new TextBlock { Text = "System Dictionary is macOS-only. Online sources require a separately enabled provider and are not available here.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0), FontSize = 11 });
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _import.Click += Import; _settings.Changed += Changed; Closed += (_, _) => Retire(); Render();
    }
    private void Changed(DictionaryConfiguration _) { if (!_closed && Dispatcher.CheckAccess()) Render(); }
    private void Render()
    {
        _list.Children.Clear(); var config = _settings.Configuration;
        _updatingDefinition = true; _definition.SelectedIndex = config.DefinitionLanguage is { } language ? LearningLanguages.All.ToList().IndexOf(language) + 1 : 0; _updatingDefinition = false;
        foreach (var source in config.Order)
        {
            var row = new DockPanel { Margin = new(0, 4, 0, 4) };
            var enabled = new CheckBox { IsChecked = config.Enabled.Contains(source), VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !config.Enabled.SetEquals([source]), Margin = new(0, 0, 8, 0) };
            enabled.Checked += (_, _) => Try(() => _settings.SetEnabled(source, true)); enabled.Unchecked += (_, _) => Try(() => _settings.SetEnabled(source, false));
            DockPanel.SetDock(enabled, Dock.Left); row.Children.Add(enabled);
            var controls = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(controls, Dock.Right);
            var book = config.Libraries.FirstOrDefault(x => x.Source == source);
            if (book is not null)
            {
                controls.Children.Add(Button("Languages…", () => EditLanguages(book)));
                var resources = Button("Resources…", () => Attach(book)); resources.IsEnabled = !_busy; controls.Children.Add(resources);
                controls.Children.Add(Button("Remove", () => Try(() => _settings.Remove(source))));
            }
            controls.Children.Add(Button("↑", () => Try(() => _settings.Move(source, -1))));
            controls.Children.Add(Button("↓", () => Try(() => _settings.Move(source, 1)))); row.Children.Add(controls);
            string description = book is null ? source.Kind switch { "system" => "Unavailable on Windows", "online" => "Not enabled in this Windows module", _ => "Local bundled expressions" }
                : Path.GetFileName(book.MdxPath) + " · " + book.EntryCount + " entries · " + (book.Languages?.Entry?.Label() ?? "Unspecified language");
            var labels = new StackPanel(); labels.Children.Add(new TextBlock { Text = config.Name(source), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            labels.Children.Add(new TextBlock { Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap }); row.Children.Add(labels); _list.Children.Add(row);
        }
    }
    private static Button Button(string text, Action action)
    { var button = new Button { Content = text, Padding = new(7, 3, 7, 3), Margin = new(3, 0, 0, 0) }; button.Click += (_, _) => action(); return button; }
    private void Try(Func<bool> action) { try { if (!action()) _status.Text = "This dictionary setting could not be saved."; else _status.Text = ""; } catch { _status.Text = "This dictionary setting could not be saved."; } }
    private void Import(object sender, RoutedEventArgs e)
    {
        if (_busy || _closed) return;
        var dialog = new OpenFileDialog { Filter = "MDict dictionary (*.mdx)|*.mdx", Multiselect = false, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) Track(ImportAsync(dialog.FileName));
    }
    private async Task ImportAsync(string path)
    {
        _busy = true; _import.IsEnabled = false; _status.Text = "Validating dictionary…"; Render();
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
                else _status.Text = "This dictionary setting could not be saved.";
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!_closed) _status.Text = "The selected dictionary could not be imported."; }
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
        _busy = true; _import.IsEnabled = false; _status.Text = "Validating resources…"; Render();
        try { var updated = await _service.AttachAsync(book, paths, _stop.Token);
            if (!_closed && !_stop.IsCancellationRequested && _settings.Configuration.Libraries.Any(x => x.Id == book.Id && x.Revision == book.Revision)) Try(() => _settings.Add(updated)); }
        catch (OperationCanceledException) { }
        catch { if (!_closed) _status.Text = "The selected resource files could not be attached."; }
        finally { _busy = false; if (!_closed) { _import.IsEnabled = true; Render(); } }
    }
    private void EditLanguages(MDictLibraryDescriptor book)
    {
        var window = new Window { Title = "Dictionary languages", Owner = this, Width = 380, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(18) }; var options = new[] { "Unspecified" }.Concat(LearningLanguages.All.Select(x => x.Label())).ToArray();
        ComboBox Pick(LearningLanguage? value) { var combo = new ComboBox { ItemsSource = options, SelectedIndex = value is null ? 0 : LearningLanguages.All.ToList().IndexOf(value.Value) + 1, Margin = new(0, 3, 0, 8) }; return combo; }
        var entry = Pick(book.Languages?.Entry); var definition = Pick(book.Languages?.Definition);
        var multi = new CheckBox { Content = "Multilingual entries", IsChecked = book.Languages?.Multilingual == true, Margin = new(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock { Text = "Entry language" }); panel.Children.Add(entry); panel.Children.Add(new TextBlock { Text = "Definition language" }); panel.Children.Add(definition); panel.Children.Add(multi);
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
