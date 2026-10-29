using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.ExpressionAssist;

public sealed class ExpressionAssistPolicyTests
{
    [Theory]
    [InlineData("??", "word？？", "？？")]
    [InlineData(";;", "文字;;", ";;")]
    [InlineData("✨✨", "✨✨", "✨✨")]
    public void MatchesCommittedTriggerAtAnyCaretBoundary(string configured, string before, string expected) =>
        Assert.Equal(expected, ExpressionAssistPolicy.MatchedTrigger(before, configured));

    [Theory]
    [InlineData("")][InlineData("?")][InlineData("hello")][InlineData("? ?")][InlineData("/?")][InlineData(":?")][InlineData("?????????")]
    public void RejectsInvalidOrReservedTriggers(string trigger) => Assert.NotNull(ExpressionAssistPolicy.TriggerError(trigger));

    [Fact]
    public void RequestContainsOnlyNearestParagraphAndWholeBoundedGraphemes()
    {
        var value = "PRIVATE\n" + string.Concat(Enumerable.Repeat("😀", 1000)) + "??" + new string('界', 900) + "\nUNRELATED";
        var start = value.IndexOf("??", StringComparison.Ordinal);
        var request = ExpressionAssistPolicy.Request(" 排除 ", value, start, 2, LearningLanguage.English, LearningLanguage.Chinese);
        Assert.Equal("排除", request.Query);
        Assert.Equal(700, new System.Globalization.StringInfo(request.LeftContext).LengthInTextElements);
        Assert.Equal(500, new System.Globalization.StringInfo(request.RightContext).LengthInTextElements);
        var prompt = ExpressionAssistPrompt.Render(request, 8192, 768);
        Assert.DoesNotContain("PRIVATE", prompt);
        Assert.DoesNotContain("UNRELATED", prompt);
        Assert.Throws<LearningTranslationException>(() => ExpressionAssistPrompt.Render(request, 128, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExpressionAssistPolicy.Request("x", "e\u0301", 1, 0, LearningLanguage.English, LearningLanguage.Chinese));
    }

    [Fact]
    public void ParsesExactInsertionDeduplicatesAndAllowsClarification()
    {
        var request = ExpressionAssistPolicy.Request("排除", "We can rule??.", 11, 2, LearningLanguage.English, LearningLanguage.Chinese);
        var result = ExpressionAssistPrompt.Parse("{\"candidates\":[{\"text\":\" out\",\"hint\":\"排除\"},{\"text\":\"out \",\"hint\":\"重复\"}]}", request);
        Assert.Single(result.Candidates);
        Assert.Equal(" out", result.Candidates[0].Text);
        Assert.Equal("We can rule out.", request.LeftContext + result.Candidates[0].Text + request.RightContext);
        Assert.Empty(ExpressionAssistPrompt.Parse("{\"candidates\":[],\"clarification\":\"哪一种？\"}", request).Candidates);
    }

    [Theory]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{\"text\":\"unfinished\"}]}")]
    [InlineData("{\"candidates\":[{\"text\":\"line\\nline\",\"hint\":\"hint\"}]}")]
    [InlineData("{\"candidates\":[{\"text\":\"We still cannot rule out caching\",\"hint\":\"hint\"}]}")]
    public void InvalidOrContextEchoingResultsCannotBeInserted(string raw)
    {
        var request = ExpressionAssistPolicy.Request("排除", "We still cannot ?? yet.", 16, 2, LearningLanguage.English, LearningLanguage.Chinese);
        Assert.Throws<ArgumentException>(() => ExpressionAssistPrompt.Parse(raw, request));
    }
}
