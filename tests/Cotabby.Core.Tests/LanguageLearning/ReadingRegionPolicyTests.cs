using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class ReadingRegionPolicyTests
{
    [Fact]
    public void RegionNeedsOneVerifiedFieldAndDisplayWithoutTruncation()
    {
        var field = new ScreenRect(-100, 10, 200, 100); var monitor = new ScreenRect(-200, 0, 200, 200);
        Assert.True(ReadingRegionPolicy.Contains(field, monitor, new(-90, 20, 50, 20)));
        Assert.False(ReadingRegionPolicy.Contains(field, monitor, new(-90, 20, 100, 20)));
        Assert.False(ReadingRegionPolicy.Contains(field, monitor, new(-110, 20, 20, 20)));
        Assert.False(ReadingRegionPolicy.Contains(field, monitor, new(-90, 20, 3, 20)));
        Assert.False(ReadingRegionPolicy.Contains(field, monitor, new(-90.5, 20, 20, 20)));
        Assert.False(ReadingRegionPolicy.Contains(field, monitor, new(double.NaN, 20, 20, 20)));
        Assert.False(ReadingRegionPolicy.Contains(new(0, 0, 5000, 5000), new(0, 0, 5000, 5000), new(0, 0, 5000, 5000)));
    }
}
