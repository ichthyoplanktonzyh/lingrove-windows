using System.Globalization;
using System.Buffers;
using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Immutable per-language index adapted directly from the pinned Mac recognizer. An offline
/// lexicon owner supplies explicit entries; each call reads one fixed translation and returns
/// original UTF-16 spans. No suffix inference, model generation, editor access or IO lives here.
/// </summary>
public sealed class LearningUnitRecognizer
{
    private static readonly HashSet<string> Slots = ["one's", "someone's", "someone", "somebody", "something"];
    private static readonly HashSet<string> Contractions =
        ["it's", "that's", "there's", "here's", "what's", "who's", "where's", "when's", "why's", "how's", "let's", "he's", "she's"];
    private static readonly HashSet<string> Possessives = ["my", "your", "his", "her", "its", "our", "their"];
    private static readonly HashSet<string> People = ["me", "you", "him", "her", "us", "them", "someone", "somebody"];
    private static readonly HashSet<string> Objects =
        ["it", "this", "that", "these", "those", "something", "anything", "everything", "nothing"];
    private readonly LearningLexiconEntry[] _entries;
    private readonly Dictionary<string, List<int>> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Pattern[]> _patterns;
    private readonly ILearningLexicalText _text;

    public LearningUnitRecognizer(IReadOnlyList<LearningLexiconEntry> entries, ILearningLexicalText text)
    {
        ArgumentNullException.ThrowIfNull(entries); ArgumentNullException.ThrowIfNull(text);
        _entries = entries.ToArray(); _text = text;
        var patterns = new List<Pattern>();
        for (var index = 0; index < _entries.Length; index++)
        {
            var entry = _entries[index];
            foreach (var form in new[] { entry.Headword }.Concat(entry.Forms))
            {
                var key = _text.Key(form);
                if (LearningUnitPolicy.ContainsWhitespace(entry.Headword) && key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2) continue;
                if (entry.Language is "zh" or "ja" or "ko" && StringInfo.ParseCombiningCharacters(key).Length < 2) continue;
                if (key.Length > 0)
                {
                    if (!_aliases.TryGetValue(key, out var indices)) _aliases[key] = indices = [];
                    indices.Add(index);
                }
            }
            foreach (var form in new[] { entry.Headword }.Concat(entry.Forms))
            {
                var tokens = _text.Key(form).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Any(Slots.Contains) && tokens.Count(token => !Slots.Contains(token)) >= 2)
                    patterns.Add(new Pattern(index, tokens));
            }
        }
        _patterns = patterns.OrderByDescending(pattern => pattern.Tokens.Length)
            .GroupBy(pattern => Slots.Contains(pattern.Tokens[0]) ? "*" : pattern.Tokens[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }

    public IReadOnlyList<LearningLexiconEntry> Lookup(string query) =>
        _aliases.TryGetValue(_text.Key(query), out var indices)
            ? Array.AsReadOnly(indices.Distinct().Order().Select(index => _entries[index]).ToArray())
            : Array.Empty<LearningLexiconEntry>();

    public IReadOnlyList<LearningUnit> Recognize(string value, LearningLanguage language)
    {
        var words = _text.Words(value, language).ToArray();
        ValidateWords(value, words);
        var units = new List<LearningUnit>();
        for (var index = 0; index < words.Length;)
        {
            (int End, LearningLexiconEntry Entry)? match = null;
            for (int end = index; end < Math.Min(words.Length, index + 12); end++)
            {
                var range = Extent(words[index], words[end]);
                var quote = value.Substring(range.Location, range.Length);
                if (LearningUnitPolicy.ContainsNewline(quote)) break;
                if (Lookup(quote).FirstOrDefault() is { } entry) match = (end, entry);
            }
            if (match is { } fixedMatch)
            {
                var parts = words[index..(fixedMatch.End + 1)];
                var range = Extent(parts[0], parts[^1]);
                var surface = value.Substring(range.Location, range.Length);
                units.Add(new LearningUnit(range.Location, surface, LearningUnitPolicy.CollapseWhitespace(surface),
                    LearningUnitKind.Expression, [range], parts, fixedMatch.Entry.Id));
                index = fixedMatch.End + 1;
                continue;
            }
            if (MatchPattern(index, words, value) is { } pattern)
            {
                units.Add(pattern.Unit);
                units.AddRange(pattern.Slots.Select(LearningUnit.Word));
                index += pattern.Length;
                continue;
            }
            units.Add(LearningUnit.Word(words[index++]));
        }
        return Array.AsReadOnly(units.OrderBy(unit => unit.Id).ToArray());
    }

    private PatternMatch? MatchPattern(int index, LearningDictionaryWord[] words, string value)
    {
        var first = _text.Key(words[index].Text);
        foreach (var pattern in Candidates(first))
        {
            if (pattern.Tokens.Length > words.Length - index) continue;
            var parts = words[index..(index + pattern.Tokens.Length)];
            var fixedRanges = new List<LearningTextRange>();
            var slots = new List<LearningDictionaryWord>();
            bool matches = true;
            for (int i = 0; i < parts.Length; i++)
            {
                var token = pattern.Tokens[i]; var word = parts[i]; var key = _text.Key(word.Text);
                if (token is "one's" or "someone's")
                {
                    if (Contractions.Contains(key) || !(Possessives.Contains(key) || key.EndsWith("'s", StringComparison.Ordinal)))
                    { matches = false; break; }
                    slots.Add(word);
                }
                else if (token is "someone" or "somebody")
                {
                    if (!People.Contains(key) && !_text.FirstIsUppercase(word.Text)) { matches = false; break; }
                    slots.Add(word);
                }
                else if (token == "something")
                {
                    if (!Objects.Contains(key)) { matches = false; break; }
                    slots.Add(word);
                }
                else if (token == key) fixedRanges.Add(word.Range);
                else { matches = false; break; }
            }
            var extent = Extent(parts[0], parts[^1]);
            var quote = value.Substring(extent.Location, extent.Length);
            if (!matches || fixedRanges.Count == 0 || LearningUnitPolicy.ContainsNewline(quote) || !_text.IsPatternText(quote)) continue;
            var ranges = new List<LearningTextRange>();
            foreach (var range in fixedRanges)
            {
                if (ranges.Count > 0)
                {
                    var last = ranges[^1];
                    var gap = value.Substring(last.End, range.Location - last.End);
                    if (LearningUnitPolicy.AllWhitespace(gap))
                    {
                        ranges[^1] = new LearningTextRange(last.Location, range.End - last.Location);
                        continue;
                    }
                }
                ranges.Add(range);
            }
            var entry = _entries[pattern.Entry];
            var surface = string.Join(" … ", ranges.Select(range => value.Substring(range.Location, range.Length)));
            return new PatternMatch(new LearningUnit(ranges[0].Location, surface, entry.Headword,
                LearningUnitKind.Pattern, ranges, parts, entry.Id), slots, parts.Length);
        }
        return null;
    }

    private IEnumerable<Pattern> Candidates(string first) =>
        (_patterns.GetValueOrDefault(first) ?? []).Concat(_patterns.GetValueOrDefault("*") ?? []);

    private static LearningTextRange Extent(LearningDictionaryWord first, LearningDictionaryWord last) =>
        new(first.Range.Location, last.Range.End - first.Range.Location);

    private static void ValidateWords(string value, IReadOnlyList<LearningDictionaryWord> words)
    {
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out int consumed) != OperationStatus.Done)
                throw new LearningLexicalException(LearningLexicalFailure.InvalidInput);
            remaining = remaining[consumed..];
        }
        var boundaries = StringInfo.ParseCombiningCharacters(value).ToHashSet();
        boundaries.Add(value.Length);
        int previousEnd = 0;
        foreach (var word in words)
        {
            var range = word.Range;
            if (range.Location < previousEnd || range.Length <= 0 || range.Location > value.Length
                || range.Length > value.Length - range.Location
                || !boundaries.Contains(range.Location) || !boundaries.Contains(range.End)
                || !string.Equals(word.Text, value.Substring(range.Location, range.Length), StringComparison.Ordinal))
                throw new LearningLexicalException(LearningLexicalFailure.InvalidWordRanges);
            previousEnd = range.End;
        }
    }
    private sealed record Pattern(int Entry, string[] Tokens);
    private sealed record PatternMatch(LearningUnit Unit, IReadOnlyList<LearningDictionaryWord> Slots, int Length);
}
