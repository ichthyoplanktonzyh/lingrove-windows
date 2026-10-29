using Cotabby.Win32.Text;

namespace Cotabby.Inference.Tests.LanguageLearning;

public sealed class ExpressionAssistCaseNativeTests
{
    [Fact]
    public void FullUnicodeLowercaseDoesNotApplyLexicalKeyProjection()
    {
        var text = new IcuLearningLexicalText();
        Assert.Equal("i\u0307", text.Lowercase("İ"));
        Assert.Equal("e\u0301 ’", text.Lowercase("E\u0301 ’"));
    }
}
