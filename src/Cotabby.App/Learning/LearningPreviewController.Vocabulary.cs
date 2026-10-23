using System.Windows.Threading;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Learning;

/// <summary>
/// AppHost lends its shared store. The controller detaches durable captures on a worker and
/// validates the same frozen editor/document around that work; the view only borrows a binding.
/// A retired preparation cannot attach a favorite to a reused card. Saved edits, once admitted
/// by Store, finish even if this presentation disappears. Saving never starts a model.
/// </summary>
internal sealed partial class LearningPreviewController
{
    private readonly VocabularyStore? _vocabulary;
    private readonly VocabularyCaptureFactory? _vocabularyCaptures;
    private LearningPreviewState? _vocabularyPresentation;
    private long _vocabularyPreparationRevision;
    private VocabularyCapture? _dictionaryVocabularyCapture;

    private void PrepareSentenceVocabulary(LearningPreviewState presentation, CapturedSentence capture, long intent)
    {
        long revision;
        lock (_gate)
        {
            if (_vocabulary is null || _vocabularyCaptures is null || presentation.Status != LearningPreviewStatus.Ready
                || !CanExplainDocumentLocked(presentation, capture, intent)
                || LearningExplanationCardPolicy.SameDocument(_vocabularyPresentation, presentation)) return;
            _vocabularyPresentation = presentation; revision = ++_vocabularyPreparationRevision;
        }
        _ = ObserveExplanationPresentationAsync(PrepareVocabularyAsync(presentation, capture, intent, revision));
    }
    private async Task PrepareVocabularyAsync(LearningPreviewState presentation, CapturedSentence capture, long intent, long revision)
    {
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        var detached = await Task.Run(() =>
        {
            // A process basename is not a stable application identity. Leave provenance absent
            // until the Windows focus boundary supplies a real identifier; never label it a bundle ID.
            try { return _vocabularyCaptures!.FromWritingSentencePreview(presentation); }
            catch { return null; }
        }).ConfigureAwait(false);
        if (detached is null || !await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            bool Current()
            {
                lock (_gate) return revision == _vocabularyPreparationRevision && CanExplainDocumentLocked(presentation, capture, intent);
            }
            if (Current()) _window?.BindVocabulary(_vocabulary!, detached, presentation.RequestId, Current);
        }, DispatcherPriority.Background);
    }
    private void BindDictionaryVocabulary(LearningDictionaryState state, CapturedSentence capture, long intent)
    {
        if (_vocabulary is null || _dictionaryVocabularyCapture is null || state.Unit is not { } unit) return;
        VocabularyCapture? detached = _dictionaryVocabularyCapture;
        if (state.SelectedWord is { } word) detached = VocabularyCaptureFactory.FromComponent(word, unit, detached);
        if (detached is null) return;
        _dictionaryWindow?.BindVocabulary(_vocabulary, detached, state.TargetIdentity, () =>
        {
            lock (_gate) return CanUseDictionaryTargetLocked(state, capture, intent);
        });
    }
    private void RetireVocabularyLocked()
    { ++_vocabularyPreparationRevision; _vocabularyPresentation = null; }
}
