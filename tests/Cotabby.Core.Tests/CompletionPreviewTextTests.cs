using Cotabby.Core.Settings;

namespace Cotabby.Core.Tests;

public sealed class CompletionPreviewTextTests
{
    [Theory]
    [InlineData("Thanks for the¦ quick reply!", "Thanks for the", " quick reply!")]
    [InlineData("明日¦、また会いましょう", "明日", "、また会いましょう")]
    [InlineData("A whole sentence", "A whole sentence", "")]
    [InlineData("A¦ whole¦ sentence", "A whole sentence", "")]
    [InlineData("This is how your¦ suggestions will look", "This is how your", " suggestions will look")]
    [InlineData("输入👩‍💻¦继续书写", "输入👩‍💻", "继续书写")]
    [InlineData("¦suffix", "", "suffix")]
    [InlineData("prefix¦", "prefix", "")]
    [InlineData("readable sentence", "readable sentence", "")]
    [InlineData("a¦b¦c", "abc", "")]
    [InlineData("¦¦", "", "")]
    [InlineData("", "", "")]
    public void PreservesTheWholeLocalizedSentenceAndDegradesMalformedMarkers(string sentence, string typed, string ghost)
    {
        var result = new CompletionPreviewText(sentence);
        Assert.Equal(typed, result.TypedText);
        Assert.Equal(ghost, result.GhostText);
    }
}
