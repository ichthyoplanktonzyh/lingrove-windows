using Cotabby.Core.Text;

namespace Cotabby.Core.Spelling;

/// <summary>Immutable, bounded exact-prefix lookup built alongside a spelling dictionary.</summary>
public sealed class WordPrefixIndex
{
    public readonly record struct Candidate(string Word, long Frequency);
    private readonly Candidate[] _words;

    public WordPrefixIndex(string contents) => _words = FrequencyDictionaryFormat.Entries(contents)
        .Where(e => e.Count > 0 && new GraphemeString(e.Word).All(c => c.IsLetter))
        .Select(e => new Candidate(e.Word, e.Count)).OrderBy(e => e.Word, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<Candidate> Candidates(string prefix)
    {
        prefix = prefix.ToLowerInvariant();
        if (new GraphemeString(prefix).Count < 3) return [];
        int low = 0, high = _words.Length;
        while (low < high) { int middle = (low + high) / 2; if (string.CompareOrdinal(_words[middle].Word, prefix) < 0) low = middle + 1; else high = middle; }
        var matches = new List<Candidate>();
        while (low < _words.Length && _words[low].Word.StartsWith(prefix, StringComparison.Ordinal))
        {
            if (matches.Count >= 512) return [];
            if (_words[low].Word != prefix) matches.Add(_words[low]);
            low++;
        }
        return matches.OrderByDescending(c => c.Frequency).ThenBy(c => c.Word, StringComparer.Ordinal).Take(2).ToArray();
    }
}

/// <summary>Source fallback never changes typed letters or resolves ambiguous references.</summary>
public static class WordCompletionFallback
{
    public static string? Suffix(string prefix, IEnumerable<string> references, IReadOnlyList<WordPrefixIndex.Candidate> dictionary)
    {
        var typed = new GraphemeString(prefix);
        if (typed.Count < 3 || !typed.All(c => c.IsLetter)) return null;
        string lower = prefix.ToLowerInvariant();
        var matches = references.Where(w => new GraphemeString(w).Count > typed.Count && w.ToLowerInvariant().StartsWith(lower, StringComparison.Ordinal)).ToArray();
        string word;
        if (matches.Select(w => w.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Count() == 1)
            word = matches.OrderBy(w => w, StringComparer.Ordinal).First();
        else if (matches.Length != 0) return null;
        else if (dictionary.Count == 0 || dictionary.Count > 1 && (double)dictionary[0].Frequency < (double)dictionary[1].Frequency * 4) return null;
        else word = dictionary[0].Word;
        var chars = new GraphemeString(word);
        if (chars.Count <= typed.Count || !string.Equals(chars.Prefix(typed.Count), prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string suffix = word[chars.Offset(typed.Count)..];
        return prefix == prefix.ToUpperInvariant() ? suffix.ToUpperInvariant() : suffix;
    }
}
