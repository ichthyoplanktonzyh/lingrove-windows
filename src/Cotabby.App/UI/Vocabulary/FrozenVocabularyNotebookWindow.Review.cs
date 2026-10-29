using System.ComponentModel;
using System.Windows.Controls;
using Cotabby.App.Review;
using Cotabby.App.UI.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Vocabulary;

internal sealed partial class FrozenVocabularyNotebookWindow
{
    private FrozenVocabularyReviewSection? _reviewSection;
    private AnkiReviewFeature? _reviewFeature;
    private Action? _configureReview;
    private CanonicalAnkiReviewBatchWindow? _reviewBatchWindow;
    private CancellationTokenSource? _reviewBatchCancellation;
    private TaskCompletionSource? _reviewBatchCompletion;
    internal CanonicalAnkiReviewBatchWindow? ReviewBatchWindow => _reviewBatchWindow;
    internal FrozenVocabularyReviewSection? ReviewSection => _reviewSection;
    internal void AttachReviewFeature(AnkiReviewFeature feature, Action configure)
    {
        Dispatcher.VerifyAccess();
        if (_retiring || _reviewSection is not null) return;
        if (_store is not ICanonicalVocabularyStore canonical) return;
        _reviewFeature = feature; _configureReview = configure;
        _reviewSection = new(this, canonical, feature, configure, () => { if (!_retiring) _session.Refresh(); },
            canAct: (entry, sense) => !_retiring && IsVisible && _modalDone is null && !_senseMutating && !_semanticBusy && _session.Selection == new VocabularySelection(entry, sense), clipboard: _clipboard);
        feature.Controller.PropertyChanged += ReviewChanged; _session.Refresh();
    }
    private void ReviewChanged(object? sender, PropertyChangedEventArgs e) { if (!_retiring) _session.Refresh(); }
    private void RenderReviewSection(StackPanel panel, F.VocabularyEntry entry, F.VocabularySense sense)
    {
        if (_reviewSection is { } section) panel.Children.Add(section.Render(entry, sense));
        else panel.Children.Add(VocabularyInputControls.Caption("Review cards are unavailable."));
    }
    internal void OpenReviewBatch(IReadOnlyList<VocabularySelection> selections)
    {
        Dispatcher.VerifyAccess();
        if (_retiring || _modalDone is not null || _reviewBatchCompletion is not null || _reviewSection?.IsBusy == true || _reviewFeature is null || _configureReview is null || _store is not ICanonicalVocabularyStore || selections.Count == 0) return;
        var captured = selections.ToArray(); var done = _reviewBatchCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = _reviewBatchCancellation = new CancellationTokenSource(); _modalDone = done;
        LastOperation = Track(Run());
        async Task Run()
        {
            try
            {
                var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(_reviewFeature, (ICanonicalVocabularyStore)_store, captured, cancellation.Token);
                if (_retiring) return;
                var window = new CanonicalAnkiReviewBatchWindow(batch, _reviewFeature, _configureReview, _clipboard) { Owner = this }; _reviewBatchWindow = window;
                try { window.ShowDialog(); } finally { await window.RetireAsync(); _reviewBatchWindow = null; }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_retiring) _actionError = error.Message; }
            finally
            {
                _reviewBatchWindow = null; _reviewBatchCancellation = null; cancellation.Dispose(); _reviewBatchCompletion = null; _modalDone = null;
                try { if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } if (!_retiring) _session.Refresh(); } finally { done.TrySetResult(); }
            }
        }
    }
    private async Task RetireReviewAsync()
    {
        if (_reviewFeature is { } feature) feature.Controller.PropertyChanged -= ReviewChanged;
        var errors = new List<Exception>();
        try { _reviewBatchCancellation?.Cancel(); } catch (Exception error) { errors.Add(error); }
        try { if (_reviewBatchWindow is { } window) await window.RetireAsync(); } catch (Exception error) { errors.Add(error); }
        try { if (_reviewBatchCompletion is { } completion) await completion.Task; } catch (Exception error) { errors.Add(error); }
        try { if (_reviewSection is { } section) await section.RetireAsync(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException(errors);
    }
}
