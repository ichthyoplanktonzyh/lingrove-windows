using System.Globalization;
using System.Text;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.ExpressionAssist;

public sealed record ExpressionAssistRequest(string Query, string LeftContext, string RightContext,
    string SelectedText, string TargetLanguage, string ExplanationLanguage);
public sealed record ExpressionAssistCandidate(string Text, string Hint, string? DictionaryForm);
public sealed record ExpressionAssistResult(IReadOnlyList<ExpressionAssistCandidate> Candidates, string? Clarification);

/// <summary>Pure trigger and UTF-16 selection rules; editor handles remain with the session owner.</summary>
public static class ExpressionAssistPolicy
{
    public static string? TriggerError(string trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var elements = StringInfo.ParseCombiningCharacters(trigger);
        if (elements.Length is < 2 or > 8 || trigger.EnumerateRunes().Any(r => !IsSymbolOrPunctuation(r)))
            return "Use 2–8 punctuation marks or symbols without spaces.";
        if (trigger.StartsWith(':') || trigger.StartsWith('/'))
            return "Choose a trigger that does not start with : or /; those open emoji and macros.";
        return null;
    }

    public static string? MatchedTrigger(string beforeCaret, string configured)
    {
        if (TriggerError(configured) is not null) return null;
        if (beforeCaret.EndsWith(configured, StringComparison.Ordinal)) return configured;
        return configured == "??" && beforeCaret.EndsWith("？？", StringComparison.Ordinal) ? "？？" : null;
    }

    public static ExpressionAssistRequest Request(string query, string value, int location, int length,
        LearningLanguage target, LearningLanguage explanation)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(value);
        var boundaries = StringInfo.ParseCombiningCharacters(value);
        if (location < 0 || length < 0 || location > value.Length || length > value.Length - location ||
            !IsBoundary(location) || !IsBoundary(location + length)) throw new ArgumentOutOfRangeException(nameof(location));
        var selected = value.Substring(location, length);
        query = query.Trim();
        if (query.Length == 0 || Encoding.UTF8.GetByteCount(query) > 2_000 || Encoding.UTF8.GetByteCount(selected) > 2_000)
            throw new ArgumentException("Keep the expression description and selection under 2 KB each.");
        var before = value[..location];
        var after = value[(location + length)..];
        var left = before[(before.LastIndexOf('\n') + 1)..];
        var newline = after.IndexOf('\n');
        var right = newline < 0 ? after : after[..newline];
        return new(query, Take(left, 700, true), Take(right, 500, false), selected, target.Code(), explanation.Code());

        bool IsBoundary(int offset) => offset == value.Length || Array.BinarySearch(boundaries, offset) >= 0;
    }

    private static string Take(string text, int count, bool suffix)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        if (starts.Length <= count) return text;
        return suffix ? text[starts[starts.Length - count]..] : text[..starts[count]];
    }

    private static bool IsSymbolOrPunctuation(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or
        UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or
        UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or
        UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;
}
