using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Text;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Drawing stays separate from pointer/session orchestration. The original dot radii,
/// edge emphasis, label gates and culling bounds use platform fonts/brushes in WPF DIPs, without
/// creating a visual control for every particle. Camera changes never mutate worker positions.</summary>
internal sealed partial class VocabularyGraphCanvas
{
    private Dictionary<VocabularyGraphNodeId, WindowsTextDirection> _labelDirections = [];
    private void RefreshLabelDirections() => _labelDirections = _snapshot.Nodes.ToDictionary(node => node.Id, node => WindowsTextBaseDirection.Detect(node.Label));
    private FlowDirection LabelDirection(VocabularyGraphNodeId id) => _labelDirections.GetValueOrDefault(id) switch
    {
        WindowsTextDirection.LeftToRight => FlowDirection.LeftToRight, WindowsTextDirection.RightToLeft => FlowDirection.RightToLeft,
        _ => FlowDirection // Neutral/unavailable retains the containing UI direction; provider gaps stay explicit.
    };
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing); drawing.DrawRectangle(Brush("WindowBg", SystemColors.WindowBrush), null, new Rect(RenderSize));
        Guid? focused = _hovered?.EntryId ?? _presentation.FocusedEntryId;
        foreach (var edge in _snapshot.Edges)
        {
            if (!_positions.TryGetValue(edge.Source, out var start) || !_positions.TryGetValue(edge.Target, out var end)) continue;
            bool highlighted = edge.Source.EntryId == focused; double alpha = _hovered is not null && !highlighted ? .2 : 1;
            drawing.DrawLine(new Pen(WithOpacity(Brush(highlighted ? "AccentBrush" : "CardBorder", SystemColors.GrayTextBrush), alpha), _applied.LinkWidth), Screen(start), Screen(end));
        }
        var cull = new Rect(-160, -55, ActualWidth + 320, ActualHeight + 110);
        foreach (var node in _snapshot.Nodes)
            if (_positions.TryGetValue(node.Id, out var point) && cull.Contains(Screen(point))) DrawNode(drawing, node, Screen(point), focused);
    }
    private void DrawNode(DrawingContext drawing, VocabularyGraphNode node, Point point, Guid? focused)
    {
        bool sense = node.Id.Selection is not null;
        bool selected = node.Id.Selection is { } selection ? selection == _selection : node.Id.EntryId == _presentation.FocusedEntryId;
        bool active = selected || _snapshot.Matches.Contains(node.Id), related = node.Id.EntryId == focused;
        double alpha = _hovered is not null && !related ? .23 : 1;
        double radius = Math.Max(3, Math.Min(20, _applied.NodeSize * (sense ? .29 : .5) * _presentation.Camera.Scale));
        var accent = Brush("AccentBrush", SystemColors.HighlightBrush);
        if (active || _hovered == node.Id || _keyboardNode == node.Id) drawing.DrawEllipse(WithOpacity(accent, .1), null, point, radius + 5, radius + 5);
        var color = WithOpacity(active || sense ? accent : Brush("TextSecondary", SystemColors.GrayTextBrush), alpha);
        drawing.DrawEllipse(sense && !active ? null : color, sense && !active ? new Pen(color, 1.4) : null, point, radius, radius);
        bool show = _applied.Labels == VocabularyGraphLabels.All || active || _keyboardNode == node.Id || related
            || (_applied.Labels == VocabularyGraphLabels.Automatic && _presentation.Camera.Scale >= .65);
        if (!show) return;
        var foreground = WithOpacity(active ? accent : Brush(sense ? "TextSecondary" : "TextPrimary", SystemColors.ControlTextBrush), alpha);
        var text = new FormattedText(node.Label, CultureInfo.CurrentUICulture, LabelDirection(node.Id),
            new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, active ? FontWeights.Medium : FontWeights.Normal, FontStretches.Normal),
            sense ? 12 : 13, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { TextAlignment = TextAlignment.Center, MaxTextWidth = 152, MaxTextHeight = 36, Trimming = TextTrimming.CharacterEllipsis };
        drawing.DrawText(text, new(point.X - 76, point.Y + radius + 6));
    }
    private System.Windows.Media.Brush Brush(string key, System.Windows.Media.Brush fallback) => TryFindResource(key) as System.Windows.Media.Brush ?? fallback;
    private static System.Windows.Media.Brush WithOpacity(System.Windows.Media.Brush brush, double alpha)
    { if (alpha == 1) return brush; var result = brush.CloneCurrentValue(); result.Opacity *= alpha; result.Freeze(); return result; }
}
