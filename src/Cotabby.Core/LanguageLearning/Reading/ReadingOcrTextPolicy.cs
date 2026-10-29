namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>Source-derived 3b OCR reading order. All recognized lines remain in the target;
/// missing geometry preserves provider order, and adversarial splitting stops at depth sixteen.</summary>
public static class ReadingOcrTextPolicy
{
    public static string Text(IReadOnlyList<ReadingOcrLine> lines, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (width <= 0 || height <= 0 || lines.Count > 4096) throw new ArgumentException("Invalid OCR image.");
        var ordered = Order(lines.Select(line => new Positioned(line, Bounds(line, width, height))).ToArray(), 0);
        string text = string.Join("\n", ordered.Select(item => item.Line.Text)); ReadingSelectionPolicy.Validate(text); return text;
    }
    private readonly record struct Box(double Left, double Top, double Right, double Bottom);
    private readonly record struct Positioned(ReadingOcrLine Line, Box? Bounds);
    private static Box? Bounds(ReadingOcrLine line, int width, int height)
    {
        if (line.Text is null || line.Words.Count == 0) return null;
        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = 0, bottom = 0;
        foreach (var word in line.Words)
        {
            var b = word.Bounds;
            if (!double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Width) || !double.IsFinite(b.Height)
                || b.X < 0 || b.Y < 0 || b.Width <= 0 || b.Height <= 0 || b.X + b.Width > width || b.Y + b.Height > height)
                throw new ArgumentException("Invalid OCR bounds.");
            left = Math.Min(left, b.X / width); top = Math.Min(top, b.Y / height);
            right = Math.Max(right, (b.X + b.Width) / width); bottom = Math.Max(bottom, (b.Y + b.Height) / height);
        }
        return new(left, top, right, bottom);
    }
    private static Positioned[] Order(Positioned[] lines, int depth)
    {
        if (lines.Length < 2 || lines.Any(x => x.Bounds is null)) return lines;
        var rows = lines.OrderBy(x => (x.Bounds!.Value.Top + x.Bounds.Value.Bottom) / 2).ThenBy(x => x.Bounds!.Value.Left).ToArray();
        if (depth >= 16) return rows;
        var byX = lines.OrderBy(x => x.Bounds!.Value.Left).ToArray(); double right = byX[0].Bounds!.Value.Right;
        for (int i = 1; i < byX.Length; i++)
        {
            if (byX[i].Bounds!.Value.Left - right > .035) return [.. Order(byX[..i], depth + 1), .. Order(byX[i..], depth + 1)];
            right = Math.Max(right, byX[i].Bounds!.Value.Right);
        }
        double bottom = rows[0].Bounds!.Value.Bottom;
        for (int i = 1; i < rows.Length; i++)
        {
            if (rows[i].Bounds!.Value.Top - bottom > .02) return [.. Order(rows[..i], depth + 1), .. Order(rows[i..], depth + 1)];
            bottom = Math.Max(bottom, rows[i].Bounds!.Value.Bottom);
        }
        return rows;
    }
}
