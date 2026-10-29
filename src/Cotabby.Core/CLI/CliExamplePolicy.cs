using System.Text;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.CLI;

/// <summary>Pure CLI examples and merge rules. Ranges address the exact UTF-16 source text;
/// an existing example keeps its source, occurrence, interpretation and authored meaning IDs.</summary>
internal static class CliExamplePolicy
{
    internal static string Text(string value, int limit, bool nonempty = false)
    {
        if (Encoding.UTF8.GetByteCount(value) > limit || nonempty && FoundationText.TrimWhitespaceAndNewlines(value).Length == 0)
            throw new CliException(CliErrorCode.InvalidInput, "Text is empty or exceeds its field limit.");
        return value;
    }
    internal static LearningLanguage? Language(string? value) => value is null ? null : LearningLanguages.FromCode(value)
        ?? throw new CliException(CliErrorCode.InvalidInput, "Unknown language code.");
    internal static IReadOnlyList<string> Tags(IEnumerable<string> source)
    {
        var values = source.ToArray(); if (values.Length > 100) throw new CliException(CliErrorCode.InvalidInput, "At most 100 tags are allowed.");
        var result = new List<string>();
        foreach (var value in values)
        {
            var tag = Text(FoundationText.TrimWhitespaceAndNewlines(value), 200, true);
            if (!result.Contains(tag, CanonicalStringComparer.Instance)) result.Add(tag);
        }
        return result;
    }
    internal static F.VocabularyExample Create(JsonObject input, string expression, Guid id, F.SourceDate now, string defaultKind = "authored")
    {
        string text = Text(input["text"]!.GetValue<string>(), 262144, true); var ranges = new List<VocabularyTextRange>();
        if (input["ranges"] is JsonArray supplied)
        {
            foreach (var value in supplied)
            {
                long location = value!["location"]!.GetValue<long>(), length = value["length"]!.GetValue<long>();
                if (location > int.MaxValue || length > int.MaxValue || location < 0 || length < 0) throw Invalid();
                ranges.Add(new((int)location, (int)length));
            }
        }
        else
        {
            int found = text.IndexOf(expression, StringComparison.Ordinal);
            if (found >= 0)
            {
                if (text.IndexOf(expression, found + 1, StringComparison.Ordinal) >= 0) throw new CliException(CliErrorCode.InvalidInput, "Expression occurs more than once. Supply explicit UTF-16 ranges.");
                ranges.Add(new(found, expression.Length));
            }
        }
        var boundaries = FoundationTextRanges.Boundaries(text);
        if (ranges.Count > 64 || ranges.Any(range => !FoundationTextRanges.IsValid(range.Location, range.Length, text, boundaries))) throw Invalid();
        var completeness = input["completeness"]?.GetValue<string>() switch { null or "sentence" => R.VocabularyExampleCompleteness.Sentence, "fragment" => R.VocabularyExampleCompleteness.Fragment, "unknown" => R.VocabularyExampleCompleteness.Unknown, _ => throw Invalid() };
        var kind = input["sourceKind"]?.GetValue<string>() ?? defaultKind;
        if (!Enum.TryParse<R.VocabularySourceKind>(kind, true, out var sourceKind) || !Enum.IsDefined(sourceKind)) throw Invalid();
        string? meaning = input["meaning"]?.GetValue<string>();
        return new F.VocabularyExample(id, text, Text(input["translation"]?.GetValue<string>() ?? "", 262144), ranges, completeness,
            Text(meaning ?? "", 32000), now)
        {
            SourceId = id, SourceKind = sourceKind, TranslationLanguage = Language(input["translationLanguage"]?.GetValue<string>()),
            MeaningLanguage = Language(input["meaningLanguage"]?.GetValue<string>()), MeaningOrigin = meaning is null ? VocabularyMeaningOrigin.Captured : VocabularyMeaningOrigin.User
        };
    }
    internal static string Key(F.VocabularyExample value)
    {
        string ranges = string.Join(",", value.Ranges.Select(range => $"{range.Location}:{range.Length}"));
        string completeness = value.Completeness.ToString().ToLowerInvariant();
        return string.Concat(new[] { value.Text, ranges, completeness }.Select(text => $"{Encoding.UTF8.GetByteCount(text)}:{text}"));
    }
    internal static F.VocabularyExample Merge(F.VocabularyExample retained, F.VocabularyExample incoming)
    {
        if (incoming.Meaning.Length > 0 && (retained.Meaning.Length > 0 || retained.MeaningOrigin == VocabularyMeaningOrigin.User)
            && (!CanonicalStringComparer.Instance.Equals(incoming.Meaning, retained.Meaning) || incoming.MeaningLanguage != retained.MeaningLanguage))
            throw new CliException(CliErrorCode.Conflict, "This usage has a different saved meaning. Edit it in the app.");
        var result = retained;
        if (incoming.Translation.Length > 0)
        {
            if (retained.Translation.Length == 0) result = result with { Translation = incoming.Translation, TranslationLanguage = incoming.TranslationLanguage };
            else
            {
                var variant = new VocabularyMeaning(incoming.Translation, incoming.TranslationLanguage);
                if (variant != new VocabularyMeaning(retained.Translation, retained.TranslationLanguage) && !retained.TranslationVariants.Contains(variant))
                    result = result with { TranslationVariants = retained.TranslationVariants.Append(variant).ToArray() };
            }
        }
        if (retained.Meaning.Length == 0 && retained.MeaningOrigin != VocabularyMeaningOrigin.User && incoming.Meaning.Length > 0)
            result = result with { Meaning = incoming.Meaning, MeaningLanguage = incoming.MeaningLanguage, MeaningOrigin = VocabularyMeaningOrigin.User };
        return result;
    }
    private static CliException Invalid() => new(CliErrorCode.InvalidInput, "Invalid UTF-16 ranges or context completeness.");
}
