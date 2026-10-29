namespace Cotabby.Core.ExpressionAssist;

/// <summary>Only the registered panel transition and the explicit host return are admitted.</summary>
public static class ExpressionFocusTransitionPolicy
{
    public enum Phase { Capturing, Captured, Panel, Returning, Dispatching }
    public enum Decision { PanelTransition, HostTransition, Retire }

    public static Decision Classify(Phase phase, nint foreground, nint host, nint panel,
        bool panelTransitionSeen, bool hostTransitionSeen, bool panelOwnsKeyboard)
    {
        if (foreground == panel && !panelTransitionSeen
            && (phase == Phase.Captured || (phase == Phase.Panel && panelOwnsKeyboard))) return Decision.PanelTransition;
        if (phase == Phase.Returning && foreground == host && !hostTransitionSeen) return Decision.HostTransition;
        return Decision.Retire;
    }
}
