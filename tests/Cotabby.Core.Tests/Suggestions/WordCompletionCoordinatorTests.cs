using Cotabby.Core.Spelling;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class WordCompletionCoordinatorTests() : CoordinatorHarness(
    new TypoCorrectionAdvisor(new SymSpellCorrector(_ => null), new SpellingLanguageResolver(_ => null)))
{
    [Fact]
    public async Task UnusableFinalUsesUniqueDocumentWordAndInsertsOnlyMissingSuffix()
    {
        Engine.Completion = _ => "";
        Type("schedule and sched");
        await Shown("ule");
        Assert.True(Hook.Press(Backtick));
        await Eventually(() => Focus.Current!.Text == "schedule and schedule", "the suffix was accepted");
        Assert.Single(Engine.Requests);
    }

    [Fact]
    public async Task MalformedFinalFallsBackToReferenceWithoutReplacingTypedText()
    {
        Engine.Completion = _ => " schedule a meeting";
        Type("schedule and sched");
        await Shown("ule");
        Assert.True(Hook.Press(Backtick));
        await Eventually(() => Focus.Current!.Text == "schedule and schedule", "the suffix was accepted");
        Assert.Single(Engine.Requests);
    }
}
