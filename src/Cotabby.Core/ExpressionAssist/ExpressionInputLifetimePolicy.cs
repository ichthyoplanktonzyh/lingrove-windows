using Cotabby.Core.Input;

namespace Cotabby.Core.ExpressionAssist;

/// <summary>Classifies a borrowed global key observation without suppressing the event.</summary>
public static class ExpressionInputLifetimePolicy
{
    public enum Decision { Keep, ConsumeInitialKeyUpAllowance, Retire }

    public static Decision Classify(KeyboardEvent key, bool capturing, bool initialKeyUpAvailable,
        int? initiatingVirtualKey, bool registeredPanelOwnsKeyboard)
    {
        if (capturing && initialKeyUpAvailable && initiatingVirtualKey.HasValue
            && !key.IsKeyDown && key.VirtualKey == initiatingVirtualKey.Value) return Decision.ConsumeInitialKeyUpAllowance;
        if (registeredPanelOwnsKeyboard) return Decision.Keep;
        return Decision.Retire;
    }
}
