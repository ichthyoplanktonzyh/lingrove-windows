using Cotabby.Core.ExpressionAssist;
using static Cotabby.Core.ExpressionAssist.ExpressionFocusTransitionPolicy;

namespace Cotabby.Core.Tests.ExpressionAssist;

public sealed class ExpressionFocusTransitionPolicyTests
{
    [Fact]
    public void OnlyOnePanelAndOneExplicitReturnTransitionAreAllowed()
    {
        Assert.Equal(Decision.PanelTransition, Classify(Phase.Captured, 20, 10, 20, false, false, false));
        Assert.Equal(Decision.Retire, Classify(Phase.Panel, 20, 10, 20, true, false, true));
        Assert.Equal(Decision.Retire, Classify(Phase.Panel, 30, 10, 20, false, false, true));
        Assert.Equal(Decision.Retire, Classify(Phase.Panel, 20, 10, 20, false, false, false));
        Assert.Equal(Decision.HostTransition, Classify(Phase.Returning, 10, 10, 20, true, false, false));
        Assert.Equal(Decision.Retire, Classify(Phase.Returning, 10, 10, 20, true, true, false));
        Assert.Equal(Decision.Retire, Classify(Phase.Dispatching, 10, 10, 20, true, true, false));
        Assert.Equal(Decision.Retire, Classify(Phase.Capturing, 10, 10, 20, false, false, false));
    }
}
