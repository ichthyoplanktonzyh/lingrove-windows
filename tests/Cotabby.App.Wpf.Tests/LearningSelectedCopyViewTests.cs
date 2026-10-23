using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.Controls;
using Cotabby.App.UI.Learning;
using Cotabby.Core.LanguageLearning;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Actual card wiring with detached copy events; no native clipboard writer is attached.</summary>
[Collection("WPF views")]
public sealed class LearningSelectedCopyViewTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task EmbeddedMeaningEmitsItsExactDocumentAndKeepsSelectionThroughRefreshAndAnnotations() => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            var document = Preview; var explanation = Explanation("<b>cafe\u0301</b>", "Literal prose.");
            SelectedTextCopyLease? emitted = null; LearningPreviewState? owner = null;
            window.SelectedCopyRequested += (lease, shown) => { emitted = lease; owner = shown; };
            window.ShowState(document, Anchor, false);
            window.ShowExplanationState(document.RequestId, explanation, true, Anchor);
            var meaning = Find<NonactivatingSelectableText>(window, "ExplanationMeaning");
            meaning.Select(0, meaning.Text.Length);
            var lease = meaning.RequestCopy()!;
            Assert.Same(lease, emitted); Assert.Same(document, owner);
            Assert.Equal("<b>cafe\u0301</b>", lease.Text);
            window.ShowExplanationState(document.RequestId, explanation with { IsGenerating = true }, true, Anchor);
            window.ShowState(document with { UnitStage = LearningUnitStage.Failed, Units = [] }, Anchor, false);
            Assert.True(lease.IsCurrent); Assert.Equal(lease.Text, meaning.Selection!.Text);
            Assert.IsNotType<NonactivatingSelectableText>(Find<TextBlock>(window, "SourceText"));
            Assert.IsNotType<NonactivatingSelectableText>(Find<TextBlock>(window, "ResultText"));
            window.ShowState(document with { RequestId = "doc-b" }, Anchor, false);
            Assert.False(lease.IsCurrent); Assert.Null(meaning.Selection);
            window.ShowSelectedCopyOutcome(lease, true);
            Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "CopyStatus").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ContextualExamplePreservesHighlightedRunsAndLeaseUntilPageOrTargetChanges() => dispatcher.Run(() =>
    {
        var window = new LearningDictionaryWindow();
        try
        {
            var state = Dictionary;
            SelectedTextCopyLease? emitted = null; LearningDictionaryState? owner = null;
            window.SelectedCopyRequested += (lease, shown) => { emitted = lease; owner = shown; };
            window.ShowState(state, Anchor);
            var example = Find<NonactivatingSelectableText>(window, "ContextualExampleText");
            example.Select(5, 7);
            var lease = example.RequestCopy()!;
            Assert.Same(lease, emitted); Assert.Same(state, owner); Assert.Equal("me", lease.Text);
            window.ShowState(state with { Explanation = state.Explanation! with { IsGenerating = true } }, Anchor);
            Assert.True(lease.IsCurrent); Assert.Equal("me", example.Selection!.Text);
            Assert.Equal("keepposted", string.Concat(example.Inlines.OfType<System.Windows.Documents.Run>()
                .Where(run => run.FontWeight == FontWeights.SemiBold).Select(run => run.Text)));
            window.ShowSelectedCopyOutcome(lease, true);
            window.ShowState(state with { Page = LearningDictionaryPage.Dictionary }, Anchor);
            Assert.False(lease.IsCurrent); Assert.Null(example.RequestCopy());
            Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "CopyStatus").Visibility);
            Assert.IsNotType<NonactivatingSelectableText>(Find<TextBlock>(window, "DictionaryExampleText"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DetailsAndReciprocalLineEmitTargetBoundCopiesAndRetargetRejectsOldFeedback() => dispatcher.Run(() =>
    {
        var window = new LearningDictionaryWindow();
        try
        {
            var state = Dictionary;
            window.ShowState(state, Anchor, new("原文翻译。", LearningLanguage.Chinese));
            var prose = Find<NonactivatingSelectableText>(window, "ExplanationProse");
            Assert.Null(prose.RequestCopy());
            Find<Expander>(window, "ExplanationDetails").IsExpanded = true; window.UpdateLayout();
            prose.Select(0, prose.Text.Length); var lease = prose.RequestCopy()!;
            Assert.Equal(state.TargetIdentity, lease.DocumentIdentity);
            Find<Expander>(window, "ExplanationDetails").IsExpanded = false;
            Assert.False(lease.IsCurrent);
            Find<Expander>(window, "ExplanationDetails").IsExpanded = true; window.UpdateLayout();
            prose.Select(0, prose.Text.Length); lease = prose.RequestCopy()!;
            var reciprocal = Find<NonactivatingSelectableText>(window, "ContextualExampleTranslation");
            reciprocal.Select(0, reciprocal.Text.Length); var original = reciprocal.RequestCopy()!;
            Assert.Equal("原文翻译。", original.Text);
            window.ShowSelectedCopyOutcome(original, false);
            Assert.Contains("Could not copy", Find<TextBlock>(window, "CopyStatus").Text);
            Assert.Equal("test-local", Find<TextBlock>(window, "AttributionText").Text);
            window.ShowState(state with { TargetIdentity = Guid.NewGuid() }, Anchor);
            Assert.False(lease.IsCurrent); Assert.False(original.IsCurrent);
            window.ShowSelectedCopyOutcome(original, true);
            Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "CopyStatus").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task HidingCardsRevokesSelectionsAndCannotEmitAnotherCopyFromTheHiddenControls() => dispatcher.Run(() =>
    {
        var preview = new LearningPreviewWindow(); var dictionary = new LearningDictionaryWindow();
        try
        {
            preview.ShowState(Preview, Anchor, false); preview.ShowExplanationState(Preview.RequestId, Explanation(), true, Anchor);
            dictionary.ShowState(Dictionary, Anchor);
            var first = Find<NonactivatingSelectableText>(preview, "ExplanationText");
            var second = Find<NonactivatingSelectableText>(dictionary, "ContextualMeaning");
            first.Select(0, first.Text.Length); second.Select(0, second.Text.Length);
            var a = first.RequestCopy()!; var b = second.RequestCopy()!;
            preview.HidePreview(); dictionary.HideCard();
            Assert.False(a.IsCurrent); Assert.False(b.IsCurrent);
            Assert.Null(first.RequestCopy()); Assert.Null(second.RequestCopy());
            Assert.Null(first.DocumentIdentity); Assert.Null(second.DocumentIdentity);
        }
        finally { preview.Close(); dictionary.Close(); }
    });
}
