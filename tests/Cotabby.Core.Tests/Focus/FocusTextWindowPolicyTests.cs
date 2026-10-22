using Cotabby.Core.Focus;

namespace Cotabby.Core.Tests.Focus;

public sealed class FocusTextWindowPolicyTests
{
    [Fact]
    public void CompleteBoundarySizedTextIsNotTruncated()
    {
        string before = new('a', FocusTextWindowPolicy.MaxCharsBeforeCaret);
        string after = new('b', FocusTextWindowPolicy.MaxCharsAfterCaret);

        var window = FocusTextWindowPolicy.Bound(before, after);

        Assert.False(window.truncatedWindow);
        Assert.Equal(before, window.before);
        Assert.Equal(after, window.after);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EitherOmittedSideIsExplicitEvenForIdenticalRepeatedWindows(bool trimBefore, bool trimAfter)
    {
        string before = new('a', FocusTextWindowPolicy.MaxCharsBeforeCaret + (trimBefore ? 1 : 0));
        string after = new('a', FocusTextWindowPolicy.MaxCharsAfterCaret + (trimAfter ? 1 : 0));

        var window = FocusTextWindowPolicy.Bound(before, after);

        Assert.True(window.truncatedWindow);
        Assert.Equal(new string('a', FocusTextWindowPolicy.MaxCharsBeforeCaret), window.before);
        Assert.Equal(new string('a', FocusTextWindowPolicy.MaxCharsAfterCaret), window.after);
    }

    [Fact]
    public void WindowCutsDoNotLeaveSurrogateHalves()
    {
        string before = "😀" + new string('a', FocusTextWindowPolicy.MaxCharsBeforeCaret - 1);
        string after = new string('b', FocusTextWindowPolicy.MaxCharsAfterCaret - 1) + "😀";

        var window = FocusTextWindowPolicy.Bound(before, after);

        Assert.True(window.truncatedWindow);
        Assert.Equal(new string('a', FocusTextWindowPolicy.MaxCharsBeforeCaret - 1), window.before);
        Assert.Equal(new string('b', FocusTextWindowPolicy.MaxCharsAfterCaret - 1), window.after);
    }
}
