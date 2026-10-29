using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>One file choice and one cancellable preview lifetime. This window is unconnected until
/// composition supplies the app-owned Frozen Store adapter.</summary>
internal sealed class VocabularyWordListImportWindow : Window
{
    private readonly VocabularyEntryImportSession _session;
    private readonly string _path;
    private readonly LearningLanguage _language;
    private readonly StackPanel _settings = new(), _results = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap }, _failure = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly Button _import = new() { Name = "vocabularyConfirmImport", Content = "Import", IsDefault = true, MinWidth = 90 }, _cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 90 };
    private VocabularyWordList? _renderedTable;
    private bool _retiring;
    internal VocabularyInputResult? Result { get; private set; }
    internal Task? LastOperation { get; private set; }
    internal VocabularyEntryImportSession Session => _session;
    internal VocabularyWordListImportWindow(IVocabularyEntryInputOwner owner, IVocabularyWordListReader reader, string path, LearningLanguage language = LearningLanguage.English)
    {
        _session = new(Dispatcher, owner, reader); _path = path; _language = language;
        Title = "Import word list"; Width = 900; Height = 760; MinWidth = 650; MinHeight = 550; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White; var root = new DockPanel { Margin = new Thickness(20), Background = Brushes.White }; Content = root;
        var footer = VocabularyInputControls.Footer(_cancel, _import); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
        var heading = new DockPanel(); var filename = new TextBlock { Text = Path.GetFileName(path), Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 }; DockPanel.SetDock(filename, Dock.Right); heading.Children.Add(filename);
        heading.Children.Add(VocabularyInputControls.Heading("Import word list")); body.Children.Add(heading);
        body.Children.Add(VocabularyInputControls.Caption("Preview the list before adding it. Existing entries and notes are kept."));
        body.Children.Add(VocabularyInputControls.Caption("CSV columns can be mapped below. TXT uses one expression per line."));
        body.Children.Add(_settings); body.Children.Add(_status); body.Children.Add(_results); body.Children.Add(_failure);
        _session.Changed += Refresh; _import.Click += (_, _) => LastOperation = CommitAsync();
        Loaded += (_, _) => LastOperation = _session.LoadAsync(_path, _language); Closing += OnClosing; Refresh();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    { if (_session.Saving) { e.Cancel = true; return; } _session.Cancel(); }
    internal async Task RetireAsync()
    {
        Dispatcher.VerifyAccess(); _retiring = true; Refresh();
        if (_session.Saving && LastOperation is { } operation) await operation;
        if (IsVisible) Close();
        await _session.DrainAsync();
        if (LastOperation is { } pending) await pending;
    }
    private async Task CommitAsync()
    {
        Result = await _session.CommitAsync();
        if (Result is not null) { Close(); }
    }
    private void Refresh()
    {
        if (_session.Table is { } table && !ReferenceEquals(table, _renderedTable)) { _renderedTable = table; BuildSettings(table); }
        _settings.IsEnabled = !_session.Saving && !_retiring; _cancel.IsEnabled = !_session.Saving && !_retiring;
        _import.IsEnabled = !_retiring && !_session.Preparing && !_session.Saving && _session.Preview?.Plan.Result.HasChanges == true;
        _status.Text = _session.Preparing ? "Preparing preview…" : ""; _failure.Text = _session.Failure ?? "";
        _results.Children.Clear();
        if (_session.Preview is not { } preview) return;
        var count = preview.Plan.Result;
        _results.Children.Add(new TextBlock { Text = $"New entries: {count.AddedEntries}     New senses: {count.AddedSenses}     Duplicates skipped: {count.SkippedRows}", Margin = new Thickness(0, 10, 0, 4) });
        bool examples = _session.Configuration!.Columns.ContainsKey(VocabularyImportField.Example) || _session.Configuration.Columns.ContainsKey(VocabularyImportField.ExampleTranslation);
        if (examples) _results.Children.Add(new TextBlock { Text = $"New examples: {count.AddedExamples}     New translations: {count.AddedTranslations}", Margin = new Thickness(0, 0, 0, 5) });
        var grid = new DataGrid { Name = "vocabularyImportPreview", AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            Height = Math.Min(9, Math.Max(3, preview.Inputs.Count + 1)) * (examples ? 40 : 24) + 4, ItemsSource = preview.Inputs.Take(50).ToArray() };
        AddColumn(grid, "Expression", "Expression"); AddColumn(grid, "Core meaning", "Meaning");
        if (examples) { AddColumn(grid, "Example sentence", "Example"); AddColumn(grid, "Example translation", "ExampleTranslation"); }
        AddColumn(grid, "Language", "Language"); _results.Children.Add(grid);
        if (preview.Inputs.Count > 50) _results.Children.Add(VocabularyInputControls.Caption("Showing the first 50 rows."));
        if (!count.HasChanges) _results.Children.Add(VocabularyInputControls.Caption("All rows are already in the notebook."));
    }
    private static void AddColumn(DataGrid grid, string title, string path) => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new System.Windows.Data.Binding(path), Width = new DataGridLength(160), MinWidth = 90 });
    private void BuildSettings(VocabularyWordList table)
    {
        _settings.Children.Clear(); var config = _session.Configuration!;
        var lang = VocabularyInputControls.Languages(false, config.Language); var kind = VocabularyInputControls.Kinds(); kind.Name = "vocabularyImportKind"; kind.SelectedItem = config.Kind; var meaningLang = VocabularyInputControls.Languages(true, config.MeaningLanguage);
        _settings.Children.Add(VocabularyInputControls.Group(("Language", lang), ("Entry type", kind), ("Meaning language", meaningLang)));
        _settings.Children.Add(VocabularyInputControls.Caption("Empty language and type cells use these defaults."));
        lang.SelectionChanged += (_, _) => Change(c => c with { Language = (LearningLanguage)lang.SelectedValue });
        kind.SelectionChanged += (_, _) => Change(c => c with { Kind = (VocabularyEntryKind)kind.SelectedValue });
        meaningLang.SelectionChanged += (_, _) => Change(c => c with { MeaningLanguage = (LearningLanguage?)meaningLang.SelectedValue });
        if (table.Format != VocabularyWordListFormat.Csv) return;
        _settings.Children.Add(VocabularyInputControls.Caption("Only mapped columns are imported. Images are not included."));
        var header = new CheckBox { Name = "vocabularyImportHeader", Content = "First row contains column names", IsChecked = config.HasHeader, Margin = new Thickness(0, 4, 0, 8) }; _settings.Children.Add(header);
        var primary = new List<(string Label, FrameworkElement Control)>(); var more = new List<(string Label, FrameworkElement Control)>();
        foreach (var field in Enum.GetValues<VocabularyImportField>())
        {
            var box = new ComboBox { MinWidth = 200 }; box.Items.Add("No column");
            for (int i = 0; i < table.ColumnCount; i++) box.Items.Add(ColumnName(i, table, config.HasHeader));
            box.SelectedIndex = config.Columns.TryGetValue(field, out int index) ? index + 1 : 0;
            box.SelectionChanged += (_, _) => Change(c => { var columns = c.Columns.ToDictionary(); if (box.SelectedIndex <= 0) columns.Remove(field); else columns[field] = box.SelectedIndex - 1; return c with { Columns = columns }; });
            (field is VocabularyImportField.Expression or VocabularyImportField.Meaning or VocabularyImportField.Example or VocabularyImportField.ExampleTranslation ? primary : more)
                .Add((FieldName(field), box));
        }
        var mappings = new StackPanel(); mappings.Children.Add(VocabularyInputControls.Group(primary.ToArray())); mappings.Children.Add(new Expander { Header = "More columns", Content = VocabularyInputControls.Group(more.ToArray()), Margin = new Thickness(0, 0, 0, 10) }); _settings.Children.Add(new ScrollViewer { Content = mappings, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        header.Checked += (_, _) => { Change(c => c with { HasHeader = true }); BuildSettings(table); }; header.Unchecked += (_, _) => { Change(c => c with { HasHeader = false }); BuildSettings(table); };
    }
    private static string ColumnName(int index, VocabularyWordList table, bool header)
    {
        string name = $"Column {index + 1}"; var cells = table.Rows[0].Cells;
        return header && index < cells.Count && cells[index].Length > 0 ? name + " · " + string.Concat(cells[index].Take(70)) : name;
    }
    private static string FieldName(VocabularyImportField field) => field switch
    {
        VocabularyImportField.Meaning => "Core meaning", VocabularyImportField.DictionaryForm => "Dictionary form",
        VocabularyImportField.Kind => "Entry type", VocabularyImportField.MeaningLanguage => "Meaning language",
        VocabularyImportField.Note => "Notes", VocabularyImportField.Example => "Example sentence",
        VocabularyImportField.ExampleTranslation => "Example translation", _ => field.ToString()
    };
    private void Change(Func<VocabularyWordListConfiguration, VocabularyWordListConfiguration> change)
    { if (_session.Configuration is { } config) LastOperation = _session.RefreshAsync(change(config)); }
}
