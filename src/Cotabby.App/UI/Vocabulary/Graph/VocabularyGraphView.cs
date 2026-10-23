using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Original graph chrome borrows the notebook's filtered entries and shared selection.
/// It owns only ordinary controls/popover visibility; presentation owns preferences and expansion,
/// while the native canvas owns input/drawing. No Store or runtime is created by this view.</summary>
internal sealed class VocabularyGraphView : Grid
{
    private readonly VocabularyGraphPresentation _presentation;
    private readonly IVocabularyGraphTextSearching _search;
    private readonly Popup _settingsPopup;
    private readonly TextBlock _empty;
    private readonly ComboBox _labels;
    private readonly List<(Slider Slider, Func<VocabularyGraphSettings, double> Read)> _sliders = [];
    private IReadOnlyList<VocabularyEntry> _entries = [];
    private string _query = "";
    private VocabularySelection? _selection;
    private Action<VocabularySelection?> _select = _ => { };
    private bool _refreshing, _retired;
    internal VocabularyGraphCanvas Canvas { get; }
    internal VocabularyGraphPresentation Presentation => _presentation;
    private sealed record LabelOption(VocabularyGraphLabels Value, string Title);

    internal VocabularyGraphView(VocabularyGraphPresentation presentation, IVocabularyGraphTextSearching search, Action<Task>? track = null)
    {
        _presentation = presentation; _search = search;
        NameScope.SetNameScope(this, new NameScope());
        Canvas = new(presentation, track); Children.Add(Canvas);
        var settings = Button("GraphSettings", "⚙", "Graph settings", () => _settingsPopup!.IsOpen = !_settingsPopup.IsOpen);
        settings.HorizontalAlignment = HorizontalAlignment.Right; settings.VerticalAlignment = VerticalAlignment.Top; settings.Margin = new(14); Children.Add(settings);
        var form = new StackPanel { Margin = new(14), Width = 280 };
        form.Children.Add(Title("Appearance"));
        Slider(form, "GraphNodeSize", "Node size", 8, 28, s => s.NodeSize, (s, v) => s with { NodeSize = v });
        Slider(form, "GraphLinkWidth", "Link thickness", .5, 3, s => s.LinkWidth, (s, v) => s with { LinkWidth = v });
        form.Children.Add(Label("Labels"));
        _labels = new ComboBox { Name = "GraphLabels", DisplayMemberPath = nameof(LabelOption.Title), SelectedValuePath = nameof(LabelOption.Value), Margin = new(0, 4, 0, 10),
            ItemsSource = new[] { new LabelOption(VocabularyGraphLabels.Automatic, "Automatic"), new(VocabularyGraphLabels.All, "All labels"), new(VocabularyGraphLabels.Focused, "Focused labels") } };
        _labels.SetResourceReference(StyleProperty, "Combo");
        AutomationProperties.SetName(_labels, "Labels"); RegisterName(_labels.Name, _labels); form.Children.Add(_labels);
        _labels.SelectionChanged += (_, _) => { if (!_refreshing && _labels.SelectedValue is VocabularyGraphLabels labels) _presentation.Settings = _presentation.Settings with { Labels = labels }; };
        form.Children.Add(Title("Forces"));
        Slider(form, "GraphCenter", "Center force", 0, .08, s => s.Center, (s, v) => s with { Center = v });
        Slider(form, "GraphRepulsion", "Repel force", 10, 1200, s => s.Repulsion, (s, v) => s with { Repulsion = v });
        Slider(form, "GraphLinkStrength", "Link force", 0, .7, s => s.LinkStrength, (s, v) => s with { LinkStrength = v });
        Slider(form, "GraphLinkDistance", "Link distance", 60, 240, s => s.LinkDistance, (s, v) => s with { LinkDistance = v });
        form.Children.Add(Button("GraphDefaults", "Restore graph defaults", "Restore graph defaults", () => _presentation.Settings = new()));
        var popover = new Border { Child = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(180, Math.Min(560, SystemParameters.WorkArea.Height - 60)) }, BorderThickness = new(1), CornerRadius = new(8) };
        popover.SetResourceReference(Border.BackgroundProperty, "WindowBg"); popover.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        _settingsPopup = new() { Name = "GraphSettingsPopup", Child = popover, PlacementTarget = settings, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        RegisterName(_settingsPopup.Name, _settingsPopup); Children.Add(_settingsPopup);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new(16), IsHitTestVisible = false };
        legend.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Brushes.Gray, Margin = new(0, 0, 5, 0) }); legend.Children.Add(Label("Headwords"));
        legend.Children.Add(new Ellipse { Width = 6, Height = 6, Stroke = Brushes.DodgerBlue, StrokeThickness = 1, Margin = new(12, 0, 5, 0) }); legend.Children.Add(Label("Senses")); Children.Add(legend);
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        commands.Children.Add(Button("GraphZoomOut", "−", "Zoom out", () => _presentation.Send(VocabularyGraphCommand.ZoomOut)));
        commands.Children.Add(Button("GraphZoomIn", "+", "Zoom in", () => _presentation.Send(VocabularyGraphCommand.ZoomIn)));
        commands.Children.Add(Button("GraphFit", "Fit", "Fit graph", () => _presentation.Send(VocabularyGraphCommand.Fit)));
        commands.Children.Add(Button("GraphLocate", "Locate", "Locate selection", () => _presentation.Send(VocabularyGraphCommand.Locate)));
        commands.Children.Add(Button("GraphCollapse", "Collapse", "Collapse senses", () => { _presentation.Collapse(); _select(null); }));
        var controls = new Border { Child = commands, Padding = new(9), CornerRadius = new(8), Margin = new(12), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        controls.SetResourceReference(Border.BackgroundProperty, "CardBg"); Children.Add(controls);
        _empty = new() { Text = "No matching expressions", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); Children.Add(_empty);
        _presentation.PropertyChanged += OnPresentationChanged;
        IsVisibleChanged += (_, _) => { if (!_retired) { if (IsVisible) Refresh(); else _settingsPopup.IsOpen = false; } };
        Refresh();
    }
    internal void Update(IReadOnlyList<VocabularyEntry> entries, string query, VocabularySelection? selection, Action<VocabularySelection?> select)
    {
        Dispatcher.VerifyAccess(); if (_retired) return;
        _entries = entries.ToArray(); _query = query; _selection = selection; _select = select;
        // Hidden list mode keeps the latest input without doing graph search/geometry work.
        if (IsVisible) Refresh();
    }
    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs args) { if (!_retired && IsVisible) Refresh(); }
    private void Refresh()
    {
        Dispatcher.VerifyAccess(); if (_retired) return;
        _refreshing = true;
        try { foreach (var (slider, read) in _sliders) slider.Value = read(_presentation.Settings); _labels.SelectedValue = _presentation.Settings.Labels; }
        finally { _refreshing = false; }
        _empty.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Canvas.Update(VocabularyGraphProjection.Make(_entries, _presentation.Expanded, _query, _search), _selection, _select);
    }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess();
        if (!_retired) { _retired = true; _presentation.PropertyChanged -= OnPresentationChanged; _settingsPopup.IsOpen = false; }
        return Canvas.RetireAsync();
    }
    private Button Button(string name, string content, string label, Action action)
    {
        var button = new Button { Name = name, Content = content, ToolTip = label, Padding = new(7, 4, 7, 4), Margin = new(1) };
        button.SetResourceReference(StyleProperty, "Btn");
        AutomationProperties.SetName(button, label); RegisterName(name, button); button.Click += (_, _) => { if (!_retired) action(); }; return button;
    }
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Title(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 8) };
    private void Slider(Panel form, string name, string label, double min, double max, Func<VocabularyGraphSettings, double> read, Func<VocabularyGraphSettings, double, VocabularyGraphSettings> change)
    {
        form.Children.Add(Label(label));
        var slider = new Slider { Name = name, Minimum = min, Maximum = max, Margin = new(0, 4, 0, 10) };
        slider.SetResourceReference(StyleProperty, "ModernSlider");
        AutomationProperties.SetName(slider, label); RegisterName(name, slider); _sliders.Add((slider, read)); form.Children.Add(slider);
        slider.ValueChanged += (_, e) => { if (!_refreshing && !_retired) _presentation.Settings = change(_presentation.Settings, e.NewValue); };
    }
}
