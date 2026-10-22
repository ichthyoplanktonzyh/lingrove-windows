using Cotabby.App.Learning;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Exercises the actual non-WPF action/document projection used by the writing card.</summary>
public sealed class LearningExplanationCardPolicyTests
{
    private static LearningExplanationInput Input => new("Hello", "Hello", LearningLanguage.English,
        VocabularyEntryKind.Word, "Hello", [new(0, 5)], LearningExplanationContext.None);
    private static LearningExplanationRecord Record => new(Guid.NewGuid(), Input, LearningLanguage.English,
        new("A greeting", "Use this to greet someone."), "local", DateTimeOffset.UtcNow);
    private static LearningPreviewState Preview => new()
    {
        RequestId = "doc-a", Source = "你好", Result = "Hello", Status = LearningPreviewStatus.Ready,
        Options = new() { SourceLanguage = LearningLanguage.Chinese, TargetLanguage = LearningLanguage.English },
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingTargetNeverOffersGenerationOrConfiguration(bool configured)
    {
        var state = new LearningExplanationState { IsGenerating = true, Record = Record, ErrorMessage = "error" };
        Assert.Equal(LearningExplanationCardAction.None, LearningExplanationCardPolicy.Action(state, configured));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopWinsOverConfigurationRefreshAndErrors(bool configured)
    {
        var state = new LearningExplanationState { Input = Input, IsGenerating = true, Record = Record, ErrorMessage = "error" };
        Assert.Equal(LearningExplanationCardAction.Stop, LearningExplanationCardPolicy.Action(state, configured));
        Assert.Equal("Stop generating explanation", LearningExplanationCardPolicy.Label(state, configured));
    }

    [Fact]
    public void ConfigurationWinsOverSavedResultOrError()
    {
        var state = new LearningExplanationState { Input = Input, Record = Record, ErrorMessage = "error" };
        Assert.Equal(LearningExplanationCardAction.Configure, LearningExplanationCardPolicy.Action(state, false));
        Assert.Equal("Configure AI explanation model", LearningExplanationCardPolicy.Label(state, false));
    }

    [Fact]
    public void ExplicitActionTracksFreshCompleteAndFailedRefreshStates()
    {
        var state = new LearningExplanationState { Input = Input };
        Assert.Equal(LearningExplanationCardAction.Explain, LearningExplanationCardPolicy.Action(state, true));
        Assert.Equal("AI explanation", LearningExplanationCardPolicy.Label(state, true));
        state = state with { Record = Record };
        Assert.Equal(LearningExplanationCardAction.Refresh, LearningExplanationCardPolicy.Action(state, true));
        Assert.Equal("Regenerate explanation", LearningExplanationCardPolicy.Label(state, true));
        state = state with { ErrorMessage = "fixed error" };
        Assert.Equal(LearningExplanationCardAction.Refresh, LearningExplanationCardPolicy.Action(state, true));
        Assert.Equal("Try again", LearningExplanationCardPolicy.Label(state, true));
        Assert.NotNull(state.Record); // Presentation never discards the retained complete answer.
    }

    [Theory]
    [InlineData(LearningPreviewStatus.Idle)]
    [InlineData(LearningPreviewStatus.Loading)]
    [InlineData(LearningPreviewStatus.Failed)]
    [InlineData(LearningPreviewStatus.Cancelled)]
    public void PartialOrRetiredTranslationCannotOwnExplanation(LearningPreviewStatus status)
    {
        var ready = Preview;
        var other = ready with { Status = status, PartialResult = ready.Result };
        Assert.False(LearningExplanationCardPolicy.SameDocument(ready, other));
        Assert.False(LearningExplanationCardPolicy.SameDocument(other, ready));
    }

    [Fact]
    public void DocumentIdentityIsIndependentOfEqualTargetTextAndLexicalStage()
    {
        var ready = Preview;
        Assert.True(LearningExplanationCardPolicy.SameDocument(ready, ready with { UnitStage = LearningUnitStage.Ready }));
        Assert.False(LearningExplanationCardPolicy.SameDocument(ready, ready with { RequestId = "doc-b" }));
        Assert.False(LearningExplanationCardPolicy.SameDocument(ready, ready with { Source = "您好" }));
        Assert.False(LearningExplanationCardPolicy.SameDocument(ready, ready with { Result = "Hi" }));
        Assert.False(LearningExplanationCardPolicy.SameDocument(ready, ready with
        { Options = new() { SourceLanguage = LearningLanguage.French, TargetLanguage = LearningLanguage.English } }));
    }

    [Fact]
    public void CanonicalEqualityCannotReplaceTheExactDocumentReceipt()
    {
        var ready = Preview with { Result = "café" };
        Assert.False(LearningExplanationCardPolicy.SameDocument(ready, ready with { Result = "cafe\u0301" }));
        ready = Preview with { Source = "café" };
        Assert.False(LearningExplanationCardPolicy.SameDocument(ready, ready with { Source = "cafe\u0301" }));
    }

    [Fact]
    public void EmptyUnknownOrIncompleteDocumentsCannotEnableTheCard()
    {
        var ready = Preview;
        Assert.False(LearningExplanationCardPolicy.SameDocument(null, ready));
        Assert.False(LearningExplanationCardPolicy.SameDocument(ready, null));
        foreach (var invalid in new[] { ready with { RequestId = "" }, ready with { Source = "" }, ready with { Result = "" },
            ready with { Result = null }, ready with { Options = null } })
        {
            Assert.False(LearningExplanationCardPolicy.SameDocument(invalid, ready));
            Assert.False(LearningExplanationCardPolicy.SameDocument(ready, invalid));
        }
    }
}
