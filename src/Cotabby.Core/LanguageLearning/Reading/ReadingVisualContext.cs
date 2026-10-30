using Cotabby.Core.Focus;
using Cotabby.Core.Text;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualPolicy;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualLayout;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>3b OCRPolicy: reconstruct only adjacent compatible rows around one proved word.
/// Exact original line ranges survive joining, clipping and sentence resolution for selection alignment.</summary>
public sealed class ReadingVisualContext(ReadingVisualLayout layout, Func<string, IReadOnlyList<LearningTextRange>> tokenize)
{
    public sealed record Source(int Line, int OriginalStart, int Length, int WindowStart);
    public sealed record Block(ReadingContext? Context, ScreenRect Bounds, IReadOnlyList<Source> Sources, ReadingAcquisitionWindow Window);
    public Block? Resolve(ReadingVisualWord word, ReadingVisualText extraction, int width, int height, IReadOnlyList<Row>? prepared = null)
    {
        if (width <= 0 || height <= 0 || extraction.Lines.Count > 2048 || !extraction.ValidWord(word)) return null;
        var rows = prepared ?? layout.Rows(extraction);
        int index = -1; Source? anchor = null;
        for (int i = 0; i < rows.Count; i++) if (rows[i].Sources.TryGetValue(word.Line, out var source)
            && word.Start >= source.OriginalStart && word.Start + word.Length <= source.OriginalStart + source.Length)
        { index = i; anchor = new(word.Line, source.OriginalStart, source.Length, source.RowStart); break; }
        if (index < 0 || anchor is null) return null;
        var lines = rows.Select(row => row.Line).ToArray();
        int start = anchor.WindowStart + word.Start - anchor.OriginalStart;
        int budget = ReadingContextPolicy.MaximumSurrounding - (lines[index].Text.Length - word.Text.Length);
        if (budget < 0)
        {
            string text = lines[index].Text;
            var requested = ReadingContextPolicy.WindowRange(start, word.Length, text.Length);
            if (requested is null) return null;
            var graphemes = new GraphemeString(text);
            int lower = requested.Value.Location, upper = lower + requested.Value.Length;
            var edges = Enumerable.Range(0, graphemes.Count + 1).Select(graphemes.Offset).ToArray();
            lower = edges.First(edge => edge >= lower); upper = edges.Last(edge => edge <= upper);
            if (lower > start || upper < start + word.Length) return null;
            var window = new ReadingAcquisitionWindow(text[lower..upper], start - lower, word.Length, ReadingBoundary.Crop, ReadingBoundary.Crop, ReadingContextMethod.Ocr);
            return new(ReadingContextPolicy.Resolve(window, word.Text, tokenize), extraction.Lines[word.Line].Bounds!.Value,
                Sources(rows[index], 0, lower, upper - lower), window);
        }
        var included = Connected(lines, index, budget); string joined = ""; int target = 0; var sources = new List<Source>(); ScreenRect bounds = default;
        foreach (int current in included)
        {
            joined += layout.Separator(joined, lines[current].Text);
            if (current == index) target = joined.Length + start;
            sources.AddRange(Sources(rows[current], joined.Length, 0, lines[current].Text.Length));
            joined += lines[current].Text; if (lines[current].Bounds is { } box) bounds = Union(bounds, box);
        }
        var boxes = included.Where(i => lines[i].Bounds.HasValue).Select(i => lines[i].Bounds!.Value).ToArray();
        bool horizontal = boxes.Any(box => box.X <= 3 || box.Right >= width - 3);
        var left = boxes.Length == 0 || !horizontal && boxes[0].Y > 3 ? ReadingBoundary.Unknown : ReadingBoundary.Crop;
        var right = boxes.Length == 0 || !horizontal && boxes[^1].Bottom < height - 3 ? ReadingBoundary.Unknown : ReadingBoundary.Crop;
        var acquisition = new ReadingAcquisitionWindow(joined, target, word.Length, left, right, ReadingContextMethod.Ocr);
        return new(ReadingContextPolicy.Resolve(acquisition, word.Text, tokenize), bounds, sources, acquisition);
    }
    private static IReadOnlyList<Source> Sources(Row row, int offset, int start, int length)
    {
        var result = new List<Source>();
        foreach (var (index, source) in row.Sources)
        {
            int lower = Math.Max(source.RowStart, start), upper = Math.Min(source.RowStart + source.Length, start + length);
            if (upper > lower) result.Add(new(index, source.OriginalStart + lower - source.RowStart, upper - lower, offset + lower - start));
        }
        return result;
    }
    private static IReadOnlyList<int> Connected(ReadingVisualLine[] lines, int index, int budget)
    {
        var included = new List<int> { index }; int[] edges = [index, index]; bool[] active = [true, true];
        double? pitch = TypicalPitch(lines, index);
        for (int pass = 0; pass < 8; pass++) for (int side = 0; side < 2; side++)
        {
            if (!active[side] || lines[edges[side]].Bounds is not { } edge) continue;
            var next = Enumerable.Range(0, lines.Length).Where(candidate => !included.Contains(candidate) && lines[candidate].Bounds is { } b
                && (side == 0 ? CenterY(b) < CenterY(edge) : CenterY(b) > CenterY(edge))
                && Math.Abs(CenterY(b) - CenterY(edge)) > Math.Max(edge.Height, b.Height) * .6
                && Math.Abs(edge.X - b.X) < Math.Max(edge.Height, b.Height) * 3
                && Math.Max(0, Math.Min(edge.Right, b.Right) - Math.Max(edge.X, b.X)) >= Math.Min(edge.Width, b.Width) * .6)
                .OrderBy(candidate => Math.Abs(CenterY(lines[candidate].Bounds!.Value) - CenterY(edge))).Cast<int?>().FirstOrDefault();
            if (next is null || !Continuous(lines[edges[side]], lines[next.Value], pitch) || lines[next.Value].Text.Length + 1 > budget)
            { active[side] = false; continue; }
            if (side == 0) included.Insert(0, next.Value); else included.Add(next.Value);
            budget -= lines[next.Value].Text.Length + 1; edges[side] = next.Value;
        }
        return included;
    }
    private static bool Continuous(ReadingVisualLine first, ReadingVisualLine second, double? pitch)
    {
        if (first.Bounds is not { } a || second.Bounds is not { } b || a.Height <= 0 || b.Height <= 0) return false;
        bool order = CenterY(a) < CenterY(b);
        var upper = order ? a : b; var lower = order ? b : a;
        var upperLine = order ? first : second; var lowerLine = order ? second : first;
        if (StartsListItem(lowerLine.Text) || ReadingContextPolicy.HasTerminator(upperLine.Text) && lower.X - upper.X > upper.Height * .8) return false;
        double distance = Math.Abs(CenterY(a) - CenterY(b));
        if (pitch is double p && distance > p * 1.45) return false;
        if (pitch is null && lower.Y - upper.Bottom > Math.Min(upper.Height, lower.Height) * .85 && new GraphemeString(upperLine.Text).Count < 40) return false;
        if (StartsListItem(upperLine.Text) && lower.X - upper.X < upper.Height * .3) return false;
        double height = Math.Max(a.Height, b.Height), smaller = Math.Min(a.Height, b.Height);
        double overlap = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X));
        bool variation = height / smaller < 1.8 && lower.Y - upper.Bottom < smaller * .25 && Math.Abs(a.X - b.X) < smaller * .5
            && Math.Min(a.Width, b.Width) > Math.Max(a.Width, b.Width) * .8;
        return (height / smaller < 1.6 || variation) && distance > height * .6 && distance < smaller * 2.2
            && Math.Abs(a.X - b.X) < height * 2 && overlap >= Math.Min(a.Width, b.Width) * .65;
    }
    private static double? TypicalPitch(ReadingVisualLine[] lines, int index)
    {
        if (lines[index].Bounds is not { } reference) return null;
        var boxes = lines.Where(line => line.Bounds is { } box && Math.Min(box.Height, reference.Height) > 0
            && Math.Max(box.Height, reference.Height) < Math.Min(box.Height, reference.Height) * 1.4
            && Math.Abs(box.X - reference.X) < reference.Height * 2 && Math.Abs(CenterY(box) - CenterY(reference)) < reference.Height * 12)
            .Select(line => line.Bounds!.Value).OrderBy(CenterY).ToArray();
        var pitches = boxes.Zip(boxes.Skip(1), (a, b) => new { Distance = CenterY(b) - CenterY(a), A = a, B = b })
            .Where(pair => pair.Distance > Math.Max(pair.A.Height, pair.B.Height) * .8 && pair.Distance < Math.Min(pair.A.Height, pair.B.Height) * 3)
            .Select(pair => pair.Distance).Order().ToArray();
        return pitches.Length >= 2 ? pitches[(pitches.Length - 1) / 2] : null;
    }
}
