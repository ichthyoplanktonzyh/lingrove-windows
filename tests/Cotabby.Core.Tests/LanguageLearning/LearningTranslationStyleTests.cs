using System.Globalization;
using System.Text;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningTranslationStyleTests
{
    [Fact]
    public void NaturalStyleAddsNoPreferenceAndEffectiveStylesDropDormantCustomText()
    {
        var natural = new LearningTranslationStyle();
        var formal = new LearningTranslationStyle(LearningTranslationRegister.Formal, "old custom text");
        var conversational = new LearningTranslationStyle(LearningTranslationRegister.Conversational);

        Assert.Null(natural.Instruction);
        Assert.Equal("Use a formal written register suitable for documents.", formal.Instruction);
        Assert.Equal("Use natural everyday conversational phrasing.", conversational.Instruction);
        Assert.Equal(new LearningTranslationStyle(LearningTranslationRegister.Formal), formal.Effective);
        Assert.Equal("Keep it short.", new LearningTranslationStyle(
            LearningTranslationRegister.Custom, "  Keep it short.  ").Instruction);
        var custom = new LearningTranslationStyle(LearningTranslationRegister.Custom, "  Keep it short.  ");
        Assert.Equal(custom.Normalized, custom.Effective);
        Assert.Null(new LearningTranslationStyle(LearningTranslationRegister.Custom, " \n\t ").Instruction);
    }

    [Fact]
    public void CustomTextIsCappedAtFourThousandUtf8BytesThenOneThousandGraphemes()
    {
        var source = string.Concat(Enumerable.Repeat("你好", 5_000));
        var normalized = new LearningTranslationStyle(LearningTranslationRegister.Custom, source).Normalized;

        Assert.Equal(1_000, StringInfo.ParseCombiningCharacters(normalized.CustomInstruction).Length);
        Assert.Equal(3_000, Encoding.UTF8.GetByteCount(normalized.CustomInstruction));
        var combining = string.Concat(Enumerable.Repeat("e\u0301", 1_500));
        var normalizedCombining = new LearningTranslationStyle(
            LearningTranslationRegister.Custom, combining).Normalized.CustomInstruction;
        Assert.Equal(1_000, StringInfo.ParseCombiningCharacters(normalizedCombining).Length);
        Assert.Equal(string.Concat(Enumerable.Repeat("e\u0301", 1_000)), normalizedCombining);
        Assert.Equal(new string('a', 1_000), new LearningTranslationStyle(
            LearningTranslationRegister.Custom, new string('a', 5_000)).Normalized.CustomInstruction);
    }

    [Fact]
    public void AByteCutInsideAUnicodeScalarUsesTheSameReplacementBehaviorAsMac()
    {
        var source = string.Concat(
            string.Concat(Enumerable.Repeat("👩🏽‍💻", 266)),
            "aa😀a😀");

        var normalized = new LearningTranslationStyle(LearningTranslationRegister.Custom, source).Normalized.CustomInstruction;

        Assert.EndsWith("\uFFFD", normalized, StringComparison.Ordinal);
        Assert.Equal(4_000, Encoding.UTF8.GetByteCount(normalized));
        Assert.True(StringInfo.ParseCombiningCharacters(normalized).Length <= 1_000);
    }

    [Theory]
    [InlineData(LearningTranslationRegister.Natural, "natural")]
    [InlineData(LearningTranslationRegister.Formal, "formal")]
    [InlineData(LearningTranslationRegister.Conversational, "conversational")]
    [InlineData(LearningTranslationRegister.Custom, "custom")]
    public void RegistersSerializeWithTheirStableStringIdentifiers(LearningTranslationRegister register, string code)
    {
        Assert.Equal("\"" + code + "\"", JsonSerializer.Serialize(register));
        Assert.Equal(register, JsonSerializer.Deserialize<LearningTranslationRegister>("\"" + code + "\""));
    }
}
