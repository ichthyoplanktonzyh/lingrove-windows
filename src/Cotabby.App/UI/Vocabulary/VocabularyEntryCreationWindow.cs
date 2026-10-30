using Cotabby.App.Localization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Globalization;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Unsaved sheet draft. Only its injected shared owner may assign IDs or write.</summary>
internal sealed class VocabularyEntryCreationWindow : Window
{
    private readonly IVocabularyEntryInputOwner _owner;
    private readonly TextBox _expression = new() { Name = "vocabularyNewExpression" }, _dictionary = new(), _meaning = new() { Name = "vocabularyNewMeaning" }, _notes = new(), _tags = new();
    private readonly ComboBox _language, _kind, _meaningLanguage;
    private readonly StackPanel _fields = new();
    private readonly Button _save = new() { Name = "vocabularyCreateEntry", Content = InterfaceText.Get("Save"), IsDefault = true, MinWidth = 90 }, _cancel = new() { Content = InterfaceText.Get("Cancel"), IsCancel = true, MinWidth = 90 };
    private readonly TextBlock _failure = VocabularyNotebookTheme.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush");
    private bool _saving, _retiring;
    private TaskCompletionSource? _retirement;
    internal VocabularyInputResult? Result { get; private set; }
    internal Task? LastOperation { get; private set; }
    internal VocabularyEntryCreationWindow(IVocabularyEntryInputOwner owner, LearningLanguage language = LearningLanguage.English)
    {
        _owner = owner; Title = InterfaceText.Get("Add entry"); Width = 620; Height = 590; MinWidth = 500; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _language = VocabularyInputControls.Languages(true, language); _meaningLanguage = VocabularyInputControls.Languages(true, null);
        _kind = VocabularyInputControls.Kinds();
        VocabularyNotebookTheme.Follow(this); var root = new DockPanel { Margin = new Thickness(22) }; VocabularyNotebookTheme.Brush(root, Panel.BackgroundProperty, "WindowBg"); Content = root;
        var title = VocabularyInputControls.Heading(InterfaceText.Get("Add entry")); DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var footer = VocabularyInputControls.Footer(_cancel, _save); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        DockPanel.SetDock(_failure, Dock.Bottom); root.Children.Add(_failure);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _fields }; root.Children.Add(scroll);
        _fields.Children.Add(VocabularyInputControls.Group(
            ("Expression", _expression), ("Language", _language), ("Entry type", _kind), ("Dictionary form", _dictionary)));
        _fields.Children.Add(VocabularyInputControls.Group(
            ("Core meaning", _meaning), ("Meaning language", _meaningLanguage), ("Notes", _notes), ("Tags", _tags)));
        _fields.Children.Add(VocabularyInputControls.Caption(InterfaceText.Get("Separate tags with commas or semicolons.")));
        _fields.Children.Add(VocabularyInputControls.Caption(InterfaceText.Get("Existing entries keep their content. New content is added as a separate sense.")));
        _expression.TextChanged += (_, _) => Update(); _save.Click += (_, _) => LastOperation = SaveAsync();
        Loaded += (_, _) => _expression.Focus(); Closing += OnClosing; Update();
    }
    private void Update() { _fields.IsEnabled = !_saving && !_retiring; _cancel.IsEnabled = !_saving && !_retiring; _save.IsEnabled = !_saving && !_retiring && !string.IsNullOrWhiteSpace(_expression.Text); }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_saving) e.Cancel = true; }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess(); if (_retirement is not null) return _retirement.Task;
        var completion = _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = CompleteRetirementAsync(completion); return completion.Task;
    }
    private async Task CompleteRetirementAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        _retiring = true;
        try { Update(); } catch (Exception error) { failure = error; }
        try { if (LastOperation is { } operation) await operation; }
        catch (Exception error) { failure ??= error; }
        try { if (IsVisible) Close(); }
        catch (Exception error) { failure ??= error; }
        if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
    }
    private async Task SaveAsync()
    {
        if (_saving || !_save.IsEnabled) return;
        _saving = true; _failure.Text = ""; Update();
        try
        {
            var value = VocabularyEntryInputPolicy.Validated(new(_expression.Text, _dictionary.Text,
                (LearningLanguage?)_language.SelectedValue, (VocabularyEntryKind)_kind.SelectedValue,
                _meaning.Text, (LearningLanguage?)_meaningLanguage.SelectedValue, _notes.Text,
                _tags.Text.Split([',', '，', ';', '；'])));
            Result = await _owner.AddEntriesAsync(Array.AsReadOnly(new[] { value }), CancellationToken.None);
        }
        catch (Exception error) { _failure.Text = VocabularyInputMessages.For(error); }
        finally { _saving = false; Update(); }
        if (Result is not null) { Close(); }
    }
}

