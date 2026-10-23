using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.Learning;
using Cotabby.Core.LanguageLearning;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Explicit Copy intents are observed with no listener writing the real clipboard.</summary>
[Collection("WPF views")]
public sealed class LearningCopyViewTests(WpfDispatcher dispatcher)
{
    private static Button[] Buttons(Window window) => Find<WrapPanel>(window, "CopyActions").Children.OfType<Button>().ToArray();

    [Fact]
    public Task ProvisionalTranslationHasOnlySourceCopyAndReadyAddsTheExactLiteralTranslation() => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            var ready = Preview with { Source = "  原文\r\n", Result = "  <b>cafe\u0301</b> 👩🏽‍💻\n" };
            var partial = ready with { Status = LearningPreviewStatus.Loading, Result = null, PartialResult = "provisional" };
            var requests = new List<LearningPreviewCopyRequest>();
            window.CopyRequested += requests.Add;
            window.ShowState(partial, Anchor, false);
            var source = Assert.Single(Buttons(window));
            Assert.Equal("Copy source text", source.Content); Assert.False(source.Focusable);
            Click(source);
            Assert.Equal(ready.Source, LearningPreviewCopyPolicy.Text(Assert.Single(requests), partial));
            window.ShowState(ready, Anchor, false);
            var buttons = Buttons(window);
            Assert.Equal(new[] { "Copy source text", "Copy translation" }, buttons.Select(button => button.Content));
            Assert.All(buttons, button => Assert.False(button.Focusable));
            Click(buttons[1]);
            Assert.Same(ready, requests[1].Document);
            Assert.Equal(ready.Result, LearningPreviewCopyPolicy.Text(requests[1], ready));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LexicalUpdatesKeepButtonsButRemovedOldDocumentButtonsCannotCopyANewerDocument() => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            var ready = Preview;
            int requests = 0;
            window.CopyRequested += _ => requests++;
            window.ShowState(ready, Anchor, false);
            var buttons = Buttons(window);
            window.ShowState(ready with { UnitStage = LearningUnitStage.Failed, Units = [] }, Anchor, false);
            Assert.Same(buttons[0], Buttons(window)[0]); Assert.Same(buttons[1], Buttons(window)[1]);
            window.ShowState(ready with { RequestId = "doc-b" }, Anchor, false);
            Click(buttons[0]); Click(buttons[1]);
            Assert.Equal(0, requests);
            Click(Buttons(window)[1]);
            Assert.Equal(1, requests);
            var current = Buttons(window)[1];
            window.HidePreview(); Click(current);
            Assert.Equal(1, requests);
            Assert.Empty(Buttons(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ErrorCardDropsCopyActionsAndOldTranslationFeedbackCannotRepopulateIt() => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            var ready = Preview;
            window.ShowState(ready, Anchor, false);
            var oldTranslation = Buttons(window)[1];
            var request = new LearningPreviewCopyRequest(ready, LearningPreviewCopyKind.Translation);
            window.ShowCopyOutcome(request, false);
            Assert.Contains("try again", Find<TextBlock>(window, "CopyStatus").Text);
            window.ShowState(ready with { Status = LearningPreviewStatus.Failed, Result = null }, Anchor, true);
            Assert.Empty(Buttons(window));
            Assert.Equal(Visibility.Collapsed, Find<StackPanel>(window, "SourceSection").Visibility);
            int copies = 0; window.CopyRequested += _ => copies++;
            Click(oldTranslation);
            Assert.Equal(0, copies);
            window.ShowCopyOutcome(request, true);
            Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "CopyStatus").Visibility);
            window.ShowCaptureFailure("Unavailable.", Anchor);
            Assert.Empty(Buttons(window));
            Assert.Empty(Find<TextBlock>(window, "CopyStatus").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LongContentKeepsCopyFooterOutsideScrollingAndWithinCardWidth() => dispatcher.Run(() =>
    {
        var window = new LearningPreviewWindow();
        try
        {
            window.ShowState(Preview with { Source = string.Concat(Enumerable.Repeat("Long captured source text. ", 800)) }, Anchor, false);
            var actions = Find<WrapPanel>(window, "CopyActions");
            var card = Find<Border>(window, "Card");
            var container = Assert.IsType<StackPanel>(card.Child);
            var body = Assert.Single(container.Children.OfType<ScrollViewer>());
            var bodyBounds = body.TransformToAncestor(window).TransformBounds(new Rect(body.RenderSize));
            var footerBounds = actions.TransformToAncestor(window).TransformBounds(new Rect(actions.RenderSize));
            Assert.True(footerBounds.Top >= bodyBounds.Bottom);
            Assert.True(footerBounds.Bottom <= window.ActualHeight);
            foreach (var button in Buttons(window))
            {
                var bounds = button.TransformToAncestor(card).TransformBounds(new Rect(button.RenderSize));
                Assert.True(bounds.Left >= 0 && bounds.Right <= card.ActualWidth);
            }
            AssertNonactivating(window); AssertPaints(window);
        }
        finally { window.Close(); }
    });
}
