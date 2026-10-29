using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.Text;

namespace Cotabby.Core.Suggestions.Streaming;

/// <summary>Source, expected committed edit and virtual request boundary remain separate. Windows
/// requires an exact complete field snapshot; hypothetical text is never dispatched to the host.</summary>
public sealed record SuggestionContinuationPlan(FocusedField Source, FocusedField Target, FocusedField Request, string JoiningSeparator)
{
    public static SuggestionContinuationPlan? Completing(string suffix, FocusedField source)
    {
        if (!InsertionTargetPolicy.IsEligibleField(source) || string.IsNullOrWhiteSpace(suffix) || !InsertionTargetPolicy.IsValidInsertionText(suffix)) return null;
        return Create(source, source with { Text = source.PrecedingText + suffix + source.TrailingText, CaretOffset = source.CaretOffset + suffix.Length });
    }
    public static SuggestionContinuationPlan? Correcting(int deleteUtf16Count, string replacement, FocusedField source)
    {
        if (!InsertionTargetPolicy.IsValidReplacement(source, deleteUtf16Count, replacement) || deleteUtf16Count <= 0 || string.IsNullOrWhiteSpace(replacement)) return null;
        int start = source.CaretOffset - deleteUtf16Count; var text = new GraphemeString(source.Text);
        if (!Enumerable.Range(0, text.Count + 1).Any(i => text.Offset(i) == start)) return null;
        return Create(source, source with { Text = source.Text[..start] + replacement + source.TrailingText, CaretOffset = start + replacement.Length });
    }
    private static SuggestionContinuationPlan Create(FocusedField source, FocusedField target)
    {
        string separator = target.PrecedingText.Length > 0 && char.IsWhiteSpace(target.PrecedingText[^1]) ? "" : " ";
        var request = target with { Text = target.PrecedingText + separator + target.TrailingText, CaretOffset = target.CaretOffset + separator.Length };
        return new(source, target, request, separator);
    }
    public bool MatchesSource(FocusedField? snapshot) => InsertionTargetPolicy.IsCurrentTarget(Source, snapshot);
    public bool MatchesTarget(FocusedField? snapshot) => InsertionTargetPolicy.IsCurrentTarget(Target, snapshot);
    public bool MatchesTargetWithJoiningSeparator(FocusedField? snapshot) => JoiningSeparator.Length != 0 && InsertionTargetPolicy.IsCurrentTarget(Request, snapshot);
    public string Continuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (JoiningSeparator.Length != 0) return char.IsWhiteSpace(text[0]) ? text : JoiningSeparator + text;
        return Target.PrecedingText.EndsWith(' ') ? text.TrimStart(' ') : text;
    }
}
