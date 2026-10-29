using Cotabby.Core.LanguageLearning.MDict;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class MDictCompressionTests
{
    [Fact]
    public void SourceLzoLiteralAndInvalidBackReference()
    {
        byte[] block = [1, 0, 0, 0, 0, 0, 0, 0, 22, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 17, 0, 0];
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(MDictCompression.Expand(block, 5)));
        Assert.Throws<InvalidDataException>(() => MDictCompression.Expand([1, 0, 0, 0, 0, 0, 0, 0, 17, 1, 0], 8));
        Assert.Throws<InvalidDataException>(() => MDictCompression.Expand([2, 0, 0, 0, 0, 0, 0, 0], 0));
        Assert.Throws<InvalidDataException>(() => MDictCompression.Expand(new byte[8], int.MaxValue));
    }
    [Theory]
    [InlineData("", "CDF26213A150DC3ECB610F18F6B38B46")]
    [InlineData("abc", "C14A12199C66E4BA84636B0F69144C77")]
    public void Ripemd128MatchesPublishedVectors(string input, string expected) =>
        Assert.Equal(expected, Convert.ToHexString(MDictRipemd128.Hash(System.Text.Encoding.ASCII.GetBytes(input))));
}
