using System.Text;
using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.Text;

namespace Cotabby.Core.Suggestions.Streaming;

/// <summary>One local request's bounded typed-ahead path. Host snapshots must prove the same
/// eligible field and exact append, including UTF-16 caret movement, before output can be rebased.</summary>
public sealed class TypingPredictionCandidate(FocusedField origin, bool requiresInitialPrediction = false)
{
    public const double CatchUpWindow = 0.15, PublicationWindow = 0.4;
    public const int MaximumTypedCharacters = 64;
    public FocusedField Origin { get; } = origin;
    public string TypedText { get; private set; } = "";
    public string? LatestResult { get; private set; }
    public bool IsFinal { get; private set; }
    public double? LastInputAt { get; private set; }
    private double? _behindSince;

    public bool Accepts(FocusedField? live)
    {
        if (!InsertionTargetPolicy.IsSameField(Origin, live) || live!.TrailingText != Origin.TrailingText ||
            !live.PrecedingText.StartsWith(Origin.PrecedingText, StringComparison.Ordinal) ||
            !(Origin.PrecedingText + TypedText).StartsWith(live.PrecedingText, StringComparison.Ordinal)) return false;
        int published = live.PrecedingText.Length - Origin.PrecedingText.Length;
        return live.CaretOffset == Origin.CaretOffset + published;
    }
    public bool IsPublished(FocusedField? live) => Accepts(live) && live!.PrecedingText == Origin.PrecedingText + TypedText;
    public bool Append(string characters, FocusedField? live, double time)
    {
        if (!double.IsFinite(time) || !Accepts(live) || characters.Length == 0 || !WellFormed(characters) ||
            characters.EnumerateRunes().Any(Rune.IsControl) || GraphemeString.CountOf(TypedText + characters) > MaximumTypedCharacters ||
            _behindSince is { } since && time >= since + CatchUpWindow) return false;
        string next = TypedText + characters, prediction = LatestResult ?? "";
        if (requiresInitialPrediction && prediction.Length == 0) return false;
        if (!HasPrefix(prediction, next) && (IsFinal || !HasPrefix(next, prediction))) return false;
        TypedText = next; LastInputAt = time;
        if (HasPrefix(prediction, next)) _behindSince = null;
        else _behindSince ??= time;
        return true;
    }
    public bool Receive(string text, bool final)
    {
        if (IsFinal) return false;
        if (!final && LatestResult is { } previous && !HasPrefix(text, previous)) return true;
        if (!HasPrefix(text, TypedText) && (final || !HasPrefix(TypedText, text))) return false;
        LatestResult = text; IsFinal = final;
        if (HasPrefix(text, TypedText)) _behindSince = null;
        return true;
    }
    public double? Expiration(FocusedField? live)
    {
        double? model = _behindSince + CatchUpWindow;
        double? host = IsPublished(live) ? null : LastInputAt + PublicationWindow;
        return model is null ? host : host is null ? model : Math.Min(model.Value, host.Value);
    }
    public string? Rebased(string result, FocusedField? live)
    {
        if (!IsPublished(live) || !HasPrefix(result, TypedText)) return null;
        var text = new GraphemeString(result); return text.Text[text.Offset(GraphemeString.CountOf(TypedText))..];
    }
    private static bool HasPrefix(string value, string prefix)
    {
        var text = new GraphemeString(value); int count = GraphemeString.CountOf(prefix);
        return text.Count >= count && CanonicalStringComparer.Instance.Equals(text.Prefix(count), prefix);
    }
    private static bool WellFormed(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i])) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false; }
            else if (char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }
}
