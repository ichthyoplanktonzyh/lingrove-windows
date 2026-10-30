using System.Globalization;
using Cotabby.App.Localization;
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
    // Only the finite relationship chrome enters the interface catalog. Node labels remain
    // literal study/user content and retain their own direction evidence and world coordinates.
    internal static string RelationshipLabel(VocabularyGraphEdgeKind kind) => InterfaceText.Get(kind switch
    {
        VocabularyGraphEdgeKind.Narrower => "More specific than", VocabularyGraphEdgeKind.Opposite => "Opposite",
        VocabularyGraphEdgeKind.Related => "Related", _ => ""
    }, "Vocabulary");
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
        var related = new HashSet<VocabularyGraphNodeId>(); VocabularyGraphNodeId? focus = _hovered ?? (_meaningSelection is { } meaning ? VocabularyGraphNodeId.Meaning(meaning) : _categorySelection is { } category ? VocabularyGraphNodeId.Category(category) : _selection is { } selection ? VocabularyGraphNodeId.Sense(selection) : null);
        if (_snapshot.IsMeaningGraph && focus is { } id) { related.Add(id); foreach (var edge in _snapshot.Edges.Where(e => e.Source == id || e.Target == id)) { related.Add(edge.Source); related.Add(edge.Target); } }
        foreach (var edge in _snapshot.Edges)
        {
            if (!_positions.TryGetValue(edge.Source, out var start) || !_positions.TryGetValue(edge.Target, out var end)) continue;
            bool highlighted = _snapshot.IsMeaningGraph ? related.Contains(edge.Source) && related.Contains(edge.Target) : edge.Source.EntryId == focused; double alpha = _hovered is not null && !highlighted ? .2 : edge.IsUnexplored ? .36 : 1;
            var pen = new Pen(WithOpacity(Brush(highlighted ? "AccentBrush" : "CardBorder", SystemColors.GrayTextBrush), alpha), _applied.LinkWidth); if (edge.IsUnexplored || edge.Kind is VocabularyGraphEdgeKind.Opposite or VocabularyGraphEdgeKind.Related) pen.DashStyle = DashStyles.Dash;
            var a = Screen(start); var b = Screen(end); if (_snapshot.IsMeaningGraph && edge.Kind is VocabularyGraphEdgeKind.Taxonomy or VocabularyGraphEdgeKind.ExpressesContent or VocabularyGraphEdgeKind.RealizesFunction) { var geometry = new StreamGeometry(); using (var context = geometry.Open()) { context.BeginFigure(a, false, false); double middle = (a.X + b.X) / 2; context.BezierTo(new(middle, a.Y), new(middle, b.Y), b, true, false); } drawing.DrawGeometry(null, pen, geometry); } else drawing.DrawLine(pen, a, b);
            if (_snapshot.IsMeaningGraph && edge.Kind is VocabularyGraphEdgeKind.Narrower or VocabularyGraphEdgeKind.Opposite or VocabularyGraphEdgeKind.Related && (highlighted || _applied.Labels == VocabularyGraphLabels.All || _presentation.Camera.Scale > .8)) { var label = new FormattedText(RelationshipLabel(edge.Kind), InterfaceText.Culture, FlowDirection, new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10, Brush("TextSecondary", SystemColors.GrayTextBrush), VisualTreeHelper.GetDpi(this).PixelsPerDip); drawing.DrawText(label, new((a.X + b.X) / 2 + 5, (a.Y + b.Y) / 2 + 3)); }
            if (_snapshot.IsMeaningGraph && edge.Kind == VocabularyGraphEdgeKind.Narrower && (highlighted || _applied.Labels == VocabularyGraphLabels.All || _presentation.Camera.Scale > .8)) { var vector = b - a; if (vector.Length > 30) { vector.Normalize(); var tip = b - vector * 14; var side = new Vector(-vector.Y, vector.X) * 4; drawing.DrawLine(pen, tip - vector * 8 + side, tip); drawing.DrawLine(pen, tip - vector * 8 - side, tip); } }
        }
        var cull = new Rect(-160, -55, ActualWidth + 320, ActualHeight + 110);
        foreach (var node in _snapshot.Nodes)
            if (_positions.TryGetValue(node.Id, out var point) && cull.Contains(Screen(point))) DrawNode(drawing, node, Screen(point), focused, related.Contains(node.Id));
    }
    private void DrawNode(DrawingContext drawing, VocabularyGraphNode node, Point point, Guid? focused, bool semanticRelated)
    {
        if (_snapshot.IsMeaningGraph) { DrawMeaningNode(drawing, node, point, semanticRelated); return; }
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
    private void DrawMeaningNode(DrawingContext drawing, VocabularyGraphNode node, Point point, bool related)
    {
        bool selected = node.Id.Kind switch { VocabularyGraphNodeKind.Meaning => _meaningSelection == node.Id.EntryId, VocabularyGraphNodeKind.Category => _categorySelection == node.Id.EntryId, _ => node.Id.Selection is { } sense && sense == _selection };
        bool active = selected || _snapshot.Matches.Contains(node.Id) || _hovered == node.Id || _keyboardNode == node.Id; double alpha = _hovered is not null && !related ? .23 : 1; var accent = Brush("AccentBrush", SystemColors.HighlightBrush); var textBrush = WithOpacity(Brush(active ? "AccentBrush" : "TextPrimary", SystemColors.ControlTextBrush), alpha); double radius = Math.Max(3, Math.Min(14, _applied.NodeSize * .42 * _presentation.Camera.Scale));
        var fill = WithOpacity(node.IsUnexplored ? Brush("TextSecondary", SystemColors.GrayTextBrush) : accent, (node.IsUnexplored ? .35 : .9) * alpha);
        if (node.Id.Kind is VocabularyGraphNodeKind.Category or VocabularyGraphNodeKind.Axis) drawing.DrawRoundedRectangle(active ? fill : null, new Pen(fill, 1.3), new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2), 3, 3);
        else drawing.DrawEllipse(node.Id.Selection is not null && !active ? null : fill, new Pen(fill, 1.3), point, radius, radius);
        bool show = _applied.Labels != VocabularyGraphLabels.Focused || active || related; if (!show) return;
        string label = node.Label + (node.ItemCount > 0 && node.Id.Selection is null ? " (" + node.ItemCount + ")" : node.HasChildren && !node.IsExpanded ? " +" : "");
        double width = node.Id.Kind == VocabularyGraphNodeKind.Axis || !node.IsExpanded || !node.HasChildren ? 190 : Math.Clamp(260 * _presentation.Camera.Scale - radius - 12, 45, 190);
        width = Math.Min(width, Math.Max(1, node.Id.Kind == VocabularyGraphNodeKind.Axis ? point.X - radius - 6 : ActualWidth - point.X - radius - 6));
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, LabelDirection(node.Id), new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, active ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), 12, textBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = width, MaxLineCount = 1, MaxTextHeight = 18, Trimming = TextTrimming.CharacterEllipsis };
        drawing.DrawText(text, new(node.Id.Kind == VocabularyGraphNodeKind.Axis ? point.X - radius - 6 - text.Width : point.X + radius + 6, point.Y - text.Height / 2));
    }
    private System.Windows.Media.Brush Brush(string key, System.Windows.Media.Brush fallback) => TryFindResource(key) as System.Windows.Media.Brush ?? fallback;
    private static System.Windows.Media.Brush WithOpacity(System.Windows.Media.Brush brush, double alpha)
    { if (alpha == 1) return brush; var result = brush.CloneCurrentValue(); result.Opacity *= alpha; result.Freeze(); return result; }
}
