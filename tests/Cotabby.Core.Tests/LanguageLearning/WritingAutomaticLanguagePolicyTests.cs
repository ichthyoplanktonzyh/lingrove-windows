using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class WritingAutomaticLanguagePolicyTests
{
    [Fact]
    public void PassiveScriptRequiresSourceEvidenceAndDoesNotInferLanguage()
    {
        int? Script(int scalar) => scalar switch { 'a' => 25, '中' => 17, 'あ' => 20, _ => 0 };
        Assert.False(WritingAutomaticLanguagePolicy.Accepts("abc", LearningLanguage.Chinese, Script));
        Assert.True(WritingAutomaticLanguagePolicy.Accepts("a中", LearningLanguage.Chinese, Script));
        Assert.True(WritingAutomaticLanguagePolicy.Accepts("あ", LearningLanguage.Japanese, Script));
        Assert.False(WritingAutomaticLanguagePolicy.Accepts("中", LearningLanguage.English, Script));
        Assert.False(WritingAutomaticLanguagePolicy.Accepts("a", LearningLanguage.English, _ => null));
    }
}
