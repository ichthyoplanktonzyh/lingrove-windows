using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Text;

namespace Cotabby.Inference.Tests.LanguageLearning;

public sealed class IcuWritingScriptEvidenceTests
{
    [Theory]
    [InlineData("hello", LearningLanguage.Chinese, false)]
    [InlineData("你好", LearningLanguage.Chinese, true)]
    [InlineData("かな", LearningLanguage.Japanese, true)]
    [InlineData("한국어", LearningLanguage.Korean, true)]
    [InlineData("hello", LearningLanguage.English, true)]
    public void WindowsIcuProvidesSourceScriptEvidence(string text, LearningLanguage language, bool expected) =>
        Assert.Equal(expected, IcuWritingScriptEvidence.Accepts(text, language));
}
