using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Cotabby.App.UI.Learning;
using Cotabby.Core.LanguageLearning;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningPreviewViewTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task StreamingPrefixIsLiteralAndCannotAcquireReadyLexicalAffordances() => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            var ready = Preview;
            var partial = ready with { Status = LearningPreviewStatus.Loading, PartialResult = "<b>keep</b>", Result = null };
            window.ShowState(partial, Anchor, canRetry: false);
            Assert.Equal("<b>keep</b>", Text(Find<TextBlock>(window, "ResultText")));
            Assert.Null(Find<TextBlock>(window, "ResultText").ToolTip);
            Assert.Equal(Visibility.Visible, Find<ProgressBar>(window, "Progress").Visibility);
            Assert.Equal("TRANSLATION · PREVIEW", Find<TextBlock>(window, "ResultLabel").Text);
            window.ShowState(ready, Anchor, canRetry: false);
            var result = Find<TextBlock>(window, "ResultText");
            Assert.Equal(ready.Result, Text(result));
            Assert.NotNull(result.ToolTip);
            Assert.Contains(result.Inlines.OfType<Run>(), run => run.TextDecorations is { Count: > 0 });
            Assert.Equal(Visibility.Collapsed, Find<ProgressBar>(window, "Progress").Visibility);
            AssertNonactivating(window);
            AssertPaints(window);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ExplanationActionCarriesExactSnapshotAndSurvivesOnlyItsCompletedDocument() => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            var preview = Preview;
            var explanation = Explanation();
            var commands = new List<(string Document, LearningExplanationState State)>();
            window.ExplanationRequested += (document, state) => commands.Add((document, state));
            window.ShowState(preview, Anchor, false);
            window.ShowExplanationState(preview.RequestId, explanation, true, Anchor);
            Click(Find<Button>(window, "ExplanationButton"));
            Assert.Equal(preview.RequestId, Assert.Single(commands).Document);
            Assert.Same(explanation, commands[0].State);
            window.ShowState(preview with { UnitStage = LearningUnitStage.Failed, Units = [] }, Anchor, false);
            Assert.Equal(explanation.Record!.Result.Meaning, Find<TextBlock>(window, "ExplanationMeaning").Text);
            window.ShowState(preview with { RequestId = "doc-b" }, Anchor, false);
            Assert.Equal(Visibility.Collapsed, Find<Button>(window, "ExplanationButton").Visibility);
            Assert.Empty(Find<TextBlock>(window, "ExplanationMeaning").Text);
            window.ShowExplanationState(preview.RequestId, explanation, true, Anchor);
            Click(Find<Button>(window, "ExplanationButton"));
            Assert.Single(commands);
            Assert.Equal(Visibility.Collapsed, Find<StackPanel>(window, "ExplanationSection").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RefreshAndFailureKeepCompleteAnswerInBoundedViewportWithResponseDirection() => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            string prose = string.Concat(Enumerable.Repeat("هذا شرح طويل. ", 400));
            var explanation = Explanation("<b>معنى</b>", prose);
            explanation = explanation with { Record = explanation.Record! with { Language = LearningLanguage.Arabic }, IsGenerating = true };
            var preview = Preview with { Source = string.Concat(Enumerable.Repeat("一段很长的原文。", 400)) };
            window.ShowState(preview, Anchor, false);
            window.ShowExplanationState(preview.RequestId, explanation, true, Anchor);
            Assert.Equal("<b>معنى</b>", Find<TextBlock>(window, "ExplanationMeaning").Text);
            Assert.Equal(prose, Find<TextBlock>(window, "ExplanationText").Text);
            Assert.Equal(FlowDirection.RightToLeft, Find<StackPanel>(window, "ExplanationResult").FlowDirection);
            Assert.Equal("Stop generating explanation", AutomationProperties.GetName(Find<Button>(window, "ExplanationButton")));
            Assert.InRange(Find<Border>(window, "Card").ActualHeight, 100, 594);
            window.ShowExplanationState(preview.RequestId, explanation with { IsGenerating = false, ErrorMessage = "Could not explain." }, true, Anchor);
            Assert.Equal(prose, Find<TextBlock>(window, "ExplanationText").Text);
            Assert.Equal("Try again", AutomationProperties.GetName(Find<Button>(window, "ExplanationButton")));
            Assert.Equal(Visibility.Collapsed, Find<ProgressBar>(window, "ExplanationProgress").Visibility);
            Assert.Equal("Could not explain.", Find<TextBlock>(window, "ExplanationStatus").Text);
            AssertPaints(window);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(LearningPreviewStatus.Loading)]
    [InlineData(LearningPreviewStatus.Failed)]
    [InlineData(LearningPreviewStatus.Cancelled)]
    public Task NonReadyDocumentAndHideRejectLateExplanation(LearningPreviewStatus status) => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            var preview = Preview;
            window.ShowState(preview, Anchor, false);
            window.ShowExplanationState(preview.RequestId, Explanation(), true, Anchor);
            window.ShowState(preview with { Status = status, Result = null }, Anchor, true);
            window.ShowExplanationState(preview.RequestId, Explanation(), true, Anchor);
            Assert.Equal(Visibility.Collapsed, Find<StackPanel>(window, "ExplanationSection").Visibility);
            window.HidePreview();
            window.ShowExplanationState(preview.RequestId, Explanation(), true, Anchor);
            Assert.False(window.IsVisible);
            Assert.Empty(Find<TextBlock>(window, "SourceText").Text);
            Assert.Empty(Find<TextBlock>(window, "ResultText").Inlines);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CaptureFailureClearsPreviousResultAndButtonsKeepNonfocusableIntentWiring() => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            int retries = 0, cancels = 0, closes = 0;
            window.RetryRequested += () => retries++;
            window.CancelRequested += () => cancels++;
            window.DismissRequested += () => closes++;
            window.ShowState(Preview with { Status = LearningPreviewStatus.Failed, Result = null, ErrorMessage = "Could not translate." }, Anchor, true);
            Assert.Equal(Visibility.Visible, Find<Button>(window, "RetryButton").Visibility);
            Click(Find<Button>(window, "RetryButton"));
            Click(Find<Button>(window, "CancelButton"));
            Click(Find<Button>(window, "CloseButton"));
            Assert.Equal((1, 1, 1), (retries, cancels, closes));
            foreach (string name in new[] { "RetryButton", "CancelButton", "CloseButton", "ExplanationButton" })
                Assert.False(Find<Button>(window, name).Focusable);
            window.ShowState(Preview, Anchor, false);
            window.ShowExplanationState(Preview.RequestId, Explanation(), true, Anchor);
            window.ShowCaptureFailure("No committed sentence is available.", Anchor);
            Assert.Empty(Find<TextBlock>(window, "SourceText").Text);
            Assert.Empty(Find<TextBlock>(window, "ResultText").Inlines);
            Assert.Empty(Find<TextBlock>(window, "ExplanationText").Text);
            Assert.Equal(Visibility.Collapsed, Find<StackPanel>(window, "ResultSection").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<Button>(window, "RetryButton").Visibility);
            Assert.Equal("No text was sent.", Find<TextBlock>(window, "StatusText").Text);
        }
        finally { window.Close(); }
    });
}