internal static class VocabularyInputControls
{
    private sealed record Option(string Label, object? Value) { public override string ToString() => Label; }
    private sealed class KindLabelConverter : IValueConverter
    {
        // Detaching/rebuilding a ComboBox can clear a presenter's DataContext before its binding.
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is VocabularyEntryKind kind ? InterfaceText.Get(kind.ToString()) : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    internal static ComboBox Languages(bool optional, LearningLanguage? selected)
    {
        var box = new ComboBox { MinWidth = 130, SelectedValuePath = nameof(Option.Value), DisplayMemberPath = nameof(Option.Label) };
        if (optional) box.Items.Add(new Option(InterfaceText.Get("Not specified"), null));
        foreach (var value in LearningLanguages.All) box.Items.Add(new Option(InterfaceText.Get(value.Name()), value));
        box.SelectedValue = selected; if (selected is null) box.SelectedIndex = 0; return box;
    }
    internal static ComboBox Kinds()
    {
        var box = new ComboBox { MinWidth = 130, ItemsSource = Enum.GetValues<VocabularyEntryKind>(), SelectedItem = VocabularyEntryKind.Word };
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding { Converter = new KindLabelConverter() });
        box.ItemTemplate = new DataTemplate { VisualTree = label };
        box.SelectedValuePath = ""; return box;
    }
    internal static TextBlock Heading(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
    internal static TextBlock Caption(string text) => VocabularyNotebookTheme.Brush(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) }, TextBlock.ForegroundProperty, "TextSecondary");
    internal static StackPanel Footer(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var button in buttons) { VocabularyNotebookTheme.Button(button, button == buttons[^1]); button.Margin = new Thickness(8, 0, 0, 0); panel.Children.Add(button); } return panel;
    }
    internal static Border Defaults(params (string Label, FrameworkElement Control)[] fields)
    {
        var grid = new Grid(); for (int i = 0; i < fields.Length; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < fields.Length; i++)
        {
            var column = new StackPanel { Margin = new Thickness(0, 0, i == fields.Length - 1 ? 0 : 12, 0) };
            var label = InterfaceText.Get(fields[i].Label);
            column.Children.Add(Caption(label)); if (fields[i].Control is ComboBox combo) VocabularyNotebookTheme.Combo(combo);
            System.Windows.Automation.AutomationProperties.SetName(fields[i].Control, label); column.Children.Add(fields[i].Control); Grid.SetColumn(column, i); grid.Children.Add(column);
        }
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12), Child = grid };
        VocabularyNotebookTheme.Brush(card, Border.BackgroundProperty, "CardBg"); return VocabularyNotebookTheme.Brush(card, Border.BorderBrushProperty, "CardBorder");
    }
    internal static Border Group(params (string Label, FrameworkElement Control)[] fields)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(145) }); grid.ColumnDefinitions.Add(new());
        for (int i = 0; i < fields.Length; i++)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            string localized = InterfaceText.Get(fields[i].Label);
            var label = new TextBlock { Text = localized, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 10, 5) };
            Grid.SetRow(label, i); grid.Children.Add(label); var control = fields[i].Control; control.Margin = new Thickness(0, 4, 0, 4);
            System.Windows.Automation.AutomationProperties.SetName(control, localized); if (control is TextBox text) VocabularyNotebookTheme.Input(text); else if (control is ComboBox combo) VocabularyNotebookTheme.Combo(combo); Grid.SetRow(control, i); Grid.SetColumn(control, 1); grid.Children.Add(control);
        }
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12), Child = grid }; VocabularyNotebookTheme.Brush(card, Border.BackgroundProperty, "CardBg"); return VocabularyNotebookTheme.Brush(card, Border.BorderBrushProperty, "CardBorder");
    }
}
