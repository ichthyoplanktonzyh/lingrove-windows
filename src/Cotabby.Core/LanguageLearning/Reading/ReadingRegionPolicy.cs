using Cotabby.Core.Focus;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>Physical-pixel admission for an explicitly chosen OCR region. Monitor and focused
/// field geometry are independently verified by the platform owner; this rule grants no read.</summary>
public static class ReadingRegionPolicy
{
    public static bool IsValidBounds(ScreenRect r) => double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.Width)
        && double.IsFinite(r.Height) && double.IsFinite(r.Right) && double.IsFinite(r.Bottom)
        && r.Width > 0 && r.Height > 0 && r.X == Math.Truncate(r.X) && r.Y == Math.Truncate(r.Y)
        && r.Width == Math.Truncate(r.Width) && r.Height == Math.Truncate(r.Height);
    public static bool Contains(ScreenRect field, ScreenRect monitor, ScreenRect region)
    {
        static bool Inside(ScreenRect outer, ScreenRect inner) => inner.X >= outer.X && inner.Y >= outer.Y
            && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
        return IsValidBounds(field) && IsValidBounds(monitor) && IsValidBounds(region) && region.Width >= 4 && region.Height >= 4
            && region.Width * region.Height <= 16_000_000 && Inside(field, region) && Inside(monitor, region);
    }
}
