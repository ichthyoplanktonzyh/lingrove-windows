using System.Text;

namespace Cotabby.Core.Text;

/// <summary>
/// Swift String value equality compares canonical equivalents. This stateless comparer normalizes
/// only comparison/hash keys; published strings and their UTF-16 occurrence offsets stay untouched.
/// Malformed .NET strings have no Swift counterpart and retain ordinal equality instead of throwing.
/// </summary>
internal sealed class CanonicalStringComparer : IEqualityComparer<string>, IComparer<string>
{
    public static CanonicalStringComparer Instance { get; } = new();
    public bool Equals(string? left, string? right) => string.Equals(Key(left), Key(right), StringComparison.Ordinal);
    public int GetHashCode(string value) => StringComparer.Ordinal.GetHashCode(Key(value)!);
    public int Compare(string? left, string? right)
    {
        if (left is null || right is null) return string.CompareOrdinal(left, right);
        string a = Key(left, out bool validA)!, b = Key(right, out bool validB)!;
        // A pair-dependent ordinal fallback is not transitive against valid NFC scalar order.
        // Rank malformed .NET strings after all valid Unicode, then order that category ordinally.
        // They have no Swift counterpart; a future archive validator rejects them before storage.
        if (validA != validB) return validA ? -1 : 1;
        if (!validA) return string.CompareOrdinal(a, b);
        // Swift String ordering uses canonical scalar order. UTF-16 ordinal order would place
        // supplementary emoji before BMP private-use characters and reorder merged notebook tags.
        var x = a.EnumerateRunes().GetEnumerator(); var y = b.EnumerateRunes().GetEnumerator();
        while (x.MoveNext())
        {
            if (!y.MoveNext()) return 1;
            int order = x.Current.Value.CompareTo(y.Current.Value);
            if (order != 0) return order;
        }
        return y.MoveNext() ? -1 : 0;
    }
    private static string? Key(string? value) => Key(value, out _);
    private static string? Key(string? value, out bool valid)
    {
        try { var key = value?.Normalize(NormalizationForm.FormC); valid = true; return key; }
        catch (ArgumentException) { valid = false; return value; }
    }
}
