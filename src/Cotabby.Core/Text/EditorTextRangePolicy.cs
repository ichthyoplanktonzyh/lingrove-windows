using System.Globalization;

namespace Cotabby.Core.Text;

/// <summary>
/// Literal editor ranges at Mac 35b7fbe's EditorTextRangePolicy: Swift character boundaries and
/// NSString composed-range admission both apply. This never silently widens an authorized range.
/// FoundationComposedText retains its separately documented compatibility reference.
/// </summary>
public static class EditorTextRangePolicy
{
    public static bool IsValid(string? value, int location, int length)
    {
        if (value is null || location < 0 || length < 0 || location > value.Length || length > value.Length - location
            || !WellFormed(value)) return false;
        int[] boundaries = StringInfo.ParseCombiningCharacters(value);
        bool IsCharacterBoundary(int index) => index == value.Length || Array.BinarySearch(boundaries, index) >= 0;
        if (!IsCharacterBoundary(location) || !IsCharacterBoundary(location + length)) return false;
        var composed = FoundationTextRanges.Boundaries(value);
        if (length == 0) return location == value.Length || composed.Expand(location, location + 1).Start == location;
        return FoundationTextRanges.IsValid(location, length, value, composed);
    }

    private static bool WellFormed(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i])) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false; }
            else if (char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }
}
