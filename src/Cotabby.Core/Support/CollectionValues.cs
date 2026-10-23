using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Cotabby.Core.Text;

namespace Cotabby.Core.Support;

/// <summary>
/// Internal snapshot/value mechanics for the vocabulary/review graph. Domain records own the
/// resulting immutable collections; a draft cannot mutate them after publication. Swift arrays
/// are ordered, maps/sets are not, and optional absence differs from empty. String comparisons
/// use canonical equality without changing retained keys/values or silently merging ambiguous keys.
/// </summary>
internal static class CollectionValues
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T> source) => Array.AsReadOnly(source.ToArray());
    public static IReadOnlyList<T>? OptionalList<T>(IEnumerable<T>? source) => source is null ? null : List(source);
    public static IReadOnlySet<T>? OptionalSet<T>(IEnumerable<T>? source) => source?.ToFrozenSet(Comparer<T>());
    public static IReadOnlyDictionary<K, V> Map<K, V>(IEnumerable<KeyValuePair<K, V>> source) where K : notnull
    {
        var copy = new Dictionary<K, V>(Comparer<K>());
        foreach (var pair in source)
            if (!copy.TryAdd(pair.Key, pair.Value)) throw new ArgumentException("Snapshot dictionary has duplicate canonical keys.");
        return new ReadOnlyDictionary<K, V>(copy);
    }
    public static IReadOnlyDictionary<K, V>? OptionalMap<K, V>(IEnumerable<KeyValuePair<K, V>>? source) where K : notnull => source is null ? null : Map(source);
    public static bool SequenceEqual<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) => a is null ? b is null
        : b is not null && a.SequenceEqual(b, Comparer<T>());
    public static bool SetEqual<T>(IReadOnlySet<T>? a, IReadOnlySet<T>? b) => a is null ? b is null
        : b is not null && a.SetEquals(b);
    public static bool MapEqual<K, V>(IReadOnlyDictionary<K, V>? a, IReadOnlyDictionary<K, V>? b) where K : notnull =>
        a is null ? b is null : b is not null && a.Count == b.Count
            && a.All(pair => b.TryGetValue(pair.Key, out var value) && Comparer<V>().Equals(pair.Value, value));
    public static int SequenceHash<T>(IReadOnlyList<T>? values)
    {
        var hash = new HashCode(); hash.Add(values is not null);
        if (values is not null) foreach (var value in values) hash.Add(ElementHash(value));
        return hash.ToHashCode();
    }
    public static int SetHash<T>(IReadOnlySet<T>? values)
    {
        int items = 0; if (values is not null) foreach (var value in values) items ^= ElementHash(value);
        return HashCode.Combine(values is not null, values?.Count ?? 0, items);
    }
    public static int MapHash<K, V>(IReadOnlyDictionary<K, V>? values) where K : notnull
    {
        int items = 0;
        if (values is not null) foreach (var pair in values) items ^= HashCode.Combine(ElementHash(pair.Key), ElementHash(pair.Value));
        return HashCode.Combine(values is not null, values?.Count ?? 0, items);
    }
    public static bool Same(string? a, string? b) => CanonicalStringComparer.Instance.Equals(a, b);
    public static int StringHash(string? value) => value is null ? 0 : CanonicalStringComparer.Instance.GetHashCode(value);
    private static int ElementHash<T>(T value) => value is null ? 0 : Comparer<T>().GetHashCode(value);
    private static IEqualityComparer<T> Comparer<T>() => typeof(T) == typeof(string)
        ? (IEqualityComparer<T>)(object)CanonicalStringComparer.Instance : EqualityComparer<T>.Default;
}
