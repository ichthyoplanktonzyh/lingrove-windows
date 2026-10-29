using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningPreviewEmbeddedExampleTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ExpansionAndReplacementEmitOnlyCurrentReadyDocument() => dispatcher.RunAsync(() =>
    {
        var window = new LearningPreviewWindow(true); var card = window.TakeExampleCard(); var host = new Border { Child = card };
        var expand = (Button)window.FindName("ExpandButton"); var replace = (Button)window.FindName("ReplaceButton");
        string? expanded = null, replaced = null; window.ExpandRequested += value => expanded = value; window.ReplaceRequested += value => replaced = value;
        var state = new LearningPreviewState { RequestId = "one", Source = "你好", Result = "Hello", Status = LearningPreviewStatus.Ready, Options = new() };
        window.ShowState(state, new ScreenRect(0, 0, 1, 1), false); window.ShowExpanded(false); window.ShowPreviousTranslation("Previous sentence");
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("SourceSection")).Visibility);
        Assert.Equal("English", ((TextBlock)window.FindName("TitleText")).Text);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ResultLabel")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ExplanationContainer")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("FavoriteContainer")).Visibility);
        int explanationRequests = 0; window.ExplanationRequested += (_, _) => explanationRequests++;
        window.ShowExplanationState("one", new LearningExplanationState(), true, ScreenRect.Empty);
        ((Button)window.FindName("ExplanationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, explanationRequests);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("PreviousTranslation")).Visibility);
        Assert.False(replace.IsEnabled); expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("one", expanded);
        window.ShowExpanded(true); Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("SourceSection")).Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ExplanationContainer")).Visibility);
        window.ShowReplacementState(true, false, null); Assert.True(replace.IsEnabled);
        replace.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("one", replaced);
        window.ShowReplacementState(true, true, null); Assert.False(replace.IsEnabled);
        window.ShowState(state with { RequestId = "two" }, new ScreenRect(0, 0, 1, 1), false);
        Assert.False(replace.IsEnabled); window.ShowReplacementState(false, false, "Could not verify this edit.");
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ReplacementReason")).Visibility);
        window.ShowState(state with { Status = LearningPreviewStatus.Failed, Result = null }, new ScreenRect(0, 0, 1, 1), false);
        Assert.Equal(Visibility.Collapsed, expand.Visibility); Assert.Equal(Visibility.Collapsed, replace.Visibility);
        window.RetireExample(); Assert.Null(window.Content); Assert.Same(card, host.Child); return Task.CompletedTask;
    });

    [Fact]
    public Task SampleUsesRealCardWithoutShowingNativeWindowOrCopyActions() => dispatcher.RunAsync(() =>
    {
        var cardOwner = new LearningPreviewWindow(embeddedExample: true);
        var card = cardOwner.TakeExampleCard();
        var host = new Border { Child = card };
        cardOwner.ShowState(new LearningPreviewState { RequestId = "sample", Source = "你好", Result = "Hello",
            Status = LearningPreviewStatus.Ready, Options = new() }, new ScreenRect(0, 0, 1, 1), false);
        host.Measure(new Size(450, 400)); host.Arrange(new Rect(0, 0, 450, 400)); host.UpdateLayout();
        Assert.False(cardOwner.IsVisible); Assert.Null(cardOwner.Content); Assert.Same(card, host.Child);
        var text = (TextBlock)cardOwner.FindName("ResultText");
        Assert.Equal("Hello", new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)cardOwner.FindName("CopyActions")).Visibility);
        return Task.CompletedTask;
    });
}
