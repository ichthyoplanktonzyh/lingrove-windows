using System.Globalization;

namespace Cotabby.Core.LanguageLearning;

/// <summary>A literal original-text slice; annotations never add spaces or replace the saved translation.</summary>
public sealed record LearningUnitTextRun(string Text, LearningTextRange Range, bool Underlined,
    LearningUnit? Target, LearningDictionaryWord? Word);

/// <summary>
/// Adapts LearningDictionaryTextView.present and its word-first hit policy to WPF inline runs.
/// Expressions have neutral fixed-range underlines; only exact word slices can carry a double-click
/// target. Blank/punctuation slices stay inert, and malformed provider ranges fail closed to text.
/// </summary>
public static class LearningUnitDisplayPolicy
{
    public static IReadOnlyList<LearningUnitTextRun> Runs(string text, IReadOnlyList<LearningUnit> units)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text).ToHashSet(); boundaries.Add(text.Length);
        bool Valid(LearningTextRange range) => range.Location >= 0 && range.Length > 0
            && range.Location <= text.Length && range.Length <= text.Length - range.Location
            && boundaries.Contains(range.Location) && boundaries.Contains(range.End);
        var validUnits = units.Where(unit => unit.Ranges.All(Valid) && unit.Words.All(word => Valid(word.Range)
            && string.Equals(word.Text, text.Substring(word.Range.Location, word.Range.Length), StringComparison.Ordinal))).ToArray();
        var ranges = validUnits.SelectMany(unit => unit.Ranges).OrderBy(range => range.Location).ToArray();
        for (int index = 1; index < ranges.Length; index++)
            if (ranges[index - 1].End > ranges[index].Location)
                return [new(text, new(0, text.Length), false, null, null)];
        var underlined = new bool[text.Length];
        var words = new Dictionary<LearningTextRange, (LearningDictionaryWord Word, LearningUnit Target)>();
        var cuts = new SortedSet<int> { 0, text.Length };
        foreach (var unit in validUnits)
        {
            foreach (var range in unit.Ranges)
            {
                cuts.Add(range.Location); cuts.Add(range.End);
                if (unit.Kind != LearningUnitKind.Word) Array.Fill(underlined, true, range.Location, range.Length);
            }
            foreach (var word in unit.Words)
            {
                // A pattern owns only its fixed ranges; separately emitted slot-word units own
                // their own click targets. This prevents clicking a slot from querying the pattern.
                if (!unit.Ranges.Any(range => range.Location <= word.Range.Location && range.End >= word.Range.End)) continue;
                words.TryAdd(word.Range, (word, unit));
                cuts.Add(word.Range.Location); cuts.Add(word.Range.End);
            }
        }
        var orderedWords = words.OrderBy(pair => pair.Key.Location).ToArray();
        for (int index = 1; index < orderedWords.Length; index++)
            if (orderedWords[index - 1].Key.End > orderedWords[index].Key.Location)
                return [new(text, new(0, text.Length), false, null, null)];
        var points = cuts.ToArray(); var runs = new List<LearningUnitTextRun>(); int wordIndex = 0;
        for (int index = 0; index < points.Length - 1; index++)
        {
            int start = points[index], end = points[index + 1];
            while (wordIndex < orderedWords.Length && orderedWords[wordIndex].Key.End <= start) wordIndex++;
            var word = wordIndex < orderedWords.Length && orderedWords[wordIndex].Key.Location <= start
                && orderedWords[wordIndex].Key.End >= end ? orderedWords[wordIndex].Value : default;
            runs.Add(new(text.Substring(start, end - start), new(start, end - start), underlined[start], word.Target, word.Word));
        }
        return runs.AsReadOnly();
    }
}
