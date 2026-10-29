using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;

namespace Cotabby.Core.Tests.ExpressionAssist;

public sealed class ExpressionEditorRoundTripTests
{
    private static ExpressionEditorObservation Field(long focus = 1, long version = 1, long composition = 1) =>
        new(42, "field", focus, version, "draft??", new(7, 0), false, true, InputCompositionState.Clear, composition);
    private static ExpressionEditorRoundTrip Trip() => new(ExpressionEditorReceiptPolicy.Capture(Field(), "??")!, 10, 20, 84);

    [Fact]
    public void ExplicitReturnRequiresTwoFreshReadsAndOneShotConsumption()
    {
        var trip = Trip();
        Assert.True(trip.EnterPanel(20, 84, true));
        Assert.True(trip.ObservePanelFocus(20, 84, true));
        Assert.True(trip.BeginReturn(20, 84, true));
        Assert.False(trip.ObserveReturnedHost(10, Field(2, 2, 2)));
        Assert.True(trip.ObserveReturnedHost(10, Field(2, 3, 2)));
        Assert.NotNull(trip.Consume(10, Field(2, 4, 2)));
        Assert.Null(trip.Consume(10, Field(2, 5, 2)));
    }

    [Fact]
    public void ThirdWindowOrLostPanelOwnershipPermanentlyRetires()
    {
        var trip = Trip();
        Assert.True(trip.EnterPanel(20, 84, true));
        Assert.False(trip.ObservePanelFocus(30, 84, true));
        Assert.False(trip.BeginReturn(20, 84, true));
        var second = Trip();
        Assert.False(second.EnterPanel(20, 84, false));
        Assert.False(second.EnterPanel(20, 84, true));
    }

    [Fact]
    public void SameOldFocusEpochOrChangedEvidenceFailsClosed()
    {
        var trip = Trip();
        Assert.True(trip.EnterPanel(20, 84, true));
        Assert.True(trip.BeginReturn(20, 84, true));
        Assert.False(trip.ObserveReturnedHost(10, Field(1, 2, 2)));
        Assert.False(trip.ObserveReturnedHost(10, Field(2, 3, 2)));
        var second = Trip();
        Assert.True(second.EnterPanel(20, 84, true));
        Assert.True(second.BeginReturn(20, 84, true));
        Assert.False(second.ObserveReturnedHost(10, Field(2, 2, 2)));
        Assert.False(second.ObserveReturnedHost(10, Field(2, 3, 3)));
        Assert.Null(second.Consume(10, Field(2, 4, 3)));
    }

    [Fact]
    public void DifferentCaretValueOrCompositionCannotDispatch()
    {
        var trip = Trip();
        Assert.True(trip.EnterPanel(20, 84, true));
        Assert.True(trip.BeginReturn(20, 84, true));
        Assert.False(trip.ObserveReturnedHost(10, Field(2, 2, 2)));
        Assert.True(trip.ObserveReturnedHost(10, Field(2, 3, 2)));
        Assert.Null(trip.Consume(10, Field(2, 4, 2) with { Selection = new(6, 0) }));
        Assert.Null(trip.Consume(10, Field(2, 5, 2)));
    }
}
