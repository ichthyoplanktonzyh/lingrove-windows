using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class ReadingLanguagePolicyTests
{
    [Fact]
    public void ShortAmbiguousWordUsesCompatibleContextBeforeSelectionOrHint()
    {
        var evidence = new Evidence(sample => sample == "chat" ? [new("en", .8)] : [new("fr", .9)], true);
        Assert.Equal(new ReadingLanguageResolution(LearningLanguage.French, ReadingLanguageEvidence.Context),
            ReadingLanguagePolicy.Resolve("chat", "Le chat dort ici.", LearningLanguage.English, evidence));
    }
    [Fact]
    public void IndependentSelectionWinsAndUnsupportedLanguageRemainsUnknown()
    {
        var evidence = new Evidence(_ => [new("sv", .9)], true);
        Assert.Equal(new ReadingLanguageResolution(null, ReadingLanguageEvidence.Selection),
            ReadingLanguagePolicy.Resolve("Some long clear sentence", "context", LearningLanguage.English, evidence));
    }
    [Fact]
    public void WeakOrInvalidProbabilitiesDoNotInventALanguage()
    {
        var evidence = new Evidence(_ => [new("en", .6), new("fr", .5), new("de", double.NaN)], false);
        Assert.Equal(new ReadingLanguageResolution(null, ReadingLanguageEvidence.Unavailable),
            ReadingLanguagePolicy.Resolve("chat", null, LearningLanguage.Chinese, evidence));
    }
    private sealed class Evidence(Func<string, IReadOnlyList<ReadingLanguageHypothesis>> recognize, bool compatible) : IReadingLanguageEvidence
    {
        public IReadOnlyList<ReadingLanguageHypothesis> Recognize(string sample) => recognize(sample);
        public bool HasNonLatinLetter(string sample) => false;
        public bool IsCompatible(string sample, string code) => compatible;
    }
}
