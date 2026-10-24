using System.Text;
using static Cotabby.Core.Text.FoundationEditorTextData;

namespace Cotabby.Core.Text;

/// <summary>
/// Raw, forward UTF-16 matching for VocabularyPolicy.reconciledRanges. Literal units are compared
/// before folding at the same candidate position; a later literal occurrence must never leapfrog
/// an earlier folded one. Per-attempt cursors retain the original span through variable-length
/// folds, so neither half of a sharp-s expansion becomes a separate match. No locale/UI/OS state.
/// The measured Darwin boundary and its limits are documented in VOCABULARY_EDITOR_CURRENT.md.
/// </summary>
internal sealed class FoundationSubstringMatcher(string text, string needle, CancellationToken cancellationToken = default)
{
    private readonly bool _ascii = IsAscii(text) && IsAscii(needle);
    internal (int Start, int End)? FindNext(int start)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (needle.Length == 0 || start >= text.Length) return null;
        if (_ascii)
        {
            int found = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
            return found < 0 ? null : (found, found + needle.Length);
        }
        int first = AsciiLower(needle[0]);
        for (int index = start; index < text.Length; index++)
        {
            if (((index - start) & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            int candidate = AsciiLower(text[index]);
            if (candidate < 128 && first < 128 && candidate != first) continue;
            if (!InsideIndicConjunct(text, index, start, cancellationToken) && Match(text, needle, index, cancellationToken) is { } end) return (index, end);
        }
        return null;
    }

    private static int? Match(string text, string needle, int start, CancellationToken cancellationToken)
    {
        var source = new Cursor(text, start, source: true, cancellationToken); var query = new Cursor(needle, 0, source: false, cancellationToken);
        int compared = 0;
        while (!query.End)
        {
            if ((compared++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (source.End) return null;
            if (source.Character != query.Character)
            {
                if (source.Position > start)
                {
                    bool skipSource = !source.Buffered && IsExtend(source.CurrentScalar);
                    bool skipQuery = !query.Buffered && IsExtend(query.CurrentScalar);
                    if (skipSource != skipQuery)
                    {
                        if (skipSource) source.SkipScalar(); else query.SkipScalar();
                        continue;
                    }
                }
                if (!source.Buffered) source.FoldCluster();
                if (source.Character != query.Character && !query.Buffered) query.FoldCluster();
                if (source.Character != query.Character) return null;
            }
            source.Consume(); query.Consume();
        }
        if (source.Buffered)
        {
            if (!source.HasOnlyDiacriticTail()) return null;
            source.Drain();
        }
        // A Roman/Greek/Cyrillic base consumes its following extends. Other scripts retain
        // those units in the raw cursor; composed-range expansion happens after this search.
        if (source.Position < text.Length && Scalar(text, start, out _) < 0x0510)
        {
            int tail = 0;
            while (source.Position < text.Length && IsExtend(Scalar(text, source.Position, out _)))
            {
                if ((tail++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                source.SkipScalar();
            }
        }
        return source.Position > start ? source.Position : null;
    }

    /// <summary>
    /// One attempt owns two short-lived cursors. Position advances only after a complete fold
    /// buffer drains, protecting source offsets when one character expands to several units.
    /// Compatibility expansion belongs only to the source cursor; it is not an equivalence rule.
    /// </summary>
    private sealed class Cursor(string text, int start, bool source, CancellationToken cancellationToken)
    {
        internal int Position { get; private set; } = start;
        private string _buffer = "";
        private int _bufferPosition, _rawEnd;
        internal bool Buffered => _buffer.Length > 0;
        internal bool End => !Buffered && Position >= text.Length;
        internal int CurrentScalar => Scalar(text, Position, out _);
        internal int Character => Buffered ? _buffer[_bufferPosition] : Position < text.Length ? AsciiLower(text[Position]) : -1;
        internal void Consume()
        {
            if (Buffered) { if (++_bufferPosition == _buffer.Length) Drain(); }
            else Position++;
        }
        internal void SkipScalar() { _ = Scalar(text, Position, out int width); Position += width; }
        internal void Drain() { Position = _rawEnd; _buffer = ""; }
        internal void FoldCluster()
        {
            int value = Scalar(text, Position, out int width); int end = Position + width;
            string folded = Fold(value, text.Substring(Position, width), source);
            bool removeMarks = Scalar(folded, 0, out _) < 0x0510;
            var buffer = new StringBuilder(folded);
            int marks = 0;
            while (end < text.Length)
            {
                if ((marks++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                int mark = Scalar(text, end, out width); if (!IsExtend(mark)) break;
                if (!removeMarks) buffer.Append(text, end, width);
                end += width;
            }
            // Two equal high-surrogate units may have been compared literally before their low
            // units differ. Such a temporary raw cursor is legal; Normalize rejects that isolated
            // surrogate, so keep it literal. Public entry points reject malformed captured text.
            _buffer = value is >= 0xd800 and <= 0xdfff ? buffer.ToString() : buffer.ToString().Normalize(NormalizationForm.FormD);
            cancellationToken.ThrowIfCancellationRequested();
            _bufferPosition = 0; _rawEnd = end;
        }
        internal bool HasOnlyDiacriticTail()
        {
            if (Scalar(_buffer, 0, out _) >= 0x0510) return false;
            int tail = 0;
            for (int position = _bufferPosition; position < _buffer.Length;)
            {
                if ((tail++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                int value = Scalar(_buffer, position, out int width); if (!IsExtend(value)) return false;
                position += width;
            }
            return true;
        }
    }
}
