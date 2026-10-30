using Cotabby.Core.Text;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Settings;

/// <summary>Locale folding is a platform value adapter; ranking remains portable and owns no UI,
/// settings or effects. The same adapter must prepare every field and query within one search.</summary>
public interface ISettingsSearchFolding { string Fold(string value); }

/// <summary>One immutable search observation. Value is a caller-owned navigation identity,
/// never a mutable setting value. Display text remains with the presentation owner.</summary>
public sealed class SettingsSearchItem<T>
{
    public SettingsSearchItem(T value, string title, IEnumerable<string> keywords, string group, string summary)
    { Value = value; Title = title; Keywords = Array.AsReadOnly(keywords.ToArray()); Group = group; Summary = summary; }
    public T Value { get; }
    public string Title { get; }
    public IReadOnlyList<string> Keywords { get; }
    public string Group { get; }
    public string Summary { get; }
}
public sealed record SettingsSearchMatch<T>(T Value, double Score);

/// <summary>Direct 3b SettingsSearchRanker adaptation: all-token admission, exact/prefix/word/
/// substring/subsequence tiers, title cohesion, exact-title bonus and stable declaration ties.
/// Swift Character counts and boundaries remain grapheme based; no UTF-16 length shortcuts.</summary>
public static class SettingsSearchRanker
{
    private sealed record Weights(double Exact, double Prefix, double Word, double Substring, double? Subsequence);
    private static readonly Weights Title = new(100, 90, 80, 60, 25), Keyword = new(70, 55, 50, 40, 12),
        Group = new(35, 30, 25, 20, null), Summary = new(30, 30, 30, 18, null);
    public static IReadOnlyList<SettingsSearchMatch<T>> Matches<T>(string query, IReadOnlyList<SettingsSearchItem<T>> items, ISettingsSearchFolding folding)
    {
        ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(items); ArgumentNullException.ThrowIfNull(folding);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        string Fold(string value) { if (!keys.TryGetValue(value, out var key)) keys[value] = key = folding.Fold(value); return key; }
        var trimmed = new GraphemeString(LearningExplanationText.Trim(query));
        var tokens = trimmed.Split(character => character.IsWhitespace).Take(8).Select(range => Fold(trimmed.Text[range])).Where(token => token.Length != 0).ToArray();
        if (tokens.Length == 0) return [];
        string joined = string.Join(" ", tokens);
        var results = new List<(int Offset, SettingsSearchMatch<T> Match)>();
        for (int offset = 0; offset < items.Count; offset++)
        {
            var item = items[offset];
            string title = Fold(item.Title), group = Fold(item.Group), summary = Fold(item.Summary);
            var keywords = item.Keywords.Select(Fold).ToArray(); double total = 0; int titleHits = 0;
            foreach (string token in tokens)
            {
                double best = FieldScore(token, title, Title) ?? 0; bool titleHit = best > 0;
                foreach (string keyword in keywords) Adopt(FieldScore(token, keyword, Keyword));
                Adopt(FieldScore(token, group, Group)); Adopt(FieldScore(token, summary, Summary));
                if (best <= 0) { total = 0; break; }
                total += best; if (titleHit) titleHits++;
                void Adopt(double? score) { if (score is { } value && value > best) { best = value; titleHit = false; } }
            }
            if (total <= 0) continue;
            if (titleHits == tokens.Length) total += 15;
            if (title == joined) total += 40;
            results.Add((offset, new(item.Value, total)));
        }
        return results.OrderByDescending(result => result.Match.Score).ThenBy(result => result.Offset).Select(result => result.Match).ToArray();
    }
    private static double? FieldScore(string token, string target, Weights weights)
    {
        if (target.Length == 0) return null;
        var query = new GraphemeString(token); var field = new GraphemeString(target);
        if (target == token) return weights.Exact;
        if (field.StartsWith(token)) return weights.Prefix;
        if (query.Count >= 4 && field.Count >= 4 && query.StartsWith(target)) return weights.Prefix;
        foreach (var range in field.Split(character => !character.IsLetter && !character.IsNumber))
        {
            var word = new GraphemeString(target[range]);
            if (word.StartsWith(token) || query.Count >= 4 && word.Count >= 4 && query.StartsWith(word.Text)) return weights.Word;
        }
        for (int index = 0; index <= field.Count - query.Count; index++)
        {
            int matched = 0;
            while (matched < query.Count && field[index + matched].Equals(query[matched])) matched++;
            if (matched == query.Count) return weights.Substring;
        }
        if (weights.Subsequence is { } subsequence && query.Count >= 3)
        {
            int next = 0;
            foreach (var character in field) { if (next < query.Count && query[next].Equals(character)) next++; }
            if (next == query.Count) return subsequence;
        }
        return null;
    }
}
