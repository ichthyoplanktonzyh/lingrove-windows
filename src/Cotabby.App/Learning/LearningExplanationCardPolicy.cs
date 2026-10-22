using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Learning;

/// <summary>
/// The non-WPF projection of Mac LearningExplanationButton/embedded View. The preview window
/// borrows this decision; its controller retains editor authority and the session owns work.
/// Keeping labels/action priority here lets portable tests check the actual presentation rule.
/// </summary>
internal static class LearningExplanationCardPolicy
{
    public static LearningExplanationCardAction Action(LearningExplanationState state, bool configured) =>
        state.Input is null ? LearningExplanationCardAction.None
        : state.IsGenerating ? LearningExplanationCardAction.Stop
        : !configured ? LearningExplanationCardAction.Configure
        : state.Record is not null || state.ErrorMessage is not null ? LearningExplanationCardAction.Refresh
        : LearningExplanationCardAction.Explain;

    public static string Label(LearningExplanationState state, bool configured) => Action(state, configured) switch
    {
        LearningExplanationCardAction.Stop => "Stop generating explanation",
        LearningExplanationCardAction.Configure => "Configure AI explanation model",
        LearningExplanationCardAction.Refresh when state.ErrorMessage is not null => "Try again",
        LearningExplanationCardAction.Refresh => "Regenerate explanation",
        _ => "AI explanation",
    };

    /// <summary>
    /// ICU preparation and UIA validation may finish after a different document became active.
    /// Request identity and exact strings matter even when canonical explanation inputs compare
    /// equal. Lexical-stage changes annotate the same translation and do not replace its target.
    /// </summary>
    public static bool SameDocument(LearningPreviewState? expected, LearningPreviewState? current) =>
        expected is { Status: LearningPreviewStatus.Ready, Options: not null, Source.Length: > 0, Result.Length: > 0, RequestId.Length: > 0 }
        && current is { Status: LearningPreviewStatus.Ready, Options: not null, Source.Length: > 0, Result.Length: > 0 }
        && string.Equals(expected.RequestId, current.RequestId, StringComparison.Ordinal)
        && string.Equals(expected.Source, current.Source, StringComparison.Ordinal)
        && string.Equals(expected.Result, current.Result, StringComparison.Ordinal)
        && expected.Options == current.Options;
}

/// <summary>One explicit card intent, interpreted by the captured-document controller.</summary>
internal enum LearningExplanationCardAction { None, Explain, Refresh, Stop, Configure }
