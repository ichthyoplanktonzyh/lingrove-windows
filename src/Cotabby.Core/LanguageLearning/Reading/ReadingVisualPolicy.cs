using Cotabby.Core.Focus;
using Cotabby.Core.Text;
using System.Text;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>Committed Mac 3b visual geometry, adapted to top-left physical Windows pixels.
/// One operation freezes its frame and scale; later pointer positions never relocate a hit.</summary>
public static class ReadingVisualPolicy
{
    public static ScreenRect? Snapshot(double x, double y, ScreenRect display, ScreenRect window, double scale, int maximumDimension = int.MaxValue)
    {
        if (!double.IsFinite(scale) || scale <= 0 || !ReadingRegionPolicy.IsValidBounds(display)
            || !ReadingRegionPolicy.IsValidBounds(window) || maximumDimension < 4 || !Contains(display, x, y)) return null;
        var visible = Intersect(display, window);
        if (visible.Width < 4 || visible.Height < 4 || !Contains(visible, x, y)) return null;
        double factor = Math.Min(1, Math.Sqrt(16_000_000 / (1600 * 1200 * scale * scale)));
        var centered = Centered(x, y, Math.Min(maximumDimension, Math.Floor(1600 * factor * scale)), Math.Min(maximumDimension, Math.Floor(1200 * factor * scale)), visible);
        // Windows' capture adapter has a strict 16M envelope. Round the outer frame inward by
        // less than one pixel, retaining original glyph pixels rather than downsampling.
        return centered with { X = Math.Floor(centered.X), Y = Math.Floor(centered.Y) };
    }
    public static ScreenRect Centered(double x, double y, double width, double height, ScreenRect bounds)
    {
        width = Math.Min(width, bounds.Width); height = Math.Min(height, bounds.Height);
        return new(Math.Min(Math.Max(x - width / 2, bounds.X), bounds.Right - width),
            Math.Min(Math.Max(y - height / 2, bounds.Y), bounds.Bottom - height), width, height);
    }
    public static ScreenRect Expanded(ScreenRect region, ScreenRect? text, ReadingContext? context, ScreenRect bounds, double scale)
    {
        if (text is not { } box) return bounds;
        double pad = 28 * scale;
        bool horizontal = box.X <= region.X + pad || box.Right >= region.Right - pad;
        bool vertical = box.Y <= region.Y + pad || box.Bottom >= region.Bottom - pad;
        double left = horizontal && !Complete(context?.Left) ? bounds.X : region.X;
        double right = horizontal && !Complete(context?.Right) ? bounds.Right : region.Right;
        // Windows Y increases downwards; left/right textual boundaries govern top/bottom.
        double top = vertical && !Complete(context?.Left) ? bounds.Y : region.Y;
        double bottom = vertical && !Complete(context?.Right) ? bounds.Bottom : region.Bottom;
        return Intersect(new(left, top, right - left, bottom - top), bounds);
    }
    public static bool Complete(ReadingBoundary? boundary) => boundary is ReadingBoundary.Container or ReadingBoundary.Sentence;
    public static bool Complete(ReadingContext? context) => context is not null && Complete(context.Left) && Complete(context.Right);
    public static bool Valid(ScreenRect box) => double.IsFinite(box.X) && double.IsFinite(box.Y) && double.IsFinite(box.Right)
        && double.IsFinite(box.Bottom) && box.Width > 0 && box.Height > 0;
    public static bool Contains(ScreenRect box, double x, double y) => Valid(box) && x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom;
    public static bool Contains(ScreenRect outer, ScreenRect inner) => Valid(outer) && Valid(inner) && inner.X >= outer.X
        && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
    public static ScreenRect Intersect(ScreenRect a, ScreenRect b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y),
        Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y)));
    public static ScreenRect Union(ScreenRect a, ScreenRect b) => !Valid(a) ? b : !Valid(b) ? a : new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Max(a.Right, b.Right) - Math.Min(a.X, b.X), Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Y, b.Y));
    public static ScreenRect IntegralInside(ScreenRect region, ScreenRect frame)
    {
        double x = Math.Max(frame.X, Math.Floor(region.X)), y = Math.Max(frame.Y, Math.Floor(region.Y));
        return new(x, y, Math.Min(frame.Right, Math.Ceiling(region.Right)) - x, Math.Min(frame.Bottom, Math.Ceiling(region.Bottom)) - y);
    }
    public static ReadingVisualWord? Hit(double x, double y, ReadingVisualText text)
    {
        var candidates = new List<(ReadingVisualWord Word, double Distance, double Margin)>();
        foreach (var word in text.Words)
        {
            if (!text.ValidWord(word) || !word.Text.EnumerateRunes().Any(System.Text.Rune.IsLetter)
                || word.Confidence is double confidence && (!double.IsFinite(confidence) || confidence < .3)) continue;
            var box = word.Bounds;
            double dx = Math.Max(0, Math.Max(box.X - x, x - box.Right)), dy = Math.Max(0, Math.Max(box.Y - y, y - box.Bottom));
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= Math.Min(8, box.Height * .22)) candidates.Add((word, distance, Math.Min(2, box.Height * .08)));
        }
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (candidates.Count == 0) return null;
        var first = candidates[0];
        if (candidates.Count == 1 || candidates[1].Distance - first.Distance > first.Margin) return first.Word;
        var tied = candidates.Where(item => item.Distance - first.Distance <= first.Margin).Select(item => item.Word).OrderBy(item => item.Start).ToArray();
        if (tied.Length is < 2 or > 8 || tied.Any(item => item.Line != first.Word.Line || item.Bounds != first.Word.Bounds)) return null;
        string line = text.Lines[first.Word.Line].Text;
        for (int i = 1; i < tied.Length; i++)
        {
            int end = tied[i - 1].Start + tied[i - 1].Length;
            if (tied[i].Start <= end || !line[end..tied[i].Start].All(ch => ch is '-' or '‐' or '‑' or '\'' or '’')) return null;
        }
        int length = tied[^1].Start + tied[^1].Length - tied[0].Start;
        return length <= 64 ? first.Word with { Text = line.Substring(tied[0].Start, length), Start = tied[0].Start, Length = length,
            Confidence = tied.Where(item => item.Confidence.HasValue).Select(item => item.Confidence).Min() } : null;
    }
}

