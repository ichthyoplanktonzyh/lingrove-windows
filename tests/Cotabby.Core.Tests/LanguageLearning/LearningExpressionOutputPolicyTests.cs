using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningExpressionOutputPolicyTests
{
    [Fact]
    public void StreamSkipsWhitespacePrefixesAndPreservesInternalLineBreaks()
    {
        var stream = new LearningExpressionOutputPolicy.Stream();

        Assert.Null(stream.Append(" \n"));
        Assert.Equal("Hello", stream.Append("Hello "));
        Assert.Equal("Hello 世界", stream.Append("世界"));
        Assert.Equal("Hello 世界.\nSecond line.", stream.Append(".\nSecond line."));
    }

    [Fact]
    public void StreamAndCompleteOutputRejectUnsafeCharactersAndWholeUtf8Overflow()
    {
        var unsafeStream = new LearningExpressionOutputPolicy.Stream();
        Assert.Equal("Hello", unsafeStream.Append("Hello"));
        var unsafeError = Assert.Throws<LearningTranslationException>(() => unsafeStream.Append("\u0000world"));
        Assert.Equal(LearningTranslationFailure.InvalidOutput, unsafeError.Failure);

        var bounded = new LearningExpressionOutputPolicy.Stream();
        _ = bounded.Append(string.Concat(Enumerable.Repeat("界", 4_000)));
        Assert.Throws<LearningTranslationException>(() => bounded.Append("a"));
    }

    [Fact]
    public void FinalValidationTrimsOnlyEdgesAndKeepsInternalNewlines()
    {
        Assert.Equal("I did not send 2 files today.", LearningExpressionOutputPolicy.ValidatedText(" \nI did not send 2 files today.\n "));
        Assert.Equal("First line.\n\nSecond line.", LearningExpressionOutputPolicy.ValidatedText("\nFirst line.\n\nSecond line.\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t ")]
    [InlineData("I did\u0000 not send it.")]
    [InlineData("I did\u0001 not send it.")]
    [InlineData("I did\u0009 not send it.")]
    [InlineData("I did\u001B not send it.")]
    [InlineData("I did\u007F not send it.")]
    [InlineData("I did\uFFFD not send it.")]
    public void FinalValidationRejectsEmptyOrUnsafeOutput(string text) =>
        Assert.Throws<LearningTranslationException>(() => LearningExpressionOutputPolicy.ValidatedText(text));

    [Fact]
    public void CompleteResponseByteBudgetIsExact()
    {
        var atLimit = new string('a', LearningExpressionOutputPolicy.MaximumOutputBytes);
        var overLimit = new string('a', LearningExpressionOutputPolicy.MaximumOutputBytes + 1);

        Assert.Equal(atLimit, LearningExpressionOutputPolicy.ValidatedText(atLimit));
        var error = Assert.Throws<LearningTranslationException>(() => LearningExpressionOutputPolicy.ValidatedText(overLimit));
        Assert.Equal(LearningTranslationFailure.InvalidOutput, error.Failure);
    }
}
