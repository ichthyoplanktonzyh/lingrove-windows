using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Text;

public sealed class FoundationUnicodeNormalizationTests
{
    [Theory]
    [InlineData("\ufdd0")]
    [InlineData("\ufffe")]
    [InlineData("\uffff")]
    [InlineData("\U0001fffe")]
    [InlineData("\U0010ffff")]
    public void ValidNoncharactersRemainBarriersWhileAdjacentTextNormalizes(string barrier)
    {
        var composed = "é" + barrier + "é";
        var decomposed = "e\u0301" + barrier + "e\u0301";
        var comparer = CanonicalStringComparer.Instance;
        Assert.True(comparer.Equals(composed, decomposed));
        Assert.Equal(comparer.GetHashCode(composed), comparer.GetHashCode(decomposed));
        Assert.Equal(0, comparer.Compare(composed, decomposed));
        Assert.Equal(composed, VocabularyCapturePolicy.Normalized(decomposed));
        Assert.False(comparer.Equals("e" + barrier + "\u0301", "é" + barrier));
    }

    [Fact]
    public void MalformedUtf16RemainsOrdinalAndRejectedByCaptureValidation()
    {
        var comparer = CanonicalStringComparer.Instance;
        Assert.False(comparer.Equals("\ud800é", "\ud800e\u0301"));
        Assert.True(comparer.Compare("\ud800", "\ufffe") > 0);
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.Normalized("\ud800"));
    }

    [Fact]
    public void ClassificationDetectsCanonicallyEquivalentCategoryKeysAroundNoncharacters()
    {
        var framework = new R.VocabularyClassificationFramework(Guid.NewGuid(), "framework", 1);
        var first = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("first")])
            { FrameworkId = framework.Id, Key = "\ufffeé" };
        var second = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("second")])
            { FrameworkId = framework.Id, Key = "\ufffee\u0301" };
        var index = new R.VocabularyClassificationIndex { Frameworks = [framework], Categories = [first] };
        R.VocabularyClassificationPolicy.Validate(index, []);
        var error = Assert.Throws<VocabularyException>(() => R.VocabularyClassificationPolicy.Validate(index with { Categories = [first, second] }, []));
        Assert.Equal(VocabularyFailure.InvalidContent, error.Failure);
    }

    [Fact]
    public void EditorSearchDoesNotThrowWhenFoldingNoncharacters()
    {
        Assert.Empty(VocabularyPolicy.ReconciledRanges("\ufffe", "\uffff"));
    }
}
