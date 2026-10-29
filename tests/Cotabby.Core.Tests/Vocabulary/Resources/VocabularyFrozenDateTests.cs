using R = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class SourceDateTests
{
    [Fact]
    public void ConsecutiveWindowsTicksConvertWithoutRejectingOrdinaryTime()
    {
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 100; i++)
        {
            var value = R.SourceDate.FromWindows(now.AddTicks(i));
            Assert.True(double.IsFinite(value.ReferenceSeconds));
            Assert.True(value.TryToWindows(out var converted, out _));
            Assert.InRange(Math.Abs((converted - now.AddTicks(i)).Ticks), 0, 1);
        }
    }
    [Fact]
    public void SourceDoubleRetainsSubTickAndCanBeOutsideWindowsRange()
    {
        var subTick = new R.SourceDate(0.00000001);
        Assert.Equal(BitConverter.DoubleToInt64Bits(0.00000001), BitConverter.DoubleToInt64Bits(subTick.ReferenceSeconds));
        Assert.True(subTick.TryToWindows(out var rounded, out var exact));
        Assert.False(exact); Assert.Equal(new DateTimeOffset(2001,1,1,0,0,0,TimeSpan.Zero), rounded);
        var far = new R.SourceDate(1e100);
        Assert.False(far.TryToWindows(out _, out _)); Assert.Equal(1e100, far.ReferenceSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => new R.SourceDate(double.PositiveInfinity));
    }
}
