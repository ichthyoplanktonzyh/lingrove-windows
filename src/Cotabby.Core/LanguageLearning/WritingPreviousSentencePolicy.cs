using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

public sealed record WritingPreviousSentence(string Source, LearningTextRange Range, string Translation);

/// <summary>Mac 3b precedingSentence evidence: the old exact source must still precede the new
/// target, with only whitespace/punctuation between them. This authorizes a label, never a write.</summary>
public static class WritingPreviousSentencePolicy
{
    public static WritingPreviousSentence? Select(WritingPreviousSentence? current, WritingPreviousSentence? earlier,
        string value, LearningTextRange target)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (target.Location < 0 || target.Location > value.Length) return null;
        foreach (var candidate in new[] { current, earlier })
        {
            if (candidate is null || string.IsNullOrEmpty(candidate.Translation) || candidate.Source is null
                || candidate.Range.Location < 0 || candidate.Range.Length < 0 || candidate.Range.Location >= target.Location
                || candidate.Range.Location > value.Length || candidate.Range.Length > value.Length - candidate.Range.Location) continue;
            int end = candidate.Range.Location + candidate.Range.Length;
            if (end > target.Location || !CanonicalStringComparer.Instance.Equals(value.Substring(candidate.Range.Location, candidate.Range.Length), candidate.Source)) continue;
            if (new GraphemeString(value[end..target.Location]).All(character => character.IsWhitespace || character.IsPunctuation)) return candidate;
        }
        return null;
    }
}
