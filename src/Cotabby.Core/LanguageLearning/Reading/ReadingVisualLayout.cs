using System.Text;
using System.Text.RegularExpressions;
using Cotabby.Core.Focus;
using Cotabby.Core.Text;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualPolicy;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>3b OCRColumns and OCRLayout. Row fragments retain exact original UTF-16 ranges.
/// Platform ICU script properties are supplied as evidence rather than guessed from language.</summary>
public sealed class ReadingVisualLayout(Func<int, int?> script)
{
    public sealed record Source(int OriginalStart, int Length, int RowStart);
    public sealed record Row(ReadingVisualLine Line, IReadOnlyDictionary<int, Source> Sources);
    private sealed record Fragment(ReadingVisualLine Line, int Index, int Start, int Length);
    private sealed record Anchor(ReadingVisualWord Word, ReadingVisualWord Previous, int Line);
    private static readonly Regex ListStart = new(@"^\s*(?:[•●▪‣*−-]|\d+[.)])\s+", RegexOptions.CultureInvariant);
    public static bool StartsListItem(string text) => ListStart.IsMatch(text);
    private int Script(Rune rune) => script(rune.Value) ?? throw new InvalidOperationException("OCR script evidence is unavailable.");
    private bool RightToLeft(string text)
    {
        foreach (var rune in text.EnumerateRunes()) if (Rune.IsLetter(rune)) return Script(rune) is 2 or 19;
        return false;
    }
    public string Separator(string left, string right)
    {
        var a = new GraphemeString(left).Last; var b = new GraphemeString(right).First;
        if (a is null || b is null || a.Value.IsWhitespace || b.Value.IsWhitespace || a.Value.ToString() == "-") return "";
        bool Cjk(Grapheme value) => value.ToString().EnumerateRunes().Count() == 1 && (value.ToString() is "，" or "。" or "！" or "？" or "、" or "」" or "』"
            || Script(value.Scalar) is 17 or 20 or 22);
        return Cjk(a.Value) && Cjk(b.Value) ? "" : " ";
    }
    public IReadOnlyList<Row> Rows(ReadingVisualText extraction)
    {
        var (fragments, repeated) = Split(extraction);
        var lines = fragments.Select(item => item.Line).ToArray();
        var observed = ColumnCuts(lines); if (observed is null) return [];
        var cuts = observed.Concat(repeated).ToArray();
        var unused = Enumerable.Range(0, lines.Length).ToHashSet(); var result = new List<Row>();
        for (int seed = 0; seed < lines.Length; seed++)
        {
            if (!unused.Remove(seed)) continue;
            var indices = new List<int> { seed };
            for (int pass = 0; pass < 16; pass++)
            {
                var next = unused.Where(index => Joins(lines[index], lines[indices[0]], cuts)
                    || Joins(lines[indices[^1]], lines[index], cuts)).OrderBy(index => lines[index].Bounds?.X ?? 0).ThenBy(index => index).Cast<int?>().FirstOrDefault();
                if (next is null) break;
                indices.Add(next.Value); unused.Remove(next.Value);
                indices.Sort((a, b) => (lines[a].Bounds?.X ?? 0).CompareTo(lines[b].Bounds?.X ?? 0));
            }
            if (RightToLeft(lines[seed].Text)) indices.Reverse();
            string text = ""; var sources = new Dictionary<int, Source>(); ScreenRect box = default;
            foreach (int index in indices)
            {
                text += Separator(text, lines[index].Text); var fragment = fragments[index];
                sources[fragment.Index] = new(fragment.Start, fragment.Length, text.Length);
                text += lines[index].Text; if (lines[index].Bounds is { } bounds) box = Union(box, bounds);
            }
            result.Add(new(new(text, box.IsEmpty ? null : box, MinimumConfidence(indices.Select(index => lines[index].Confidence))), sources));
        }
        return result;
    }
    private bool Joins(ReadingVisualLine left, ReadingVisualLine right, ScreenRect[] cuts)
    {
        if (left.Bounds is not { } a || right.Bounds is not { } b || !Valid(a) || !Valid(b) || RightToLeft(left.Text) != RightToLeft(right.Text)) return false;
        double height = Math.Min(a.Height, b.Height), overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y), gap = b.X - a.Right;
        // Mac's minY baseline becomes Bottom in the Windows top-left coordinate system.
        return overlap >= height * .65 && Math.Max(a.Height, b.Height) < height * 2
            && Math.Abs(a.Bottom - b.Bottom) <= height * .3 && gap >= 0 && gap <= height * .9
            && !cuts.Any(cut => a.Right <= cut.X + cut.Width / 2 && b.X >= cut.X + cut.Width / 2
                && a.Y + a.Height / 2 >= cut.Y && a.Y + a.Height / 2 <= cut.Bottom);
    }
    private static IReadOnlyList<ScreenRect>? ColumnCuts(ReadingVisualLine[] lines)
    {
        var boxes = lines.Where(line => line.Bounds is { } b && Valid(b)).Select(line => line.Bounds!.Value).ToArray();
        var gaps = new List<ScreenRect>();
        foreach (var first in boxes) foreach (var second in boxes)
        {
            if (second.X < first.Right) continue;
            double height = Math.Min(first.Height, second.Height), gap = second.X - first.Right;
            double overlap = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Y, second.Y);
            if (gap >= height * .5 && gap <= height * .9 && overlap >= height * .65 && Math.Max(first.Height, second.Height) < height * 1.6)
            {
                gaps.Add(new(first.Right, first.Y + first.Height / 2 - height / 2, gap, height));
                if (gaps.Count > 2048) return null;
            }
        }
        var result = new List<ScreenRect>();
        foreach (var gap in gaps)
        {
            var other = gaps.Cast<ScreenRect?>().FirstOrDefault(item => item is { } b && Math.Abs(CenterY(b) - CenterY(gap)) > Math.Max(b.Height, gap.Height)
                && Math.Abs(CenterY(b) - CenterY(gap)) <= Math.Max(b.Height, gap.Height) * 4 && Math.Max(b.X, gap.X) < Math.Min(b.Right, gap.Right));
            if (other is not { } box) continue;
            var band = Union(gap, box);
            result.Add(new((Math.Max(box.X, gap.X) + Math.Min(box.Right, gap.Right)) / 2, band.Y, 0, band.Height));
        }
        return result;
    }
    private static (IReadOnlyList<Fragment> Fragments, IReadOnlyList<ScreenRect> Cuts) Split(ReadingVisualText extraction)
    {
        var grouped = extraction.Words.Where(extraction.ValidWord).GroupBy(word => word.Line)
            .ToDictionary(group => group.Key, group => group.OrderBy(word => word.Start).ToArray());
        var cuts = RepeatedCuts(grouped); var fragments = new List<Fragment>();
        for (int index = 0; index < extraction.Lines.Count; index++)
        {
            var line = extraction.Lines[index]; var words = grouped.GetValueOrDefault(index) ?? [];
            var offsets = new HashSet<int> { 0, line.Text.Length };
            foreach (var cut in cuts)
            {
                if (line.Bounds is not { } bounds || bounds.Bottom < cut.Y || bounds.Y > cut.Bottom) continue;
                for (int i = 1; i < words.Length; i++) if (words[i - 1].Bounds.Right <= cut.X && words[i].Bounds.X >= cut.X)
                { offsets.Add(words[i].Start); break; }
            }
            var sorted = offsets.Order().ToArray();
            if (sorted.Length == 2) { fragments.Add(new(line, index, 0, line.Text.Length)); continue; }
            for (int i = 1; i < sorted.Length; i++)
            {
                string raw = line.Text[sorted[i - 1]..sorted[i]], trimmed = FoundationText.TrimWhitespaceAndNewlines(raw);
                if (trimmed.Length == 0) continue;
                var graphemes = new GraphemeString(raw); int start = sorted[i - 1] + graphemes.Offset(graphemes.PrefixLength(ch => ch.IsWhitespace));
                ScreenRect box = default;
                foreach (var word in words.Where(word => Overlap(start, trimmed.Length, word.Start, word.Length))) box = Union(box, word.Bounds);
                fragments.Add(new(new(trimmed, box.IsEmpty ? line.Bounds : box, line.Confidence), index, start, trimmed.Length));
            }
        }
        return (fragments, cuts);
    }
    private static IReadOnlyList<ScreenRect> RepeatedCuts(Dictionary<int, ReadingVisualWord[]> grouped)
    {
        var anchors = new Dictionary<string, List<Anchor>>(CanonicalStringComparer.Instance);
        foreach (var (line, words) in grouped)
        {
            if (words.Length < 5) continue;
            for (int i = 2; i < words.Length - 2; i++)
            {
                string phrase = string.Join(" ", words.Skip(i).Take(3).Select(word => word.Text));
                if (phrase.EnumerateRunes().Count(Rune.IsLetter) < 12 || words[i - 1].Bounds.Right >= words[i].Bounds.X) continue;
                if (!anchors.TryGetValue(phrase, out var group)) anchors[phrase] = group = [];
                group.Add(new(words[i], words[i - 1], line));
            }
        }
        var cuts = new List<ScreenRect>();
        foreach (var group in anchors.Values.Where(group => group.Count is >= 2 and <= 128)) foreach (var anchor in group)
        {
            var box = anchor.Word.Bounds;
            var other = group.FirstOrDefault(other => other.Line != anchor.Line && !CanonicalStringComparer.Instance.Equals(other.Previous.Text, anchor.Previous.Text)
                && Math.Abs(CenterY(other.Word.Bounds) - CenterY(box)) > Math.Max(other.Word.Bounds.Height, box.Height) * .8
                && Math.Abs(CenterY(other.Word.Bounds) - CenterY(box)) <= Math.Max(other.Word.Bounds.Height, box.Height) * 4
                && Math.Abs(other.Word.Bounds.X - box.X) <= Math.Max(3, Math.Min(other.Word.Bounds.Height, box.Height) * .3));
            if (other is null) continue;
            double left = Math.Max(anchor.Previous.Bounds.Right, other.Previous.Bounds.Right), right = Math.Min(box.X, other.Word.Bounds.X);
            if (left < right) { var band = Union(box, other.Word.Bounds); cuts.Add(new((left + right) / 2, band.Y, 0, band.Height)); }
        }
        return cuts.OrderBy(cut => cut.X).ThenBy(cut => cut.Y).ToArray();
    }
    internal static double? MinimumConfidence(IEnumerable<double?> values) => values.Where(value => value.HasValue).DefaultIfEmpty(null).Min();
    internal static bool Overlap(int a, int length, int b, int otherLength) => a < b + otherLength && b < a + length;
    internal static double CenterY(ScreenRect box) => box.Y + box.Height / 2;
}
