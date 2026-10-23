namespace Cotabby.Core.Vocabulary;

/// <summary>Double-precision canvas/world values corresponding to Mac CGPoint/CGSize. They are
/// neither physical screen pixels nor focus geometry; the WPF canvas will adapt its own events.</summary>
public readonly record struct VocabularyGraphPoint(double X, double Y)
{
    public bool Equals(VocabularyGraphPoint other) => X == other.X && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
public readonly record struct VocabularyGraphSize(double Width, double Height);

/// <summary>Pure copied camera state. Pan/zoom changes screen mapping without heating or moving
/// the future solver. Methods return a new value, adapting Swift's mutating value-type methods.</summary>
public sealed record VocabularyGraphCamera
{
    public double Scale { get; init; } = 1;
    public VocabularyGraphPoint Offset { get; init; }
    public VocabularyGraphPoint Screen(VocabularyGraphPoint point, VocabularyGraphSize size) =>
        new(size.Width / 2 + Offset.X + point.X * Scale, size.Height / 2 + Offset.Y + point.Y * Scale);
    public VocabularyGraphPoint World(VocabularyGraphPoint point, VocabularyGraphSize size) =>
        new((point.X - size.Width / 2 - Offset.X) / Scale, (point.Y - size.Height / 2 - Offset.Y) / Scale);
    public VocabularyGraphCamera Zoom(double factor, VocabularyGraphPoint point, VocabularyGraphSize size)
    {
        if (!double.IsFinite(factor) || factor <= 0) return this;
        var anchor = World(point, size); double scale = Math.Min(4, Math.Max(.15, Scale * factor));
        return this with { Scale = scale, Offset = new(point.X - size.Width / 2 - anchor.X * scale, point.Y - size.Height / 2 - anchor.Y * scale) };
    }
    public VocabularyGraphCamera Fit(IReadOnlyList<VocabularyGraphPoint> positions, VocabularyGraphSize size)
    {
        if (positions.Count == 0 || !(size.Width > 0 && size.Height > 0)) return this;
        double left = positions[0].X, top = positions[0].Y, right = left, bottom = top;
        foreach (var point in positions.Skip(1))
        {
            // Preserve the original tiny rectangle for every point after the first, including
            // its asymmetric .01 extent; replacing this with a symmetric bounding box drifts fit.
            left = Math.Min(left, point.X); top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X + .01); bottom = Math.Max(bottom, point.Y + .01);
        }
        double scale = Math.Min(1.8, Math.Max(.15, Math.Min((size.Width - 150) / Math.Max(100, right - left), (size.Height - 130) / Math.Max(100, bottom - top))));
        return this with { Scale = scale, Offset = new(-(left + (right - left) / 2) * scale, -(top + (bottom - top) / 2) * scale) };
    }
    public bool Equals(VocabularyGraphCamera? other) => other is not null && Scale == other.Scale && Offset == other.Offset;
    public override int GetHashCode() => HashCode.Combine(Scale, Offset);
}
