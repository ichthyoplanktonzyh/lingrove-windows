using Cotabby.Core.Runtime.Metrics;

namespace Cotabby.Core.Tests;

public class CurrentPerformanceMetricTests
{
    [Fact]
    public void ExpressionAssistanceRoundTripsWithoutChangingHistoricalSelectableList()
    {
        var entry = new PerformanceMetricEntry(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "model", 3, ModelRequestSource.ExpressionAssist);
        var bytes = PerformanceMetricCodec.Encode([entry]);
        Assert.Equal(entry, Assert.Single(PerformanceMetricCodec.Decode(bytes)!));
        Assert.Contains(ModelRequestSource.ExpressionAssist, ModelRequestSources.Current);
        Assert.DoesNotContain(ModelRequestSource.ExpressionAssist, ModelRequestSources.All);
    }
}
