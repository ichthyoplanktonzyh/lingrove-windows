using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Shapes;

namespace Cotabby.App.UI.Vocabulary;

internal static class VocabularySidebarVisuals
{
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0, 122, 255));
    public static readonly Brush Teal = new SolidColorBrush(Color.FromRgb(0, 140, 140));
    public static readonly Brush Secondary = SystemColors.GrayTextBrush;
    public static TextBlock Text(string value, double size, FontWeight weight, Brush? foreground = null) => new()
    {
        Text = value, FontSize = size, FontWeight = weight, Foreground = foreground ?? SystemColors.ControlTextBrush,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
    };
    public static TextBlock Icon(string value, double size, Brush foreground) => new()
    {
        Text = value, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size,
        Foreground = foreground, VerticalAlignment = VerticalAlignment.Center,
    };
    // The Mac uses SF Symbols circle.hexagongrid / bubble.left.and.bubble.right. Windows has
    // no SF Symbols font, so draw small equivalents rather than a missing-font placeholder.
    public static Canvas SemanticIcon(bool concept, double scale)
    {
        var canvas = new Canvas { Width = 12 * scale, Height = 12 * scale, VerticalAlignment = VerticalAlignment.Center };
        if (concept)
        {
            var ring = new Ellipse { Width = 10 * scale, Height = 10 * scale, Stroke = Accent, StrokeThickness = scale };
            Canvas.SetLeft(ring, scale); Canvas.SetTop(ring, scale); canvas.Children.Add(ring);
            foreach (var (x, y) in new[] { (4.0, 3.0), (7.0, 3.0), (2.5, 5.5), (5.5, 5.5), (8.5, 5.5), (4.0, 8.0), (7.0, 8.0) })
            {
                var dot = new Ellipse { Width = 1.3 * scale, Height = 1.3 * scale, Fill = Accent };
                Canvas.SetLeft(dot, x * scale); Canvas.SetTop(dot, y * scale); canvas.Children.Add(dot);
            }
        }
        else
        {
            var bubbles = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0.8,1.5 L 8,1.5 Q 9,1.5 9,2.5 L 9,5.5 Q 9,6.5 8,6.5 L 4,6.5 L 2,8 L 2,6.5 L 0.8,6.5 Z M 5,8 L 9,8 L 11,9.5 L 11,5"),
                Stroke = Teal, StrokeThickness = 0.9, Stretch = Stretch.Fill, Width = 11 * scale, Height = 10 * scale };
            canvas.Children.Add(bubbles);
        }
        return canvas;
    }
    public static Border Badge(string value, bool active, double scale)
    {
        var background = active ? new SolidColorBrush(Color.FromArgb(31, 0, 122, 255)) : new SolidColorBrush(Color.FromArgb(15, 0, 0, 0));
        return new Border { CornerRadius = new CornerRadius(10), Background = background, Padding = new Thickness(5.5, 1, 5.5, 1),
            Child = Text(value, 10.5 * scale, FontWeights.SemiBold, active ? Accent : Secondary), VerticalAlignment = VerticalAlignment.Center };
    }
    public static Button IconButton(string glyph, string label)
    {
        var button = new Button { Content = Icon(glyph, 11, SystemColors.ControlTextBrush), Padding = new Thickness(3),
            MinWidth = 20, MinHeight = 20, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = label };
        AutomationProperties.SetName(button, label); return button;
    }
    public static void Automation(DependencyObject element, string id, string name)
    { AutomationProperties.SetAutomationId(element, id); AutomationProperties.SetName(element, name); }
}
