using System.Globalization;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.ExpressionAssist;

public sealed record ExpressionAssistRequest(string Query, string LeftContext, string RightContext,
    string SelectedText, string TargetLanguage, string ExplanationLanguage)
{
    /// <summary>Request-scoped nonblocking native receipt/query permit; never persisted or sent.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public Func<bool>? Admission { get; init; }
}
public sealed record ExpressionAssistCandidate(string Text, string Hint, string? DictionaryForm);
public sealed record ExpressionAssistResult(IReadOnlyList<ExpressionAssistCandidate> Candidates, string? Clarification);

/// <summary>Pure trigger and UTF-16 selection rules; editor handles remain with the session owner.</summary>
public static class ExpressionAssistPolicy
{
    public static string? TriggerError(string trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var elements = new GraphemeString(trigger);
        if (elements.Count is < 2 or > 8 || elements.Any(g => !g.IsSymbol && !g.IsPunctuation))
            return "Use 2–8 punctuation marks or symbols without spaces.";
        if (trigger.StartsWith(':') || trigger.StartsWith('/'))
            return "Choose a trigger that does not start with : or /; those open emoji and macros.";
        return null;
    }

    public static string? MatchedTrigger(string beforeCaret, string configured)
    {
        if (TriggerError(configured) is not null) return null;
        var suffix = new GraphemeString(beforeCaret);
        var configuredLength = new GraphemeString(configured).Count;
        if (suffix.Count >= configuredLength && CanonicalStringComparer.Instance.Equals(suffix.Suffix(configuredLength), configured)) return configured;
        return configured == "??" && beforeCaret.EndsWith("？？", StringComparison.Ordinal) ? "？？" : null;
    }

    public static ExpressionAssistRequest Request(string query, string value, int location, int length,
        LearningLanguage target, LearningLanguage explanation)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(value);
        if (!EditorTextRangePolicy.IsValid(value, location, length)) throw new ArgumentOutOfRangeException(nameof(location));
        var selected = value.Substring(location, length);
        query = FoundationText.TrimWhitespaceAndNewlines(query);
        if (query.Length == 0 || !LearningExplanationText.Fits(query, 2_000) || !LearningExplanationText.Fits(selected, 2_000))
            throw new ArgumentException("Keep the expression description and selection under 2 KB each.");
        var before = value[..location];
        var after = value[(location + length)..];
        var left = before[(before.LastIndexOf('\n') + 1)..];
        var newline = after.IndexOf('\n');
        var right = newline < 0 ? after : after[..newline];
        return new(query, Take(left, 700, true), Take(right, 500, false), selected, target.Code(), explanation.Code());

    }

    private static string Take(string text, int count, bool suffix)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        if (starts.Length <= count) return text;
        return suffix ? text[starts[starts.Length - count]..] : text[..starts[count]];
    }

}
