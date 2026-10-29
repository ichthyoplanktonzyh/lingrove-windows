using System.Text;
using System.Text.RegularExpressions;
using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>Bounded, already anchored acquisition evidence. This value never contains a native handle.</summary>
public sealed record ReadingAcquisitionWindow(string Text, int TargetStart, int TargetLength, ReadingBoundary Left,
    ReadingBoundary Right, ReadingContextMethod Method, bool TargetIntroducesLineBreaks = false);

/// <summary>Adapts 3b LearningReadingTargetPolicy and ContextPolicy. Alignment is confined to
/// the proved occurrence; sentence evidence comes from a platform tokenizer, never a text search.</summary>
public static class ReadingContextPolicy
{
    public const int MaximumSelection = 3072, Surrounding = 512, MaximumSurrounding = 1024;
    private static readonly Regex Terminator = new("[.!?。！？…][\"'”’»）」』】\\]\\)]*\\s*$", RegexOptions.CultureInvariant);
    public static LearningTextRange? WindowRange(int start, int length, int count)
    {
        if (start < 0 || length <= 0 || length > MaximumSelection || start > count || length > count - start) return null;
        int before = Math.Min(Surrounding, start); int after = Math.Min(MaximumSurrounding - before, count - start - length);
        before = Math.Min(MaximumSurrounding - after, start); return new(start - before, before + length + after);
    }
    public static bool Equivalent(string actual, string expected)
    {
        if (actual.Length > MaximumSelection || expected.Length > MaximumSelection) return false;
        try { var a = Canonical(actual); var b = Canonical(expected); return a.Length != 0 && string.Equals(a, b, StringComparison.Ordinal); }
        catch (ArgumentException) { return false; }
    }
    private static string Canonical(string value)
    {
        var chars = new GraphemeString(FoundationUnicodeNormalization.Normalize(value)); var builder = new StringBuilder(); bool space = false;
        foreach (var ch in chars) { if (ch.IsWhitespace) { if (builder.Length > 0) space = true; } else { if (space) builder.Append(' '); builder.Append(ch.ToString()); space = false; } }
        return builder.ToString();
    }
    public static ReadingAcquisitionWindow? Align(ReadingAcquisitionWindow window, string expected)
    {
        if (window.Text is null || !EditorTextRangePolicy.IsValid(window.Text, window.TargetStart, window.TargetLength)
            || window.Text.Length - window.TargetLength > MaximumSurrounding || expected.Length > MaximumSelection) return null;
        string actual = window.Text.Substring(window.TargetStart, window.TargetLength);
        if (!Equivalent(actual, expected)) return null;
        bool line = !new GraphemeString(actual).Any(g => g.IsNewline) && new GraphemeString(expected).Any(g => g.IsNewline);
        return window with { Text = window.Text[..window.TargetStart] + expected + window.Text[(window.TargetStart + window.TargetLength)..],
            TargetLength = expected.Length, TargetIntroducesLineBreaks = window.TargetIntroducesLineBreaks || line };
    }
    public static ReadingContext? Resolve(ReadingAcquisitionWindow original, string expected, Func<string, IReadOnlyList<LearningTextRange>> tokenize)
    {
        ArgumentNullException.ThrowIfNull(tokenize);
        if (expected.Length == 0 || expected.Length > MaximumSelection || original.Text is null
            || original.Text.Length - original.TargetLength > MaximumSurrounding
            || original.TargetLength != expected.Length || !EditorTextRangePolicy.IsValid(original.Text, original.TargetStart, original.TargetLength)
            || !original.Text.AsSpan(original.TargetStart, original.TargetLength).SequenceEqual(expected.AsSpan())) return null;
        var window = TrimArtificialEdges(original); string text = window.Text;
        string trimmed = FoundationText.TrimWhitespaceAndNewlines(expected);
        if (trimmed.Length == 0) return null;
        var graphemes = new GraphemeString(expected); int leading = graphemes.Offset(graphemes.PrefixLength(g => g.IsWhitespace));
        int targetStart = window.TargetStart + leading, targetEnd = targetStart + trimmed.Length;
        if (!EditorTextRangePolicy.IsValid(text, targetStart, trimmed.Length)) return null;
        if (FoundationText.TrimWhitespaceAndNewlines(text) == trimmed && !new GraphemeString(trimmed).Any(g => g.IsWhitespace) && !HasTerminator(trimmed)) return null;
        string tokenizeText = text;
        if (window.TargetIntroducesLineBreaks)
        {
            var mask = new StringBuilder(); foreach (var ch in new GraphemeString(expected)) mask.Append(ch.IsNewline ? new string(' ', ch.ToString().Length) : ch.ToString());
            tokenizeText = text[..window.TargetStart] + mask + text[(window.TargetStart + window.TargetLength)..];
        }
        var ranges = tokenize(tokenizeText); int previous = 0; var matched = new List<LearningTextRange>();
        foreach (var range in ranges)
        {
            if (range.Location < previous || range.Length <= 0 || range.Location > text.Length || range.Length > text.Length - range.Location) return null;
            previous = range.Location + range.Length;
            if (range.Location < targetEnd && previous > targetStart) matched.Add(range);
        }
        if (matched.Count == 0) return null;
        int begin = Math.Min(matched[0].Location, targetStart), end = Math.Max(matched[^1].Location + matched[^1].Length, targetEnd);
        if (!EditorTextRangePolicy.IsValid(text, begin, end - begin)) return null;
        string prefix = text[..begin], suffix = text[end..], scoped = text[begin..end];
        var left = HasTerminator(prefix) ? ReadingBoundary.Sentence : FoundationText.TrimWhitespaceAndNewlines(prefix).Length == 0 ? window.Left : ReadingBoundary.Unknown;
        var right = HasTerminator(scoped) && (FoundationText.TrimWhitespaceAndNewlines(suffix).Length != 0 || Complete(window.Right)) ? ReadingBoundary.Sentence
            : FoundationText.TrimWhitespaceAndNewlines(suffix).Length == 0 ? window.Right : ReadingBoundary.Unknown;
        return new(scoped, targetStart - begin, trimmed.Length, left, right, window.Method);
    }
    private static bool HasTerminator(string text) => Terminator.IsMatch(text);
    private static bool Complete(ReadingBoundary boundary) => boundary is ReadingBoundary.Container or ReadingBoundary.Sentence;
    private static ReadingAcquisitionWindow TrimArtificialEdges(ReadingAcquisitionWindow window)
    {
        if (window.Method is not (ReadingContextMethod.Characters or ReadingContextMethod.MarkerWalk)) return window;
        var chars = new GraphemeString(window.Text); int lower = 0, upper = window.Text.Length;
        if (window.Left == ReadingBoundary.Crop && chars.Count > 0 && chars.Offset(1) <= window.TargetStart) lower = chars.Offset(1);
        if (window.Right == ReadingBoundary.Crop && chars.Count > 0 && chars.Offset(chars.Count - 1) >= window.TargetStart + window.TargetLength) upper = chars.Offset(chars.Count - 1);
        return window with { Text = window.Text[lower..upper], TargetStart = window.TargetStart - lower };
    }
}
