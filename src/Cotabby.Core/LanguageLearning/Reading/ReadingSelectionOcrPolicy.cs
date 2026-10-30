using System.Text;
using Cotabby.Core.Focus;
using Cotabby.Core.Text;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualPolicy;
using static Cotabby.Core.LanguageLearning.Reading.ReadingGesturePolicy;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualLayout;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>3b SelectionOCRPolicy. OCR can enrich only one spatially proved occurrence of the
/// exact native selection. Canonical matching is an alignment map, never rewritten surrounding text.</summary>
public sealed class ReadingSelectionOcrPolicy(ReadingVisualLayout layout, ReadingVisualContext blocks,
    Func<string, IReadOnlyList<LearningTextRange>> tokenize)
{
    public sealed record Match(ReadingContext Context, ScreenRect Bounds, ScreenRect TargetBounds);
    public enum Reason { Matched, Ambiguous, InvalidGeometry, BudgetExceeded, NoSpatialWords, NoContextBlock, NoAlignedOccurrence }
    public sealed record Alignment(Reason Reason, Match? Match = null);
    private sealed record Candidate(Match Match, string Occurrence);
    public Alignment Align(ReadingSelection selection, ReadingVisualText extraction, ScreenRect region, int width, int height, double scale)
    {
        if (SearchBounds(selection, region, scale: scale) is not { } bounds) return new(Reason.InvalidGeometry);
        if (selection.Text.Length > ReadingContextPolicy.MaximumSelection || extraction.Lines.Count > 2048 || extraction.Words.Count > 16384) return new(Reason.BudgetExceeded);
        var seeds = extraction.Words.Where(word => !Intersect(Offset(word.Bounds, region), bounds).IsEmpty).ToArray();
        if (seeds.Length == 0) return new(Reason.NoSpatialWords);
        if (seeds.Length > 64) return new(Reason.BudgetExceeded);
        var rows = layout.Rows(extraction); var visited = new HashSet<int>(); var occurrences = new Dictionary<string, Match>(StringComparer.Ordinal); int count = 0;
        foreach (var word in seeds)
        {
            if (!visited.Add(word.Line) || blocks.Resolve(word, extraction, width, height, rows) is not { Context: not null } block
                || block.Confidence is double confidence && (!double.IsFinite(confidence) || confidence < .5)) continue;
            count++;
            foreach (var candidate in Candidates(selection, block, extraction, region, scale))
                if (!occurrences.TryGetValue(candidate.Occurrence, out var prior) || candidate.Match.Context.Text.Length > prior.Context.Text.Length)
                    occurrences[candidate.Occurrence] = candidate.Match;
        }
        if (occurrences.Count > 1) return new(Reason.Ambiguous);
        return occurrences.Count == 1 ? new(Reason.Matched, occurrences.Values.First()) : new(count == 0 ? Reason.NoContextBlock : Reason.NoAlignedOccurrence);
    }
    private IReadOnlyList<Candidate> Candidates(ReadingSelection selection, ReadingVisualContext.Block block, ReadingVisualText extraction, ScreenRect region, double scale)
    {
        var canonical = new Canonical(block.Window.Text); string target = FoundationText.TrimWhitespaceAndNewlines(new Canonical(selection.Text).Text);
        if (target.Length == 0) return [];
        var exact = canonical.Occurrences(target).Select(range => Resolve(selection, range, block, extraction, region, scale)).OfType<Candidate>().ToArray();
        if (exact.Length > 0 || AxBounds(selection, region, scale) is not { } bounds || target.Length > 64
            || !new GraphemeString(target).All(ch => ch.IsLetter) || selection.Context is not { } ax || !ValidTarget(ax, selection.Text) || !HasNeighbours(ax)) return exact;
        var result = new List<Candidate>();
        foreach (var word in extraction.Words)
        {
            if (!Agrees(Offset(word.Bounds, region), bounds, scale) || !extraction.ValidWord(word) || word.Length > 64
                || !new GraphemeString(word.Text).All(ch => ch.IsLetter || ch.IsNumber)) continue;
            var source = block.Sources.FirstOrDefault(source => source.Line == word.Line && word.Start >= source.OriginalStart && word.Start + word.Length <= source.OriginalStart + source.Length);
            if (source is null) continue;
            var range = new LearningTextRange(source.WindowStart + word.Start - source.OriginalStart, word.Length);
            if (!EditorTextRangePolicy.IsValid(block.Window.Text, range.Location, range.Length)
                || !block.Window.Text.AsSpan(range.Location, range.Length).SequenceEqual(word.Text)) continue;
            if (Resolve(selection, range, block, extraction, region, scale, true) is { } candidate) result.Add(candidate);
        }
        return result;
    }
    private Candidate? Resolve(ReadingSelection selection, LearningTextRange range, ReadingVisualContext.Block block,
        ReadingVisualText extraction, ScreenRect region, double scale, bool requiresQuote = false)
    {
        var window = block.Window;
        if (!EditorTextRangePolicy.IsValid(window.Text, range.Location, range.Length)) return null;
        int WindowOffset(ReadingVisualWord word) => block.Sources.Where(source => source.Line == word.Line && Overlap(source.OriginalStart, source.Length, word.Start, word.Length))
            .Select(source => source.WindowStart + Math.Max(word.Start, source.OriginalStart) - source.OriginalStart).DefaultIfEmpty(int.MaxValue).Min();
        var words = extraction.Words.Where(word => extraction.ValidWord(word) && block.Sources.Any(source =>
        {
            if (source.Line != word.Line) return false;
            int lower = Math.Max(source.OriginalStart, word.Start), upper = Math.Min(source.OriginalStart + source.Length, word.Start + word.Length);
            return upper > lower && Overlap(range.Location, range.Length, source.WindowStart + lower - source.OriginalStart, upper - lower);
        })).OrderBy(WindowOffset).ToArray();
        ScreenRect box = default; foreach (var word in words) box = Union(box, Offset(word.Bounds, region));
        string rawTarget = window.Text.Substring(range.Location, range.Length);
        bool introducesBreaks = !new GraphemeString(rawTarget).Any(ch => ch.IsNewline) && new GraphemeString(selection.Text).Any(ch => ch.IsNewline);
        var aligned = window with { Text = window.Text[..range.Location] + selection.Text + window.Text[(range.Location + range.Length)..],
            TargetStart = range.Location, TargetLength = selection.Text.Length, Method = ReadingContextMethod.SelectionOcr, TargetIntroducesLineBreaks = introducesBreaks };
        var context = ReadingContextPolicy.Resolve(aligned, selection.Text, tokenize); if (context is null) return null;
        var selected = AxBounds(selection, region, scale);
        if (requiresQuote)
        {
            if (selected is not { } bounds || !Agrees(box, bounds, scale) || selection.Context is not { } ax || !Compatible(ax, context)) return null;
        }
        else if (selected is { } bounds)
        {
            if (!Agrees(box, bounds, scale) && !CorroboratedRun(box, bounds, selection, context, scale)) return null;
        }
        else if (selection.Gesture is not { } gesture || words.Length == 0 || !ReadingGesturePolicy.Agrees(gesture, Offset(words[0].Bounds, region), Offset(words[^1].Bounds, region), scale)
            || selection.Context is { } previous && !Compatible(previous, context)) return null;
        string identity = string.Join("|", block.Sources.Select(source =>
        {
            int lower = Math.Max(source.WindowStart, range.Location), upper = Math.Min(source.WindowStart + source.Length, range.Location + range.Length);
            return upper > lower ? $"{source.Line}:{source.OriginalStart + lower - source.WindowStart}:{upper - lower}" : null;
        }).OfType<string>().Order(StringComparer.Ordinal));
        return identity.Length == 0 ? null : new(new(context, Offset(block.Bounds, region), box), identity);
    }
    private static ScreenRect? AxBounds(ReadingSelection selection, ScreenRect region, double scale) => selection.SelectionBounds is { } bounds
        && ValidSelectionBounds(bounds, scale) && SearchBounds(selection, region, scale: scale) == bounds ? bounds : null;
    private static ScreenRect Offset(ScreenRect local, ScreenRect region) => local with { X = local.X + region.X, Y = local.Y + region.Y };
    private static bool Agrees(ScreenRect text, ScreenRect selection, double scale)
    {
        if (!ValidSelectionBounds(text, scale) || !ValidSelectionBounds(selection, scale)) return false;
        var overlap = Intersect(text, Pad(selection, 3 * scale)); double area = Math.Min(text.Width * text.Height, selection.Width * selection.Height);
        return overlap.Width * overlap.Height >= area * .7 && text.Width <= selection.Width * 3 + 8 * scale && selection.Width <= text.Width * 1.5 + 8 * scale
            && text.Height <= selection.Height * 1.5 + 8 * scale && selection.Height <= text.Height * 2 + 8 * scale;
    }
    private static bool CorroboratedRun(ScreenRect text, ScreenRect bounds, ReadingSelection selection, ReadingContext context, double scale)
    {
        double pad = Math.Min(8 * scale, Math.Max(3 * scale, Math.Min(text.Height, bounds.Height) * .25));
        var padded = new ScreenRect(bounds.X - 3 * scale, bounds.Y - pad, bounds.Width + 6 * scale, bounds.Height + 2 * pad);
        return ValidSelectionBounds(text, scale) && selection.Context is { } ax && ValidTarget(ax, selection.Text) && Compatible(ax, context)
            && Contains(padded, text) && text.Height <= bounds.Height * 1.5 + 8 * scale && bounds.Height <= text.Height * 1.5 + 8 * scale && HasNeighbours(ax);
    }
    private static bool HasNeighbours(ReadingContext context)
    {
        string neighbours = new GraphemeString(context.Text[..context.TargetStart]).Suffix(32) + new GraphemeString(context.Text[(context.TargetStart + context.TargetLength)..]).Prefix(32);
        return new GraphemeString(neighbours).Count(ch => !ch.IsWhitespace) >= 8 && new GraphemeString(neighbours).Any(ch => ch.IsLetter);
    }
    private static bool ValidTarget(ReadingContext context, string expected)
    {
        if (context.Window is { } window) return window.TargetLength == expected.Length && window.TargetLength <= ReadingContextPolicy.MaximumSelection
            && window.Text.Length - window.TargetLength <= ReadingContextPolicy.MaximumSurrounding
            && EditorTextRangePolicy.IsValid(window.Text, window.TargetStart, window.TargetLength)
            && window.Text.AsSpan(window.TargetStart, window.TargetLength).SequenceEqual(expected);
        return EditorTextRangePolicy.IsValid(context.Text, context.TargetStart, context.TargetLength)
            && context.TargetLength == expected.Length && context.Text.AsSpan(context.TargetStart, context.TargetLength).SequenceEqual(expected);
    }
    private static bool Compatible(ReadingContext ax, ReadingContext visual)
    {
        if (!EditorTextRangePolicy.IsValid(ax.Text, ax.TargetStart, ax.TargetLength) || !EditorTextRangePolicy.IsValid(visual.Text, visual.TargetStart, visual.TargetLength)) return false;
        string Compact(string text) => string.Concat(new GraphemeString(FoundationUnicodeNormalization.Normalize(text)).Where(ch => !ch.IsWhitespace).Select(ch => ch.ToString()));
        string left = new GraphemeString(ax.Text[..ax.TargetStart]).Suffix(32), right = new GraphemeString(ax.Text[(ax.TargetStart + ax.TargetLength)..]).Prefix(32);
        return Compact(visual.Text[..visual.TargetStart]).EndsWith(Compact(left), StringComparison.Ordinal)
            && Compact(visual.Text[(visual.TargetStart + visual.TargetLength)..]).StartsWith(Compact(right), StringComparison.Ordinal);
    }
    public static ReadingContext? Preferred(ReadingContext? ax, ReadingContext? visual, string expected)
    {
        if (visual is null || !ValidTarget(visual, expected)) return ax;
        if (ax is null) return visual;
        if (Complete(ax) || !ValidTarget(ax, expected) || !Compatible(ax, visual)) return ax;
        return Complete(visual) || visual.Text.Length > ax.Text.Length ? visual : ax;
    }
    private sealed class Canonical
    {
        internal string Text { get; }
        private readonly List<LearningTextRange> _originals = [];
        internal Canonical(string raw)
        {
            var builder = new StringBuilder(); int offset = 0;
            foreach (var character in new GraphemeString(raw))
            {
                string literal = character.ToString(); var original = new LearningTextRange(offset, literal.Length); offset += literal.Length;
                if (character.IsWhitespace && builder.Length > 0 && builder[^1] == ' ')
                { var last = _originals[^1]; _originals[^1] = last with { Length = offset - last.Location }; continue; }
                string normalized = character.IsWhitespace ? " " : FoundationUnicodeNormalization.Normalize(literal); builder.Append(normalized);
                for (int i = 0; i < normalized.Length; i++) _originals.Add(original);
            }
            Text = builder.ToString();
        }
        internal IReadOnlyList<LearningTextRange> Occurrences(string target)
        {
            var result = new List<LearningTextRange>(); int offset = 0;
            while (offset < Text.Length && result.Count < 64)
            {
                int found = Text.IndexOf(target, offset, StringComparison.Ordinal); if (found < 0 || found + target.Length > _originals.Count) break;
                var first = _originals[found]; var last = _originals[found + target.Length - 1];
                result.Add(new(first.Location, last.Location + last.Length - first.Location)); offset = found + 1;
            }
            return result;
        }
    }
}
