using System.Globalization;
using static Cotabby.Core.Text.FoundationEditorTextData;

namespace Cotabby.Core.Text;

/// <summary>
/// A caller-owned immutable map of one original string. NSString composed ranges are not a single
/// partition: in a letter-ZWJ-letter sequence, querying the first letter extends forward while
/// querying the last letter can stand alone. Keep expansion as an operation rather than pretending
/// one array of legal endpoints can validate every span. Capture, archive and presentation borrow
/// the same map for an operation; it holds no service, UI object or persistent cache.
/// </summary>
internal sealed class FoundationComposedText
{
    internal string Text { get; }
    private readonly int[] _baseBoundaries;
    private readonly bool _ascii;
    private readonly CancellationToken _cancellationToken;
    internal FoundationComposedText(string text, CancellationToken cancellationToken = default)
    {
        Text = text; _ascii = IsAscii(text); _cancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var boundaries = new SortedSet<int>(StringInfo.ParseCombiningCharacters(text)) { text.Length };
        int scalars = 0;
        for (int position = 0; position < text.Length;)
        {
            if ((scalars++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            int value = Scalar(text, position, out int width);
            if (!_ascii && IsPrepend(value))
            {
                int end = position + width;
                int marks = 0;
                while (end < text.Length && IsNonBase(Scalar(text, end, out int next)))
                {
                    if ((marks++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                    end += next;
                }
                boundaries.Add(position); boundaries.Add(end);
            }
            // NSString lets CR and LF be referenced separately, unlike extended graphemes.
            if (value == '\r' && position + 1 < text.Length && text[position + 1] == '\n') boundaries.Add(position + 1);
            position += width;
        }
        _baseBoundaries = boundaries.ToArray();
    }
    internal bool IsValid(int location, int length) => location >= 0 && length > 0 && location <= Text.Length && length <= Text.Length - location
        && Expand(location, location + length) == (location, location + length);

    internal (int Start, int End) Expand(int start, int end)
    {
        if (_ascii) return (start, end);
        var first = At(start); var last = At(end - 1);
        return (Math.Min(first.Start, last.Start), Math.Max(first.End, last.End));
    }
    private (int Start, int End) At(int position)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        int found = Array.BinarySearch(_baseBoundaries, position);
        if (found < 0) found = ~found - 1;
        int first = found, last = found + 1;
        bool StartsLetter(int index) => Text[index] == '\u200d' || IsLetter(Scalar(Text, index, out _));
        if (StartsLetter(_baseBoundaries[first]))
        {
            while (first > 0 && _baseBoundaries[first] > 1)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                int before = _baseBoundaries[first] - 1;
                bool joined = IsVirama(Text[before]) || Text[before] == '\u200d' && before > 0 && IsVirama(Text[before - 1]);
                if (!joined || !IsLetter(Scalar(Text, _baseBoundaries[first - 1], out _))) break;
                first--;
            }
            while (last < _baseBoundaries.Length - 1)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                int end = _baseBoundaries[last];
                if (end - _baseBoundaries[first] <= 1 || Text[end - 1] != '\u200d' && !IsVirama(Text[end - 1])
                    || !IsLetter(Scalar(Text, end, out _))) break;
                last++;
            }
        }
        return (_baseBoundaries[first], _baseBoundaries[last]);
    }
}
