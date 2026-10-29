using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.Core.Tests.LanguageLearning;

/// <summary>Source-derived cases from LearningLookupPolicyTests.swift at frozen 35b; not a Swift execution.</summary>
public sealed class ReadingLookupPolicyTests
{
    private static string Lower(string value) => value.ToLowerInvariant();
    private static ReadingLookupInput Standalone => new("bank", null, LearningLanguage.Chinese, true, null);
    private static ReadingLookupInput Contextual => new("bank", LearningLanguage.English, LearningLanguage.Chinese, true,
        new("He sat on the bank of the river.", 14, 4, ReadingBoundary.Container, ReadingBoundary.Container, ReadingContextMethod.Sentence));

    [Fact]
    public void UnknownStandaloneOmitsSourceAndRejectsInventedContextTranslation()
    {
        var prompt = ReadingLookupPolicy.Prompt(Standalone, LearningLanguage.Japanese, 4096, 512);
        Assert.DoesNotContain("source_language", prompt); Assert.DoesNotContain("occurrence_ranges", prompt);
        Assert.Contains("Define the target word or expression in Japanese", prompt);
        var basic = ReadingLookupPolicy.Parse("{\"meaning\":\"銀行\",\"sentence_translation\":\"\"}", Standalone, Lower);
        Assert.Equal("銀行", basic.Meaning);
        Assert.Throws<LearningExplanationException>(() => ReadingLookupPolicy.Parse("{\"meaning\":\"銀行\",\"sentence_translation\":\"invented\"}", Standalone, Lower));
    }

    [Fact]
    public void ContextPairAndDetailsKeepExactOccurrenceAndAcceptedMeaning()
    {
        var prompt = ReadingLookupPolicy.Prompt(Contextual, LearningLanguage.Japanese, 4096, 512);
        Assert.Contains("\"location\":14", prompt); Assert.Contains("\"source_language\":\"en\"", prompt);
        Assert.Contains("Translate the whole context into Simplified Chinese", prompt);
        var basic = ReadingLookupPolicy.Parse("{\"meaning\":\"河岸\",\"sentence_translation\":\"他坐在河岸上。\"}", Contextual, Lower);
        var details = ReadingLookupPolicy.Prompt(Contextual, LearningLanguage.Chinese, 4096, 1024, basic);
        Assert.Contains("accepted_meaning", details); Assert.Contains("河岸", details); Assert.Contains("plain text, without JSON", details);
        Assert.Equal("bank 是名词。", ReadingLookupPolicy.ParseDetails("bank 是名词。"));
        Assert.Throws<LearningTranslationException>(() => ReadingLookupPolicy.Prompt(Contextual, LearningLanguage.Chinese, 100, 90));
    }

    [Theory]
    [InlineData("{\"meaning\":\"河岸\"}")]
    [InlineData("{\"meaning\":\"河岸\",\"sentence_translation\":\"\"}")]
    [InlineData("{\"meaning\":\"河岸\",\"sentence_translation\":\"译文\",\"explanation\":\"extra\"}")]
    [InlineData("{\"meaning\":\"unknown\",\"sentence_translation\":\"译文\"}")]
    public void InvalidPairsNeverBecomeResults(string raw) =>
        Assert.Throws<LearningExplanationException>(() => ReadingLookupPolicy.Parse(raw, Contextual, Lower));

    [Fact]
    public void LocalGrammarIsFixedAndDistinguishesStandalone()
    {
        Assert.Contains("ws empty ws", ReadingLookupGrammar.ForContext(false));
        Assert.Contains("ws string ws", ReadingLookupGrammar.ForContext(true));
        Assert.DoesNotContain("\r\n", ReadingLookupGrammar.ForContext(false));
    }
}
