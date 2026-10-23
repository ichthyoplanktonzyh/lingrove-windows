using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Cotabby.App.Learning;
using Cotabby.App.UI.Learning;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Adapts the pinned Mac dictionary-card and explanation-view regression responsibilities to the
/// actual WPF window. Frozen snapshots stand in for the existing Core session; no provider runs.
/// </summary>
[Collection("WPF views")]
public sealed class LearningDictionaryViewTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ContextualCardPaintsLiteralRangesAndKeepsResponseAndExampleDirectionsSeparate() => dispatcher.Run(() =>
    {
        var window = new LearningDictionaryWindow();
        try
        {
            const string raw = "<b>meaning</b> & cafe\u0301";
            var explanation = Explanation(raw, "<script>literal teaching text</script>");
            var state = Dictionary with { Explanation = explanation, ExplanationLanguage = LearningLanguage.Arabic };
            window.ShowState(state, Anchor, new LearningContextExampleTranslation("أبقني على اطلاع", LearningLanguage.Arabic));
            Assert.Equal(raw, Find<TextBlock>(window, "ContextualMeaning").Text);
            Assert.Equal(FlowDirection.RightToLeft, Find<TextBlock>(window, "ContextualMeaning").FlowDirection);
            var example = Find<TextBlock>(window, "ContextualExampleText");
            Assert.Equal(Input.Context, Text(example));
            Assert.Equal("keepposted", string.Concat(example.Inlines.OfType<Run>()
                .Where(run => run.FontWeight == FontWeights.SemiBold).Select(run => run.Text)));
            Assert.Equal(FlowDirection.LeftToRight, example.FlowDirection);
            Assert.Equal(FlowDirection.RightToLeft, Find<TextBlock>(window, "ContextualExampleTranslation").FlowDirection);
            Assert.Empty(Find<TextBlock>(window, "ExplanationProse").Inlines.OfType<Hyperlink>());
            Assert.Equal("test-local", Find<TextBlock>(window, "AttributionText").Text);
            AssertNonactivating(window);
            AssertPaints(window);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CommandsCarryDisplayedTargetAndExactSnapshotWithoutMutatingTheView() => dispatcher.Run(() =>
    {
        var window = new LearningDictionaryWindow();
        try
        {
            var state = Dictionary;
            var words = new List<(LearningDictionaryWord Word, Guid Target)>();
            var pages = new List<(LearningDictionaryPage Page, Guid Target)>();
            var explanations = new List<(Guid Target, LearningExplanationState State)>();
            Guid? back = null, close = null;
            window.WordRequested += (word, target) => words.Add((word, target));
            window.PageRequested += (page, target) => pages.Add((page, target));
            window.ExplanationRequested += (target, explanation) => explanations.Add((target, explanation));
            window.BackRequested += target => back = target;
            window.CloseRequested += target => close = target;
            window.ShowState(state, Anchor);
            var components = Find<WrapPanel>(window, "WordButtons").Children.OfType<Button>().ToArray();
            Assert.Equal(new[] { "keep", "me", "posted" }, components.Select(button => button.Content));
            Assert.All(components, button => Assert.False(button.Focusable));
            Click(components[2]);
            Assert.Equal((state.Unit!.Words[2], state.TargetIdentity), Assert.Single(words));
            Assert.Equal(state.Title, Find<TextBlock>(window, "TitleText").Text);
            Click(Find<Button>(window, "DictionaryPageButton"));
            Click(Find<Button>(window, "ContextualPageButton"));
            Assert.Equal(new[] { (LearningDictionaryPage.Dictionary, state.TargetIdentity),
                (LearningDictionaryPage.Contextual, state.TargetIdentity) }, pages);
            Assert.Equal(Visibility.Visible, Find<StackPanel>(window, "ContextualContent").Visibility);
            Click(Find<Button>(window, "ExplanationButton"));
            Assert.Equal(state.TargetIdentity, Assert.Single(explanations).Target);
            Assert.Same(state.Explanation, explanations[0].State);

            var selected = state with { SelectedWord = state.Unit.Words[2], TargetIdentity = Guid.NewGuid() };
            window.ShowState(selected, Anchor);
            Assert.Equal(Visibility.Collapsed, Find<StackPanel>(window, "WordSection").Visibility);
            Assert.Equal(Visibility.Visible, Find<Button>(window, "BackButton").Visibility);
            Click(Find<Button>(window, "BackButton")); Click(Find<Button>(window, "CloseButton"));
            Assert.Equal(selected.TargetIdentity, back); Assert.Equal(selected.TargetIdentity, close);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DetailsSurviveRefreshAndTabChangesButCollapseForChangedInput() => dispatcher.Run(() =>
    {
        var window = new LearningDictionaryWindow();
        try
        {
            var state = Dictionary;
            window.ShowState(state, Anchor);
            var details = Find<Expander>(window, "ExplanationDetails");
            details.IsExpanded = true;
            var refreshing = state.Explanation! with { IsGenerating = true, Revision = Guid.NewGuid() };
            window.ShowState(state with { Explanation = refreshing }, Anchor);
            Assert.True(details.IsExpanded);
            Assert.Equal(state.Explanation.Record!.Result.Meaning, Find<TextBlock>(window, "ContextualMeaning").Text);
            Assert.Equal(Visibility.Visible, Find<ProgressBar>(window, "ExplanationProgress").Visibility);
            Assert.Equal("Stop generating explanation", AutomationProperties.GetName(Find<Button>(window, "ExplanationButton")));
            window.ShowState(state with { Page = LearningDictionaryPage.Dictionary }, Anchor);
            window.ShowState(state with { Explanation = state.Explanation with { ErrorMessage = "Could not explain." } }, Anchor);
            Assert.True(details.IsExpanded);
            Assert.Equal("Try again", AutomationProperties.GetName(Find<Button>(window, "ExplanationButton")));
            Assert.Equal(state.Explanation.Record.Result.Explanation, Find<TextBlock>(window, "ExplanationProse").Text);

            var changed = new LearningExplanationInput("posted", "posted", LearningLanguage.English,
                VocabularyEntryKind.Word, "keep me posted", [new(8, 6)], LearningExplanationContext.Sentence);
            window.ShowState(state with { TargetIdentity = Guid.NewGuid(), Explanation = new() { Input = changed } }, Anchor);
            Assert.False(details.IsExpanded);
            Assert.Equal(Visibility.Collapsed, details.Visibility);
            Assert.Empty(Find<TextBlock>(window, "ContextualMeaning").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LongDefinitionsScrollInsideBoundedBodyWithAttributionAndNavigationOutside() => dispatcher.Run(() =>
    {
        var window = new LearningDictionaryWindow();
        try
        {
            string definition = string.Concat(Enumerable.Repeat("<b>literal definition</b> with several words. ", 150));
            var entry = new LearningLexiconEntry("fixture", "en", "keep someone posted", [],
                [new("verb", [definition], [])], "https://en.wiktionary.org/wiki/keep_someone_posted",
                sourceName: "Fixture attribution");
            var state = Dictionary with { Page = LearningDictionaryPage.Dictionary, Status = LearningDictionaryContentStatus.Result,
                Result = new LearningDictionaryResult.Entries([entry]) };
            window.ShowState(state, Anchor);
            var viewport = Find<ScrollViewer>(window, "DefinitionViewport");
            Assert.InRange(viewport.ActualHeight, 120, 300);
            Assert.True(viewport.ExtentHeight > viewport.ViewportHeight);
            var body = Find<StackPanel>(window, "DefinitionContent");
            var literal = Assert.Single(body.Children.OfType<TextBlock>(), block => block.Text.Contains("literal definition"));
            Assert.Equal("1. " + definition, literal.Text);
            Assert.Empty(literal.Inlines.OfType<Hyperlink>());
            Assert.Equal(Visibility.Visible, Find<WrapPanel>(window, "WordButtons").Visibility);
            var attribution = Find<TextBlock>(window, "AttributionText");
            Assert.Contains("CC BY-SA", Text(attribution));
            var footerBounds = attribution.TransformToAncestor(window).TransformBounds(new Rect(attribution.RenderSize));
            var viewportBounds = viewport.TransformToAncestor(window).TransformBounds(new Rect(viewport.RenderSize));
            Assert.True(footerBounds.Top >= viewportBounds.Bottom);
            Assert.True(footerBounds.Bottom <= window.ActualHeight);
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(Find<Button>(window, "DictionaryPageButton")));
            AssertPaints(window);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task UnconfiguredUnscopedTargetHasMeaningFallbackAndNoInventedExample() => dispatcher.Run(() =>
    {
        var window = new LearningDictionaryWindow();
        try
        {
            var input = LearningExplanationInputFactory.FromUnscopedUnit(Unit, LearningLanguage.English)!;
            var state = Dictionary with { ExplanationConfigured = false, Explanation = new() { Input = input } };
            window.ShowState(state, Anchor);
            Assert.Equal("Meaning", Find<Button>(window, "ContextualPageButton").Content);
            Assert.Equal("Configure AI explanation model", AutomationProperties.GetName(Find<Button>(window, "ExplanationButton")));
            Assert.Equal(Visibility.Collapsed, Find<StackPanel>(window, "ContextualExample").Visibility);
            Assert.Equal(Visibility.Visible, Find<StackPanel>(window, "PageButtons").Visibility);
            Assert.Empty(Find<TextBlock>(window, "ContextualExampleText").Inlines);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ChangedPageAndFailureReplaceDefinitionTextAndLongTitleKeepsItsThreeLineBound() => dispatcher.Run(() =>
    {
        var window = new LearningDictionaryWindow();
        try
        {
            var state = Dictionary with { Unit = new(0, string.Concat(Enumerable.Repeat("a long expression ", 50)), "dictionary form",
                LearningUnitKind.Expression, [], []), Page = LearningDictionaryPage.Dictionary,
                Status = LearningDictionaryContentStatus.Failed, ErrorMessage = "Could not look up." };
            window.ShowState(state, Anchor);
            var title = Find<TextBlock>(window, "TitleText");
            Assert.InRange(title.ActualHeight, 1, 60);
            Assert.Equal("Dictionary form: dictionary form", Find<TextBlock>(window, "QueryText").Text);
            Assert.Contains(Find<StackPanel>(window, "DefinitionContent").Children.OfType<TextBlock>(), b => b.Text == "Could not look up.");
            window.ShowState(state with { Page = LearningDictionaryPage.Contextual }, Anchor);
            Assert.Empty(Find<StackPanel>(window, "DefinitionContent").Children);
            Assert.Equal("test-local", Find<TextBlock>(window, "AttributionText").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task HideDropsTargetAndLiteralContentAndReopenStartsCollapsed() => dispatcher.Run(() =>
    {
        var window = new LearningDictionaryWindow();
        try
        {
            window.ShowState(Dictionary, Anchor);
            Find<Expander>(window, "ExplanationDetails").IsExpanded = true;
            int requests = 0;
            window.ExplanationRequested += (_, _) => requests++;
            window.HideCard();
            Assert.False(window.IsVisible);
            foreach (string name in new[] { "TitleText", "QueryText", "ContextualMeaning", "ExplanationProse", "ContextualExampleTranslation" })
                Assert.Empty(Find<TextBlock>(window, name).Text);
            Assert.Empty(Find<TextBlock>(window, "ContextualExampleText").Inlines);
            Assert.Empty(Find<WrapPanel>(window, "WordButtons").Children);
            Click(Find<Button>(window, "ExplanationButton"));
            Assert.Equal(0, requests);
            window.ShowState(Dictionary, Anchor);
            Assert.False(Find<Expander>(window, "ExplanationDetails").IsExpanded);
        }
        finally { window.Close(); }
    });
}
