using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.App.Localization;

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
    private Func<IReadOnlySet<Guid>, VocabularyGraphSnapshot>? _canonicalProjection;
    private Guid? _meaningSelection, _categorySelection;
    private bool _showsUnexplored;
    private Action<bool>? _setUnexplored;
    private readonly CheckBox _unexplored = new() { Name = "GraphShowUnexplored", Content = InterfaceText.Get("Show unexplored categories", "Vocabulary"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16), Padding = new Thickness(6) };
    private Action<Guid>? _selectMeaning, _selectCategory;
    private readonly StackPanel _legend = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(16), IsHitTestVisible = false };
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
        _unexplored.SetResourceReference(Control.ForegroundProperty, "TextPrimary"); _unexplored.Click += (_, _) => _setUnexplored?.Invoke(_unexplored.IsChecked == true); Children.Add(_unexplored);
        var settings = Button("GraphSettings", "⚙", "Graph settings", () => _settingsPopup!.IsOpen = !_settingsPopup.IsOpen);
        settings.HorizontalAlignment = HorizontalAlignment.Right; settings.VerticalAlignment = VerticalAlignment.Top; settings.Margin = new(14); Children.Add(settings);
        var form = new StackPanel { Margin = new(14), Width = 280 };
        form.Children.Add(Title("Appearance"));
        Slider(form, "GraphNodeSize", "Node size", 8, 28, s => s.NodeSize, (s, v) => s with { NodeSize = v });
        Slider(form, "GraphLinkWidth", "Link thickness", .5, 3, s => s.LinkWidth, (s, v) => s with { LinkWidth = v });
        form.Children.Add(Label("Labels"));
        _labels = new ComboBox { Name = "GraphLabels", DisplayMemberPath = nameof(LabelOption.Title), SelectedValuePath = nameof(LabelOption.Value), Margin = new(0, 4, 0, 10),
            ItemsSource = new[] { new LabelOption(VocabularyGraphLabels.Automatic, InterfaceText.Get("Automatic", "Vocabulary")), new(VocabularyGraphLabels.All, InterfaceText.Get("All labels", "Vocabulary")), new(VocabularyGraphLabels.Focused, InterfaceText.Get("Focused labels", "Vocabulary")) } };
        _labels.SetResourceReference(StyleProperty, "Combo");
        AutomationProperties.SetName(_labels, InterfaceText.Get("Labels", "Vocabulary")); RegisterName(_labels.Name, _labels); form.Children.Add(_labels);
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
        Children.Add(_legend);
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        commands.Children.Add(Button("GraphZoomOut", "−", "Zoom out", () => _presentation.Send(VocabularyGraphCommand.ZoomOut)));
        commands.Children.Add(Button("GraphZoomIn", "+", "Zoom in", () => _presentation.Send(VocabularyGraphCommand.ZoomIn)));
        commands.Children.Add(Button("GraphFit", "Fit", "Fit graph", () => _presentation.Send(VocabularyGraphCommand.Fit)));
        commands.Children.Add(Button("GraphLocate", "Locate", "Locate selection", () => _presentation.Send(VocabularyGraphCommand.Locate)));
        commands.Children.Add(Button("GraphCollapse", "Collapse", "Collapse senses", () => { _presentation.Collapse(); _select(null); }));
        var controls = new Border { Child = commands, Padding = new(9), CornerRadius = new(8), Margin = new(12), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        controls.SetResourceReference(Border.BackgroundProperty, "CardBg"); Children.Add(controls);
        _empty = new() { Text = InterfaceText.Get("No matching expressions", "Vocabulary"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); Children.Add(_empty);
        _presentation.PropertyChanged += OnPresentationChanged;
        IsVisibleChanged += (_, _) => { if (!_retired) { if (IsVisible) Refresh(); else _settingsPopup.IsOpen = false; } };
        Refresh();
    }
    internal void Update(IReadOnlyList<VocabularyEntry> entries, string query, VocabularySelection? selection, Action<VocabularySelection?> select)
    {
        Dispatcher.VerifyAccess(); if (_retired) return;
        _canonicalProjection = null; _entries = entries.ToArray(); _query = query; _selection = selection; _select = select;
        // Hidden list mode keeps the latest input without doing graph search/geometry work.
        if (IsVisible) Refresh();
    }
    internal void UpdateCanonical(Func<IReadOnlySet<Guid>, VocabularyGraphSnapshot> projection, VocabularySelection? selection, Action<VocabularySelection?> select, Guid? meaningSelection, Guid? categorySelection, Action<Guid> selectMeaning, Action<Guid> selectCategory, bool showsUnexplored = false, Action<bool>? setUnexplored = null)
    {
        Dispatcher.VerifyAccess(); if (_retired) return; _canonicalProjection = projection; _selection = selection; _select = select; _meaningSelection = meaningSelection; _categorySelection = categorySelection; _selectMeaning = selectMeaning; _selectCategory = selectCategory; _showsUnexplored = showsUnexplored; _setUnexplored = setUnexplored; if (IsVisible) Refresh();
    }
    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs args) { if (!_retired && IsVisible) Refresh(); }
    private void Refresh()
    {
        Dispatcher.VerifyAccess(); if (_retired) return;
        _refreshing = true;
        try { foreach (var (slider, read) in _sliders) slider.Value = read(_presentation.Settings); _labels.SelectedValue = _presentation.Settings.Labels; }
        finally { _refreshing = false; }
        var graph = _canonicalProjection is null ? VocabularyGraphProjection.Make(_entries, _presentation.Expanded, _query, _search) : _canonicalProjection(_presentation.Expanded);
        _unexplored.Visibility = graph.IsMeaningGraph && _setUnexplored is not null ? Visibility.Visible : Visibility.Collapsed; _unexplored.IsChecked = _showsUnexplored;
        _legend.Margin = graph.IsMeaningGraph ? new Thickness(16, 50, 16, 0) : new Thickness(16);
        _empty.Text = graph.IsMeaningGraph && graph.SearchQuery.Length == 0 ? InterfaceText.Get("Browse by meaning\nSelect or create a meaning to compare expressions.", "Vocabulary") : InterfaceText.Get("No matching expressions", "Vocabulary"); _empty.Visibility = graph.Nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _legend.Children.Clear(); var names = graph.IsMeaningGraph ? new[] { "Categories", "Meaning groups", "Expressions" } : new[] { "Headwords", "Senses" }; for (int i = 0; i < names.Length; i++) { Shape mark = graph.IsMeaningGraph && i == 0 ? new Rectangle { RadiusX = 2, RadiusY = 2 } : new Ellipse(); mark.Width = mark.Height = 8; mark.Margin = new(8, 0, 5, 0); mark.SetResourceReference(i == names.Length - 1 ? Shape.StrokeProperty : Shape.FillProperty, i == 0 ? "TextSecondary" : "AccentBrush"); mark.StrokeThickness = 1.3; _legend.Children.Add(mark); _legend.Children.Add(Label(names[i])); }
        if (graph.IsMeaningGraph && _showsUnexplored) { var mark = new Ellipse { Width = 8, Height = 8, Margin = new(8, 0, 5, 0), StrokeDashArray = new DoubleCollection([2, 2]), StrokeThickness = 1 }; mark.SetResourceReference(Shape.StrokeProperty, "TextSecondary"); _legend.Children.Add(mark); _legend.Children.Add(Label("Unexplored")); }
        Canvas.Update(graph, _selection, _select, _meaningSelection, _categorySelection, _selectMeaning, _selectCategory, canonicalLocate: _canonicalProjection is not null);
    }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess();
        if (!_retired) { _retired = true; _presentation.PropertyChanged -= OnPresentationChanged; _settingsPopup.IsOpen = false; }
        return Canvas.RetireAsync();
    }
    private Button Button(string name, string content, string label, Action action)
    {
        var button = new Button { Name = name, Content = InterfaceText.Get(content, "Vocabulary"), ToolTip = InterfaceText.Get(label, "Vocabulary"), Padding = new(7, 4, 7, 4), Margin = new(1) };
        button.SetResourceReference(StyleProperty, "Btn");
        AutomationProperties.SetName(button, InterfaceText.Get(label, "Vocabulary")); RegisterName(name, button); button.Click += (_, _) => { if (!_retired) action(); }; return button;
    }
    private static TextBlock Label(string text) => new() { Text = InterfaceText.Get(text, "Vocabulary"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Title(string text) => new() { Text = InterfaceText.Get(text, "Vocabulary"), FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 8) };
    private void Slider(Panel form, string name, string label, double min, double max, Func<VocabularyGraphSettings, double> read, Func<VocabularyGraphSettings, double, VocabularyGraphSettings> change)
    {
        form.Children.Add(Label(label));
        var slider = new Slider { Name = name, Minimum = min, Maximum = max, Margin = new(0, 4, 0, 10) };
        slider.SetResourceReference(StyleProperty, "ModernSlider");
        AutomationProperties.SetName(slider, InterfaceText.Get(label, "Vocabulary")); RegisterName(name, slider); _sliders.Add((slider, read)); form.Children.Add(slider);
        slider.ValueChanged += (_, e) => { if (!_refreshing && !_retired) _presentation.Settings = change(_presentation.Settings, e.NewValue); };
    }
}
