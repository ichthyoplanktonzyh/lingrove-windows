using Cotabby.Core.LanguageLearning.Reading;
namespace Cotabby.Core.Tests.LanguageLearning;
public sealed class ReadingOcrTextPolicyTests
{
    private static ReadingOcrLine Line(string text, double x, double y, double width = 20) => new(text, [new(text, new(x, y, width, 10))]);
    [Fact] public void ColumnsKeepEveryLineAndOrderByReadingColumn()
    {
        var lines = new[] { Line("right 1", 70, 10), Line("left 2", 5, 30), Line("left 1", 5, 10), Line("right 2", 70, 30) };
        Assert.Equal("left 1\nleft 2\nright 1\nright 2", ReadingOcrTextPolicy.Text(lines, 100, 100));
    }
    [Fact] public void MissingGeometryPreservesProviderOrderAndInvalidBoundsFailClosed()
    {
        Assert.Equal("second\nfirst", ReadingOcrTextPolicy.Text([new("second", []), Line("first", 5, 10)], 100, 100));
        Assert.Throws<ArgumentException>(() => ReadingOcrTextPolicy.Text([Line("bad", 95, 0)], 100, 100));
    }
}