public sealed record ReadingVisualWord(string Text, ScreenRect Bounds, int Line, int Start, int Length, double? Confidence = null);
public sealed record ReadingVisualLine(string Text, ScreenRect? Bounds, double? Confidence = null);
public sealed record ReadingVisualText(IReadOnlyList<ReadingVisualLine> Lines, IReadOnlyList<ReadingVisualWord> Words)
{
    public bool ValidWord(ReadingVisualWord word) => word.Line >= 0 && word.Line < Lines.Count && word.Text is not null
        && word.Length > 0 && EditorTextRangePolicy.IsValid(Lines[word.Line].Text, word.Start, word.Length)
        && Lines[word.Line].Text.AsSpan(word.Start, word.Length).SequenceEqual(word.Text)
        && ReadingVisualPolicy.Valid(word.Bounds) && Lines[word.Line].Bounds is { } line
        && !ReadingVisualPolicy.Intersect(line, word.Bounds).IsEmpty;

    /// <summary>Windows OCR supplies words in line order, but no character offsets or confidence.
    /// Anchor only an exact sequential occurrence in its original line; never repair text or search
    /// another line. Unalignable words retain no spatial authority.</summary>
    public static ReadingVisualText FromWindows(IReadOnlyList<ReadingOcrLine> lines, int width, int height)
    {
        if (width <= 0 || height <= 0 || lines.Count > 2048) throw new ArgumentException("Invalid OCR frame.");
        var output = new List<ReadingVisualLine>(); var words = new List<ReadingVisualWord>();
        var frame = new ScreenRect(0, 0, width, height); int count = 0, chars = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            var line = lines[index]; int offset = 0; ScreenRect bounds = default;
            int firstWord = words.Count; bool aligned = true;
            if (line.Text is null || (chars = checked(chars + line.Text.Length)) > 262144) throw new ArgumentException("Invalid OCR text.");
            foreach (var word in line.Words)
            {
                if (++count > 16384 || word.Text is null || word.Text.Length == 0) throw new ArgumentException("Invalid OCR words.");
                var b = word.Bounds; var box = new ScreenRect(b.X, b.Y, b.Width, b.Height);
                if (!ReadingVisualPolicy.Contains(frame, box)) throw new ArgumentException("Invalid OCR bounds.");
                bounds = ReadingVisualPolicy.Union(bounds, box);
                int start = line.Text.IndexOf(word.Text, offset, StringComparison.Ordinal);
                if (!aligned || start < 0 || line.Text[offset..start].EnumerateRunes().Any(Rune.IsLetterOrDigit)) { aligned = false; continue; }
                if (EditorTextRangePolicy.IsValid(line.Text, start, word.Text.Length)) words.Add(new(word.Text, box, index, start, word.Text.Length));
                offset = start + word.Text.Length;
            }
            if (!aligned) words.RemoveRange(firstWord, words.Count - firstWord);
            output.Add(new(line.Text, bounds.IsEmpty ? null : bounds));
        }
        return new(output, words);
    }
}
