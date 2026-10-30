using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Learning;

internal sealed partial class LearningPreviewController
{
    private sealed record WritingHistory(int ProcessId, string FieldId, long FocusEpoch, LearningRuntimeSettings Settings,
        WritingPreviousSentence Current, WritingPreviousSentence? Earlier);
    private WritingHistory? _writingHistory;
    private WritingPreviousSentence? _currentPreviousSentence;

    private WritingPreviousSentence? PreviousSentenceLocked(CapturedSentence capture)
    {
        if (_writingHistory is not { } history || !HistoryFieldMatches(history, capture.Field)
            || !history.Settings.HasSameTranslationInputs(capture.Settings)) return null;
        return WritingPreviousSentencePolicy.Select(history.Current, history.Earlier, capture.Field.Text, capture.Range);
    }
    private void RememberCompleteSentence(LearningPreviewState state, CapturedSentence capture)
    {
        lock (_gate)
        {
            if (state.Status != LearningPreviewStatus.Ready || state.Result is not { Length: > 0 } translation
                || !ReferenceEquals(capture, _activeCapture) || state.RequestId != _activeRequestId) return;
            _writingHistory = new(capture.Field.ProcessId, capture.Field.FieldId, capture.Field.FocusChangeSequence,
                capture.Settings, new(capture.Source, capture.Range, translation), _currentPreviousSentence);
        }
    }
    private static bool HistoryFieldMatches(WritingHistory history, FocusedField field) => history.ProcessId == field.ProcessId
        && history.FieldId == field.FieldId && history.FocusEpoch == field.FocusChangeSequence && !field.IsSecure;
    private void DismissPreview()
    {
        lock (_gate) { _writingHistory = null; _currentPreviousSentence = null; }
        CancelAndHide();
    }
}
