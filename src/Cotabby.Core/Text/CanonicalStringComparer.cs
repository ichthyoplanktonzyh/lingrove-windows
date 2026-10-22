using System.Text;

namespace Cotabby.Core.Text;

/// <summary>
/// Swift String value equality compares canonical equivalents. This stateless comparer normalizes
/// only comparison/hash keys; published strings and their UTF-16 occurrence offsets stay untouched.
/// Malformed .NET strings have no Swift counterpart and retain ordinal equality instead of throwing.
/// </summary>
internal sealed class CanonicalStringComparer : IEqualityComparer<string>
{
    public static CanonicalStringComparer Instance { get; } = new();
    public bool Equals(string? left, string? right) => string.Equals(Key(left), Key(right), StringComparison.Ordinal);
    public int GetHashCode(string value) => StringComparer.Ordinal.GetHashCode(Key(value)!);
    private static string? Key(string? value)
    {
        try { return value?.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return value; }
    }
}
