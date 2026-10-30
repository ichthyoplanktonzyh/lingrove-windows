using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningDraftReference3bTests
{
    private const string ReferenceStart = "REFERENCE EXCERPTS (JSON):\n";
    private const string TargetStart = "\n\nTARGET TRANSLATION TASK:\n";
    private static (string Before, string After) References(string prompt)
    {
        int start = prompt.IndexOf(ReferenceStart, StringComparison.Ordinal) + ReferenceStart.Length;
        int end = prompt.IndexOf(TargetStart, start, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(prompt[start..end]);
        return (json.RootElement.GetProperty("before_target").GetString()!, json.RootElement.GetProperty("after_target").GetString()!);
    }

    [Fact]
    public void ExtractionKeepsOnlyExplicitNearestReferenceWithoutChangingTheTarget()
    {
        string value = new string('a', 65_537) + "target" + new string('b', 65_537);
        var range = new LearningTextRange(65_537, 6);
        var current = LearningTranslationInput.Captured(value, range, "target", LearningReferenceMode.CurrentSentence);
        Assert.Empty(current.ReferenceBefore); Assert.Empty(current.ReferenceAfter);
        var draft = LearningTranslationInput.Captured(value, range, "target", LearningReferenceMode.Draft);
        Assert.Equal("target", draft.Source); Assert.Equal(new string('a', 65_536), draft.ReferenceBefore);
        Assert.Equal(new string('b', 65_536), draft.ReferenceAfter);
        var unicode = LearningTranslationInput.Captured("e\u0301target👋", new(2, 6), "target", LearningReferenceMode.Draft);
        Assert.Equal("e\u0301", unicode.ReferenceBefore); Assert.Equal("👋", unicode.ReferenceAfter);
        Assert.Empty(LearningTranslationInput.Captured(value, new(-1, 1), "target", LearningReferenceMode.Draft).ReferenceBefore);
    }

    [Theory]
    [InlineData(LearningTranslationPrompt.Family.Generic)]
    [InlineData(LearningTranslationPrompt.Family.HyMt)]
    [InlineData(LearningTranslationPrompt.Family.TranslateGemma)]
    public void EveryFamilyQuotesReferenceAndRetainsItsLiteralTargetAndFraming(LearningTranslationPrompt.Family family)
    {
        var input = new LearningTranslationInput("不要改变目标") { ReferenceBefore = "before /\"\n👋", ReferenceAfter = "after \u0001" };
        var bare = LearningTranslationPrompt.Render(input.Source, LearningLanguage.Chinese, LearningLanguage.English, family);
        var rendered = LearningTranslationPrompt.Render(input, LearningLanguage.Chinese, LearningLanguage.English, family);
        Assert.EndsWith(TargetStart + bare.Prompt, rendered.Prompt);
        Assert.Equal(input.ReferenceBefore, References(rendered.Prompt).Before); Assert.Equal(input.ReferenceAfter, References(rendered.Prompt).After);
        Assert.Contains("\\/", rendered.Prompt); Assert.Contains("👋", rendered.Prompt);
        Assert.True(rendered.Prompt.IndexOf("after_target", StringComparison.Ordinal) < rendered.Prompt.IndexOf("before_target", StringComparison.Ordinal));
        Assert.Equal(bare.UserOnly, rendered.UserOnly); Assert.Equal(bare.Sampling, rendered.Sampling);
    }

    [Fact]
    public void BinarySearchKeepsTheWinningPayloadRatherThanTheLastOversizedProbe()
    {
        var input = new LearningTranslationInput("target") { ReferenceBefore = new string('a', 200), ReferenceAfter = new string('b', 200) };
        int allowance = ModelContextBudget.InputAllowance(4096, 256, false); string? last = null;
        var plan = LearningTranslationBudget.PlanPrompt(input, LearningLanguage.Chinese, LearningLanguage.English,
            LearningTranslationPrompt.Family.Generic, 4096, 256, false, text =>
            {
                last = text;
                int length = text.Contains(ReferenceStart, StringComparison.Ordinal) ? References(text).Before.Length + References(text).After.Length : 0;
                return (allowance - 100 + length, text);
            });
        Assert.Equal(plan.Request.Prompt, plan.Payload); Assert.NotEqual(last, plan.Payload);
        Assert.Equal(75, References(plan.Request.Prompt).Before.Length); Assert.Equal(25, References(plan.Request.Prompt).After.Length);
        Assert.EndsWith("target\"", plan.Request.Prompt);
    }

    [Fact]
    public void OversizedTargetIsRejectedWithoutCroppingItToMakeRoomForReference()
    {
        int calls = 0; var input = new LearningTranslationInput("complete target") { ReferenceBefore = "reference" };
        Assert.Throws<LearningTranslationException>(() => LearningTranslationBudget.PlanPrompt(input, LearningLanguage.Chinese,
            LearningLanguage.English, LearningTranslationPrompt.Family.Generic, 1024, 256, false,
            _ => { calls++; return (int.MaxValue, "payload"); }));
        Assert.Equal(1, calls); Assert.Equal("complete target", input.Source);
    }

    [Fact]
    public void MalformedReferenceIsRejectedEvenIfTheBadPartWouldBeCropped()
    {
        var input = new LearningTranslationInput("target") { ReferenceBefore = "\ud800" + new string('x', 1000) };
        Assert.Throws<LearningTranslationException>(() => LearningTranslationBudget.Prompt(input, LearningLanguage.Chinese,
            LearningLanguage.English, LearningTranslationPrompt.Family.Generic, 1024, 256, false, _ => 1));
    }
}
