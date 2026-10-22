using Cotabby.Core.Support.Runtime;

namespace Cotabby.Core.Tests.Support.Runtime;

/// <summary>Ports the Mac residency table without opening a runtime or reading live power state.</summary>
public sealed class LocalRuntimeResidencyPolicyTests
{
    [Theory]
    [InlineData(false, true, false, true, LocalRuntimeResidency.Released)]
    [InlineData(true, false, false, true, LocalRuntimeResidency.Released)]
    [InlineData(true, true, true, true, LocalRuntimeResidency.Released)]
    [InlineData(true, true, false, true, LocalRuntimeResidency.Warm)]
    [InlineData(true, true, false, false, LocalRuntimeResidency.OnDemand)]
    [InlineData(false, true, false, false, LocalRuntimeResidency.Released)]
    [InlineData(true, false, false, false, LocalRuntimeResidency.Released)]
    [InlineData(true, true, true, false, LocalRuntimeResidency.Released)]
    public void TaskEligibilityAndBackendDetermineResidency(
        bool local, bool enabled, bool paused, bool warm, LocalRuntimeResidency expected) =>
        Assert.Equal(expected, LocalRuntimeResidencyPolicy.Residency(local, enabled, paused, warm));
}
