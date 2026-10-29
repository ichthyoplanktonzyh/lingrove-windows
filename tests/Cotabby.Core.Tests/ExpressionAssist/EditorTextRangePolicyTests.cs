using Cotabby.Core.Text;

namespace Cotabby.Core.Tests.ExpressionAssist;

public sealed class EditorTextRangePolicyTests
{
    [Theory]
    [InlineData("Hi 😀", 5, 0, true)]
    [InlineData("Hi 😀", 3, 0, true)]
    [InlineData("We can  do it", 7, 0, true)]
    [InlineData("e\u0301", 1, 0, false)]
    [InlineData("e\u0301", 0, 1, false)]
    [InlineData("e\u0301", 0, 2, true)]
    [InlineData("??", 0, 2, true)]
    [InlineData("😀", 1, 1, false)]
    [InlineData("👩‍💻", 0, 2, false)]
    [InlineData("👩‍💻", 2, 0, false)]
    [InlineData("क्‍ष", 1, 0, false)]
    [InlineData("\r\n", 1, 0, false)]
    [InlineData("\r\n", 0, 1, false)]
    [InlineData("\r\n", 0, 2, true)]
    public void SourceRangeAdmissionKeepsWholeCharacters(string value, int location, int length, bool expected) =>
        Assert.Equal(expected, EditorTextRangePolicy.IsValid(value, location, length));
}
