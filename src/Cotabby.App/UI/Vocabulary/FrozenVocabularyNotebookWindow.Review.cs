using System.ComponentModel;
using System.Windows.Controls;
using Cotabby.App.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Vocabulary;

internal sealed partial class FrozenVocabularyNotebookWindow
{
    private FrozenVocabularyReviewSection? _reviewSection;
    private AnkiReviewFeature? _reviewFeature;
    internal FrozenVocabularyReviewSection? ReviewSection => _reviewSection;
    internal void AttachReviewFeature(AnkiReviewFeature feature, Action configure)
    {
        Dispatcher.VerifyAccess();
        if (_retiring || _reviewSection is not null) return;
        if (_store is not ICanonicalVocabularyStore canonical) return;
        _reviewFeature = feature;
        _reviewSection = new(this, canonical, feature, configure, () => { if (!_retiring) _session.Refresh(); },
            canAct: (entry, sense) => !_retiring && IsVisible && _modalDone is null && !_senseMutating && !_semanticBusy && _session.Selection == new VocabularySelection(entry, sense));
        feature.Controller.PropertyChanged += ReviewChanged; _session.Refresh();
    }
    private void ReviewChanged(object? sender, PropertyChangedEventArgs e) { if (!_retiring) _session.Refresh(); }
    private void RenderReviewSection(StackPanel panel, F.VocabularyEntry entry, F.VocabularySense sense)
    {
        if (_reviewSection is { } section) panel.Children.Add(section.Render(entry, sense));
        else panel.Children.Add(VocabularyInputControls.Caption("Review cards are unavailable."));
    }
    private async Task RetireReviewAsync()
    {
        if (_reviewFeature is { } feature) feature.Controller.PropertyChanged -= ReviewChanged;
        if (_reviewSection is { } section) await section.RetireAsync();
    }
}
