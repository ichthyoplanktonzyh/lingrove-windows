using Cotabby.Core.Focus;
using Cotabby.Core.Spelling;
using Cotabby.Core.Support;
using Cotabby.Core.Suggestions.Streaming;
using Cotabby.Core.Text;

namespace Cotabby.Core.Suggestions;

/// <summary>
/// The last checks on a finished completion, made against the text that is in the field now.
/// </summary>
public static class CompletionFinalizer
{
    public readonly record struct Prepared(string Visible, string Hidden);
    public static Prepared? Prepare(string completion, FocusedField live, SuggestionSettings settings,
        bool spacingIsExact, TypoCorrectionAdvisor? typos, bool isFinal)
    {
        string? visible = VisibleText(completion, live, settings, spacingIsExact, typos, isFinal);
        if (visible is null) return null;
        string preceding = live.PrecedingText;
        string spaced = GhostSpaceBoundary.LiveAdjusted(completion, preceding, spacingIsExact ? preceding : null);
        string buffered = visible;
        if (spaced.StartsWith(visible, StringComparison.Ordinal) && spaced.Length > visible.Length)
        {
            string following = spaced[visible.Length..];
            var references = ReferenceVocabulary.Words(preceding, live.TrailingText, settings.ExtendedContext).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (CompletionSeamGuard.VisibleText(preceding + visible + " ", following,
                word => references.Contains(word) || typos is null ? CompletionSeamGuard.SpellingAssessment.Known : typos.Assess(word, preceding, settings.SpellingDictionaries), isFinal) is not null)
                buffered = spaced;
        }
        if (!isFinal)
        {
            string initial = settings.ShowFollowingWords ? visible : AcceptanceChunker.NextChunk(visible);
            buffered = StreamedGhostTextPolicy.CompletedBufferedPrediction(buffered, new GraphemeString(initial).Count);
        }
        string offered = settings.ShowFollowingWords ? visible : AcceptanceChunker.NextChunk(visible);
        if (offered.Length > buffered.Length || !buffered.StartsWith(offered, StringComparison.Ordinal)) return new(visible, "");
        return new(offered, buffered[offered.Length..]);
    }
    /// <summary>
    /// The text to show, or null when the completion went stale or would join the typed word into
    /// a misspelling. The leading space is decided here because the user keeps typing while the
    /// model runs.
    /// </summary>
    public static string? VisibleText(
        string completion, FocusedField live, SuggestionSettings settings, bool spacingIsExact,
        TypoCorrectionAdvisor? typos, bool isFinal = true)
    {
        string preceding = live.PrecedingText;
        if (GhostSpaceBoundary.IsStaleAfterTypedSpace(completion, preceding)) return null;

        string spaced = GhostSpaceBoundary.LiveAdjusted(completion, preceding, spacingIsExact ? preceding : null);
        var references = ReferenceVocabulary
            .Words(preceding, live.TrailingText, settings.ExtendedContext)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return CompletionSeamGuard.VisibleText(
            preceding, spaced,
            word => references.Contains(word) || typos is null
                ? CompletionSeamGuard.SpellingAssessment.Known
                : typos.Assess(word, preceding, settings.SpellingDictionaries), isFinal);
    }
}
