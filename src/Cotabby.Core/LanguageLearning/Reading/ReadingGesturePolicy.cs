using Cotabby.Core.Focus;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualPolicy;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>3b GesturePolicy. A gesture is bound to one exact selection receipt, never just its
/// spelling. Windows coordinates are physical pixels; fixed AppKit tolerances use the frozen scale.</summary>
public static class ReadingGesturePolicy
{
    public static ScreenRect? SearchBounds(ReadingSelection selection, ScreenRect? region = null, DateTimeOffset? now = null, double scale = 1)
    {
        if (!double.IsFinite(scale) || scale <= 0) return null;
        var gesture = GestureBounds(selection, region, now, scale);
        if (selection.SelectionBounds is { } bounds && ValidSelectionBounds(bounds, scale))
        {
            if (region is { } frame && !Contains(frame, bounds)) return gesture;
            if (gesture is { } box && (bounds.Width > box.Width * 3 || bounds.Height > box.Height * 3)) return gesture;
            return bounds;
        }
        return gesture;
    }
    public static bool ValidSelectionBounds(ScreenRect bounds, double scale = 1) => Valid(bounds) && bounds.Width > scale && bounds.Height > scale;
    private static ScreenRect? GestureBounds(ReadingSelection selection, ScreenRect? region, DateTimeOffset? now, double scale)
    {
        if (selection.Source != ReadingSourceKind.Selection || selection.Gesture is not { } gesture || !Enum.IsDefined(gesture.Kind) || gesture.SelectionId != selection.Id
            || !double.IsFinite(gesture.StartX) || !double.IsFinite(gesture.StartY) || !double.IsFinite(gesture.EndX) || !double.IsFinite(gesture.EndY)
            || (selection.CapturedAt - gesture.CompletedAt).TotalSeconds is < 0 or > 1
            || now is { } time && (time - gesture.CompletedAt).TotalSeconds is < 0 or > 8
            || gesture.Kind == ReadingGestureKind.Drag && Distance(gesture.StartX, gesture.StartY, gesture.EndX, gesture.EndY) <= 3 * scale) return null;
        if (region is { } frame && (!Contains(frame, gesture.StartX, gesture.StartY) || !Contains(frame, gesture.EndX, gesture.EndY))) return null;
        var box = new ScreenRect(Math.Min(gesture.StartX, gesture.EndX) - 12 * scale, Math.Min(gesture.StartY, gesture.EndY) - 12 * scale,
            Math.Abs(gesture.StartX - gesture.EndX) + 24 * scale, Math.Abs(gesture.StartY - gesture.EndY) + 24 * scale);
        return region is { } area ? Intersect(box, area) : box;
    }
    public static bool Agrees(ReadingSelectionGesture gesture, ScreenRect first, ScreenRect last, double scale = 1)
    {
        if (!Enum.IsDefined(gesture.Kind) || !double.IsFinite(scale) || scale <= 0 || !ValidSelectionBounds(first, scale) || !ValidSelectionBounds(last, scale)) return false;
        if (gesture.Kind == ReadingGestureKind.DoubleClick) return first == last && Contains(Pad(first, 3 * scale), gesture.EndX, gesture.EndY);
        double tolerance = Math.Min(12 * scale, Math.Max(5 * scale, Math.Min(first.Height, last.Height) * .5));
        bool Brackets(double x, double y, double ex, double ey) => Math.Abs(x - first.X) <= tolerance && Math.Abs(ex - last.Right) <= tolerance
            && y >= first.Y - tolerance && y <= first.Bottom + tolerance && ey >= last.Y - tolerance && ey <= last.Bottom + tolerance;
        return Brackets(gesture.StartX, gesture.StartY, gesture.EndX, gesture.EndY) || Brackets(gesture.EndX, gesture.EndY, gesture.StartX, gesture.StartY);
    }
    public static ScreenRect Pad(ScreenRect box, double pad) => new(box.X - pad, box.Y - pad, box.Width + 2 * pad, box.Height + 2 * pad);
    private static double Distance(double x, double y, double ex, double ey) => Math.Sqrt((x - ex) * (x - ex) + (y - ey) * (y - ey));
}
