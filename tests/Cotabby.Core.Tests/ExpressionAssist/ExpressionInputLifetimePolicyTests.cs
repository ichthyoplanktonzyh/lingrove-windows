using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Input;

namespace Cotabby.Core.Tests.ExpressionAssist;

public sealed class ExpressionInputLifetimePolicyTests
{
    private static KeyboardEvent Key(int key, bool down) => new() { Kind = KeyKind.Other, Character = '\0', IsKeyDown = down, VirtualKey = key };

    [Fact]
    public void OnlyExactInitiatingReleaseDuringCaptureHasSingleAllowance()
    {
        Assert.Equal(ExpressionInputLifetimePolicy.Decision.ConsumeInitialKeyUpAllowance,
            ExpressionInputLifetimePolicy.Classify(Key(65, false), true, true, 65, false));
        Assert.Equal(ExpressionInputLifetimePolicy.Decision.Retire, ExpressionInputLifetimePolicy.Classify(Key(65, false), true, false, 65, false));
        Assert.Equal(ExpressionInputLifetimePolicy.Decision.Retire, ExpressionInputLifetimePolicy.Classify(Key(66, false), true, true, 65, false));
        Assert.Equal(ExpressionInputLifetimePolicy.Decision.Retire, ExpressionInputLifetimePolicy.Classify(Key(65, true), true, true, 65, false));
        Assert.Equal(ExpressionInputLifetimePolicy.Decision.Retire, ExpressionInputLifetimePolicy.Classify(Key(65, false), false, true, 65, false));
        Assert.Equal(ExpressionInputLifetimePolicy.Decision.Keep, ExpressionInputLifetimePolicy.Classify(Key(65, true), false, false, null, true));
    }
}
