using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningPreviewCopyPolicyTests
{
    private static LearningPreviewState Ready => new()
    {
        RequestId = "doc-a", Source = "  原文。\n", Result = "  cafe\u0301 <b>literal</b> 👩🏽‍💻\n",
        Status = LearningPreviewStatus.Ready, Options = new(),
    };

    [Theory]
    [InlineData(LearningPreviewStatus.Loading)]
    [InlineData(LearningPreviewStatus.Ready)]
    public void CapturedSourceCopiesExactlyAcrossTranslationProgress(LearningPreviewStatus status)
    {
        var shown = Ready with { Status = LearningPreviewStatus.Loading, Result = null };
        var current = Ready with { Status = status };
        Assert.Equal(shown.Source, LearningPreviewCopyPolicy.Text(new(shown, LearningPreviewCopyKind.Source), current));
    }

    [Fact]
    public void OnlyACompleteExactTranslationCopiesAndLexicalUpdatesDoNotRetireIt()
    {
        var shown = Ready;
        var request = new LearningPreviewCopyRequest(shown, LearningPreviewCopyKind.Translation);
        Assert.Equal(shown.Result, LearningPreviewCopyPolicy.Text(request, shown with { UnitStage = LearningUnitStage.Ready }));
        Assert.Null(LearningPreviewCopyPolicy.Text(request, shown with { Result = shown.Result!.Normalize() }));
        Assert.Null(LearningPreviewCopyPolicy.Text(request, shown with { Status = LearningPreviewStatus.Loading, PartialResult = shown.Result }));
        var partial = shown with { Status = LearningPreviewStatus.Loading, Result = null, PartialResult = shown.Result };
        Assert.Null(LearningPreviewCopyPolicy.Text(new(partial, LearningPreviewCopyKind.Translation), partial));
        Assert.Null(LearningPreviewCopyPolicy.Text(request, shown with { Status = LearningPreviewStatus.Failed }));
    }

    [Fact]
    public void EqualTextInAnotherRequestChangedSourceOrDirectionCannotAuthorizeCopy()
    {
        var shown = Ready;
        foreach (var kind in new[] { LearningPreviewCopyKind.Source, LearningPreviewCopyKind.Translation })
        {
            var request = new LearningPreviewCopyRequest(shown, kind);
            Assert.Null(LearningPreviewCopyPolicy.Text(request, shown with { RequestId = "doc-b" }));
            Assert.Null(LearningPreviewCopyPolicy.Text(request, shown with { Source = "原文。" }));
            Assert.Null(LearningPreviewCopyPolicy.Text(request, shown with { Options = new() { TargetLanguage = LearningLanguage.French } }));
            Assert.Null(LearningPreviewCopyPolicy.Text(request, shown with { Status = LearningPreviewStatus.Cancelled }));
            Assert.Null(LearningPreviewCopyPolicy.Text(request, shown with { Status = LearningPreviewStatus.Failed }));
            Assert.Null(LearningPreviewCopyPolicy.Text(request, null));
        }
    }

    [Fact]
    public void EmptyOrInvalidDocumentAndEmbeddedNulNeverBecomeAClipboardPayload()
    {
        var shown = Ready;
        foreach (var invalid in new[] { shown with { RequestId = "" }, shown with { Source = "" }, shown with { Options = null }, shown with { Result = "" },
            shown with { Result = "before\0after" }, shown with { Status = LearningPreviewStatus.Idle } })
            Assert.Null(LearningPreviewCopyPolicy.Text(new(invalid, LearningPreviewCopyKind.Translation), invalid));
        Assert.Null(LearningPreviewCopyPolicy.Text(new(shown, (LearningPreviewCopyKind)99), shown));
    }
}
