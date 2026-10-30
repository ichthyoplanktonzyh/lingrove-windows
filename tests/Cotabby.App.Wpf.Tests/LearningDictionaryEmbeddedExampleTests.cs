using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningDictionaryEmbeddedExampleTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task SampleDictionaryUsesRealCardWithoutNativeWindow() => dispatcher.RunAsync(() =>
    {
        var owner = new LearningDictionaryWindow(true); var card = owner.TakeExampleCard();
        var host = new Border { Child = card };
        var entry = new LearningLexiconEntry("sample", "en", "posted", [], [new("word", ["informed"], [])], "", sourceName: "Sample dictionary");
        var state = new LearningDictionaryState { Unit = new(0, "posted", "posted", LearningUnitKind.Word, [], []),
            Page = LearningDictionaryPage.Dictionary, Status = LearningDictionaryContentStatus.Result,
            Result = new LearningDictionaryResult.Entries([entry]) };
        owner.ShowState(state, new ScreenRect(0, 0, 1, 1));
        host.Measure(new Size(450, 400)); host.Arrange(new Rect(0, 0, 450, 400)); host.UpdateLayout();
        Assert.False(owner.IsVisible); Assert.Null(owner.Content); Assert.Same(card, host.Child);
        Assert.Equal("posted", ((TextBlock)owner.FindName("TitleText")).Text);
        owner.HideCard(); Assert.Empty(((TextBlock)owner.FindName("TitleText")).Text); return Task.CompletedTask;
    });
}
