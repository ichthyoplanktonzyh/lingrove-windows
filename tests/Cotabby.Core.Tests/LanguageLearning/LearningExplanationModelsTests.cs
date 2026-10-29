using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningExplanationModelsTests
{
    [Fact]
    public void CaptureDetachesRangesAndPreservesCanonicalValueEqualityWithoutChangingText()
    {
        var ranges = new[] { new VocabularyTextRange(0, 2), new VocabularyTextRange(0, 2) };
        var first = new LearningExplanationInput("é", "é", LearningLanguage.French, VocabularyEntryKind.Word, "e\u0301", ranges, occurrenceIdentity: "é");
        ranges[0] = new(1, 1);
        var second = new LearningExplanationInput("e\u0301", "e\u0301", LearningLanguage.French, VocabularyEntryKind.Word,
            "e\u0301", [new(0, 2), new(0, 2)], occurrenceIdentity: "e\u0301");
        Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("é", first.Target); Assert.Equal("e\u0301", second.Target);
        Assert.Equal(LearningExplanationContext.Fragment, first.ContextKind);
        var withoutReceipt = new LearningExplanationInput("x", "", LearningLanguage.English, VocabularyEntryKind.Word, "x", [new(0, 1)]);
        Assert.DoesNotContain("occurrenceIdentity", JsonSerializer.Serialize(withoutReceipt));
        Assert.Equal(new VocabularyTextRange(0, 2), first.Ranges[0]);
        Assert.NotEqual(first, new LearningExplanationInput("é", "é", LearningLanguage.French, VocabularyEntryKind.Word,
            "e\u0301", [new(0, 2)], occurrenceIdentity: "é"));
    }

    [Fact]
    public void FrozenValueRoundtripsItsNamedMacFieldsButPromptOmitsTheLocalReceipt()
    {
        var input = new LearningExplanationInput("know", "know", LearningLanguage.English, VocabularyEntryKind.Word,
            "Let me know.", [new(7, 4)], LearningExplanationContext.Sentence, "local-receipt");
        string json = JsonSerializer.Serialize(input);
        Assert.Equal(input, JsonSerializer.Deserialize<LearningExplanationInput>(json));
        Assert.Contains("\"contextKind\":\"sentence\"", json); Assert.Contains("\"kind\":\"word\"", json);
        Assert.Contains("\"language\":\"en\"", json);
        string prompt = LearningExplanationPolicy.Prompt(input, LearningLanguage.Chinese, 8192, 1024);
        Assert.DoesNotContain("local-receipt", prompt); Assert.DoesNotContain("occurrenceIdentity", prompt);
    }

    [Fact]
    public void CurrentSourceAllowsUnknownLanguageAndUnverifiedContextWithoutInventingHint()
    {
        // Derived from 3b LearningExplanationModels and LearningExplanationPolicy, not Swift execution.
        var input = new LearningExplanationInput("bank", "bank", null, VocabularyEntryKind.Word,
            "river bank", [new(6, 4)], LearningExplanationContext.Unknown, "receipt");
        Assert.True(LearningExplanationPolicy.IsValid(input));
        var json = JsonSerializer.Serialize(input);
        Assert.DoesNotContain("\"language\"", json); Assert.Contains("\"contextKind\":\"unknown\"", json);
        Assert.Equal(input, JsonSerializer.Deserialize<LearningExplanationInput>(json));
        var prompt = LearningExplanationPolicy.CurrentPrompt(input, LearningLanguage.Chinese, 8192, 1024);
        Assert.DoesNotContain("targetLanguage", prompt); Assert.DoesNotContain("receipt", prompt);
        Assert.Contains("The context boundaries are unverified.", prompt);
        Assert.Contains("\"contextCompleteness\":\"unknown\"", prompt);
    }

    [Fact]
    public void CanonicalExplanationRetainsSourceDateBitsAcrossLiveProjection()
    {
        var input = new LearningExplanationInput("bank", "bank", null, VocabularyEntryKind.Word, "bank", [new(0, 4)], LearningExplanationContext.Unknown);
        var live = new LearningExplanationRecord(Guid.NewGuid(), input, LearningLanguage.Chinese, new("meaning", "details"), "model", DateTimeOffset.UtcNow);
        var exact = new Cotabby.Core.Vocabulary.Frozen.SourceDate(123.123456789123);
        var stored = Cotabby.Core.Vocabulary.Frozen.CanonicalWritingPolicy.FromLive(live) with { CreatedAt = exact };
        var projected = Cotabby.Core.Vocabulary.Frozen.CanonicalWritingPolicy.ToLive(stored);
        Assert.Equal(exact, Cotabby.Core.Vocabulary.Frozen.CanonicalWritingPolicy.FromLive(projected).CreatedAt);
        Assert.DoesNotContain("CanonicalCreatedAt", JsonSerializer.Serialize(projected));
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void UnpairedDotNetUtf16HasNoSwiftValueAndFailsClosed(int surrogate)
    {
        // Attribute metadata serializes strings as UTF-8, replacing a lone surrogate before the
        // test sees it. Construct the malformed .NET-only value at runtime instead.
        string malformed = new((char)surrogate, 1);
        var input = new LearningExplanationInput(malformed, "x", LearningLanguage.English, VocabularyEntryKind.Word, "x", [new(0, 1)]);
        Assert.False(LearningExplanationPolicy.IsValid(input));
        Assert.False(LearningExplanationPolicy.IsValid(new LearningExplanationResult("x", malformed)));
    }

    [Fact]
    public void ResultsUseCanonicalEqualityAndKeepTheOriginalResponseEncoding()
    {
        var first = new LearningExplanationResult("é", "déjà");
        var second = new LearningExplanationResult("e\u0301", "de\u0301ja\u0300");
        Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("e\u0301", second.Meaning);
        Assert.Equal(first, JsonSerializer.Deserialize<LearningExplanationResult>(JsonSerializer.Serialize(first)));
    }
}
