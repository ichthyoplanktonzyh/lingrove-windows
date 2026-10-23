using Cotabby.Core.Context;

namespace Cotabby.Core.Tests.Context;

public sealed class ClipboardTextCopyPolicyTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("before\0after")]
    public void UnrepresentablePayloadIsRejectedWithoutTruncation(string? text) =>
        Assert.False(ClipboardTextCopyPolicy.IsValid(text));

    [Fact]
    public void UnicodeAndWhitespaceRemainLiteralButUnpairedSurrogatesAreRejected()
    {
        Assert.True(ClipboardTextCopyPolicy.IsValid("  cafe\u0301 👩🏽‍💻\r\nالعربية\n"));
        Assert.True(ClipboardTextCopyPolicy.IsValid("\n\t "));
        Assert.False(ClipboardTextCopyPolicy.IsValid("a" + (char)0xd800));
        Assert.False(ClipboardTextCopyPolicy.IsValid(((char)0xdc00) + "a"));
        Assert.False(ClipboardTextCopyPolicy.IsValid(((char)0xd800) + "a"));
    }

    [Fact]
    public void SizeBoundRejectsTheWholeOversizedCopy()
    {
        Assert.True(ClipboardTextCopyPolicy.IsValid(new string('字', ClipboardTextCopyPolicy.MaximumUtf16Length)));
        Assert.False(ClipboardTextCopyPolicy.IsValid(new string('字', ClipboardTextCopyPolicy.MaximumUtf16Length + 1)));
    }
}
