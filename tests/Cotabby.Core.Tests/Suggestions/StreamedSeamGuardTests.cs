using Cotabby.Core.Support;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class StreamedSeamGuardTests
{
    [Fact]
    public void WaitsForNaturalLanguageBoundaryBeforeAssessingFirstWord()
    {
        int checks = 0;
        CompletionSeamGuard.SpellingAssessment Assess(string _) { checks++; return CompletionSeamGuard.SpellingAssessment.Known; }
        Assert.Null(CompletionSeamGuard.VisibleText("Je veux ", "ecrir", Assess, isFinal: false));
        Assert.Null(CompletionSeamGuard.VisibleText("so gre", "atf", Assess, isFinal: false));
        Assert.Equal(0, checks);
        Assert.Equal("ecrire ", CompletionSeamGuard.VisibleText("Je veux ", "ecrire ", Assess, isFinal: false));
        Assert.True(checks > 0);
        Assert.Equal("你好", CompletionSeamGuard.VisibleText("Hello ", "你好", Assess, isFinal: false));
    }
}
