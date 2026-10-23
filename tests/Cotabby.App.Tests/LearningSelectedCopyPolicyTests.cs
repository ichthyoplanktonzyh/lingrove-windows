using Cotabby.App.Learning;
using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Tests;

public sealed class LearningSelectedCopyPolicyTests
{
    private static LearningPreviewState Preview => new() { RequestId = "doc-a", Source = "原文。", Result = "keep me posted",
        Status = LearningPreviewStatus.Ready, Options = new() };

    [Fact]
    public void ExactParentDocumentAllowsLiteralSelectedProseAcrossLexicalUpdates()
    {
        var document = Preview;
        var lease = new SelectedTextCopyLease("  <b>cafe\u0301</b> 👩🏽‍💻\n", document.RequestId);
        var request = new LearningSelectedCopyRequest(lease, document);
        Assert.True(LearningSelectedCopyPolicy.IsCurrent(request, document with { UnitStage = LearningUnitStage.Ready }));
        Assert.Equal("  <b>cafe\u0301</b> 👩🏽‍💻\n", lease.Text);
    }

    [Fact]
    public void RevokedSelectionCannotCommitEvenIfItsDocumentIsStillCurrent()
    {
        var document = Preview;
        var lease = new SelectedTextCopyLease("meaning", document.RequestId);
        var request = new LearningSelectedCopyRequest(lease, document);
        lease.Revoke();
        Assert.False(LearningSelectedCopyPolicy.IsCurrent(request, document));
    }

    [Fact]
    public void NewOrChangedOrProvisionalParentNeverInheritsTheSelectedCopy()
    {
        var document = Preview;
        var request = new LearningSelectedCopyRequest(new("meaning", document.RequestId), document);
        foreach (var current in new[] { document with { RequestId = "doc-b" }, document with { Source = "another source" },
            document with { Result = "another result" }, document with { Options = new() { TargetLanguage = LearningLanguage.Arabic } },
            document with { Status = LearningPreviewStatus.Loading, PartialResult = document.Result },
            document with { Status = LearningPreviewStatus.Cancelled } })
            Assert.False(LearningSelectedCopyPolicy.IsCurrent(request, current));
        Assert.False(LearningSelectedCopyPolicy.IsCurrent(request, null));
    }

    [Fact]
    public void DictionarySelectionRemainsOnItsExactContextualTargetAndDocument()
    {
        var document = Preview; var target = Guid.NewGuid();
        var word = new LearningDictionaryWord("posted", new(8, 6));
        var dictionary = new LearningDictionaryState { TargetIdentity = target, DocumentId = document.RequestId,
            Unit = LearningUnit.Word(word), HasContextualExplanation = true, Page = LearningDictionaryPage.Contextual };
        var request = new LearningSelectedCopyRequest(new("meaning", target), document, target);
        Assert.True(LearningSelectedCopyPolicy.IsCurrent(request, document, dictionary));
        foreach (var other in new[] { dictionary with { TargetIdentity = Guid.NewGuid() }, dictionary with { DocumentId = "doc-b" },
            dictionary with { Unit = null }, dictionary with { HasContextualExplanation = false },
            dictionary with { Page = LearningDictionaryPage.Dictionary } })
            Assert.False(LearningSelectedCopyPolicy.IsCurrent(request, document, other));
        Assert.False(LearningSelectedCopyPolicy.IsCurrent(request, document));
    }

    [Fact]
    public void MissingOrWrongIdentityAndInvalidPayloadCannotReachAWriter()
    {
        var document = Preview;
        foreach (var lease in new[] { new SelectedTextCopyLease("meaning", "doc-b"), new("meaning", Guid.NewGuid()),
            new("", document.RequestId), new("before\0after", document.RequestId) })
            Assert.False(LearningSelectedCopyPolicy.IsCurrent(new(lease, document), document));
        var decomposed = document with { Source = "cafe\u0301" };
        Assert.False(LearningSelectedCopyPolicy.IsCurrent(new(new("meaning", document.RequestId), decomposed),
            decomposed with { Source = "café" }));
    }
}
