using Cotabby.Core.Focus;

namespace Cotabby.Core.Insertion;

/// <summary>
/// Anchors an inline macro/emoji replacement to the field as it was before its trigger key.
/// Both controllers retain that capture while observing query keys; a fresh field is accepted only
/// when inserting the exact typed run into the capture reproduces its complete text and caret.
/// This policy owns no editor handle and never guesses a deletion count from a stale key count.
/// </summary>
public static class InlineQueryReplacementPolicy
{
    public static FocusedField? Resolve(FocusedField capture, string typedRun, FocusedField? current)
    {
        if (!InsertionTargetPolicy.IsSameField(capture, current)
            || string.IsNullOrEmpty(typedRun)
            || typedRun[0] is not ('/' or ':')) return null;

        var expected = capture with
        {
            Text = capture.Text.Insert(capture.CaretOffset, typedRun),
            CaretOffset = capture.CaretOffset + typedRun.Length,
        };
        return InsertionTargetPolicy.IsCurrentTarget(expected, current) ? current : null;
    }
}
