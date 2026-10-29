using Cotabby.Core.Input;

namespace Cotabby.Core.ExpressionAssist;

/// <summary>Frozen shortcut admission. The app resolves process-specific reservations before
/// calling this pure rule; no shortcut decision reads an editor or starts capture.</summary>
public static class ExpressionAssistShortcutPolicy
{
    public static string? Conflict(KeyBinding candidate, IEnumerable<(KeyBinding Binding, string Name)> reservations)
    {
        if (!candidate.IsBound) return null;
        if (candidate.Modifiers == KeyModifiers.None || candidate.VirtualKey is 0x0D or 0x1B)
            return "Use a shortcut with a modifier key; Enter and Escape are reserved.";
        foreach (var (binding, name) in reservations)
            if (binding.IsBound && binding.VirtualKey == candidate.VirtualKey && binding.Modifiers == candidate.Modifiers)
                return name;
        return null;
    }
}
