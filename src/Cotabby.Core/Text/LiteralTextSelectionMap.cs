using System.Globalization;
using Cotabby.Core.Context;

namespace Cotabby.Core.Text;

/// <summary>
/// A control-owned immutable grapheme index for one literal document. Validate/segment once,
/// then binary-search mouse offsets and visible-line bounds without re-parsing a long explanation
/// on every drag/paint. Boundaries stay private; callers cannot mutate the index behind a receipt.
/// </summary>
public sealed class LiteralTextSelectionMap
{
    private readonly int[] _boundaries;
    private LiteralTextSelectionMap(string source) { Source = source; _boundaries = StringInfo.ParseCombiningCharacters(source); }
    public string Source { get; }
    public int ElementCount => _boundaries.Length;
    public int Boundary(int index) => index == ElementCount ? Source.Length : _boundaries[index];
    public int ElementAtOrBefore(int offset)
    {
        int index = Array.BinarySearch(_boundaries, offset);
        return index >= 0 ? index : Math.Max(0, ~index - 1);
    }
    public LiteralTextSelection? Select(int anchor, int extent)
    {
        if (anchor < 0 || extent < 0 || anchor > Source.Length || extent > Source.Length) return null;
        if (anchor == extent) return new(Source, anchor, 0);
        int first = Math.Min(anchor, extent), last = Math.Max(anchor, extent);
        int start = Boundary(ElementAtOrBefore(first));
        int end = Array.BinarySearch(_boundaries, last);
        end = end >= 0 ? last : Boundary(~end);
        return new(Source, start, end - start);
    }
    public static LiteralTextSelectionMap? Create(string source) => ClipboardTextCopyPolicy.IsValid(source) ? new(source) : null;
}
