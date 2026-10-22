using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningUnitDisplayPolicyTests
{
    [Fact]
    public void OriginalTextAndNeutralRangesStaySeparateFromWordClickTargets()
    {
        const string text = "😀 take your time, café.";
        var take = new LearningDictionaryWord("take", new(3, 4));
        var your = new LearningDictionaryWord("your", new(8, 4));
        var time = new LearningDictionaryWord("time", new(13, 4));
        var pattern = new LearningUnit(3, "take … time", "take one's time", LearningUnitKind.Pattern,
            [take.Range, time.Range], [take, your, time], "id");
        var slot = LearningUnit.Word(your);
        var runs = LearningUnitDisplayPolicy.Runs(text, [pattern, slot]);
        Assert.Equal(text, string.Concat(runs.Select(run => run.Text)));
        Assert.All(runs, run => Assert.Equal(run.Text, text.Substring(run.Range.Location, run.Range.Length)));
        Assert.Same(pattern, Assert.Single(runs, run => run.Text == "take").Target);
        Assert.Same(slot, Assert.Single(runs, run => run.Text == "your").Target);
        Assert.True(Assert.Single(runs, run => run.Text == "time").Underlined);
        Assert.False(Assert.Single(runs, run => run.Text == "your").Underlined);
        Assert.All(runs.Where(run => run.Word is null), run => Assert.Null(run.Target));
    }

    [Fact]
    public void ExpressionSpacesAreUnderlinedButHaveNoClickTargetAndRepeatedWordsKeepOccurrence()
    {
        const string text = "go go";
        var words = new[] { new LearningDictionaryWord("go", new(0, 2)), new LearningDictionaryWord("go", new(3, 2)) };
        var unit = new LearningUnit(0, text, text, LearningUnitKind.Expression, [new(0, 5)], words);
        var runs = LearningUnitDisplayPolicy.Runs(text, [unit]);
        var space = Assert.Single(runs, run => run.Text == " ");
        Assert.True(space.Underlined); Assert.Null(space.Word); Assert.Null(space.Target);
        Assert.Equal(new[] { words[0], words[1] }, runs.Where(run => run.Word is not null).Select(run => run.Word));
    }

    [Fact]
    public void InvalidGraphemeRangesAndOverlappingWordsFailClosedToLiteralText()
    {
        var bad = LearningUnit.Word(new("e", new(0, 1)));
        var runs = LearningUnitDisplayPolicy.Runs("e\u0301", [bad]);
        Assert.Equal("e\u0301", string.Concat(runs.Select(run => run.Text)));
        Assert.All(runs, run => Assert.Null(run.Target));
        var overlap = LearningUnitDisplayPolicy.Runs("word", [LearningUnit.Word(new("word", new(0, 4))),
            LearningUnit.Word(new("ord", new(1, 3)))]);
        Assert.Null(Assert.Single(overlap).Target);
        var crossedRanges = LearningUnitDisplayPolicy.Runs("one two", [
            new(0, "one ", "one", LearningUnitKind.Expression, [new(0, 4)], [new("one", new(0, 3))]),
            new(3, " two", "two", LearningUnitKind.Expression, [new(3, 4)], [new("two", new(4, 3))])]);
        Assert.Null(Assert.Single(crossedRanges).Target);
        Assert.Equal("one two", Assert.Single(crossedRanges).Text);
    }
}
