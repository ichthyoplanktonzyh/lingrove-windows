using System.Windows.Automation;
using System.Windows.Automation.Text;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;

namespace Cotabby.Win32.ExpressionAssist;

/// <summary>
/// Reads one complete, single UIA selection without taking focus or changing the editor. The caller
/// owns identity, foreground and composition receipts before and after this cross-process read.
/// No value is logged or retained by the reader. Unsupported/ambiguous ranges return null.
/// </summary>
internal static class UiaExpressionSelectionReader
{
    internal readonly record struct ValueSelection(string Value, ExpressionEditorRange Selection);

    internal static bool IsSafe(AutomationElement element, Func<bool>? stillAllowed = null)
    {
        try
        {
            bool Allowed() => stillAllowed?.Invoke() ?? true;
            if (!Allowed()) return false;
            var info = element.Current;
            if (!Allowed()) return false;
            // Reject secure and read-only controls before requesting any text or selection.
            if (!info.IsEnabled || !Allowed() || info.IsPassword || !Allowed()) return false;
            string name = info.Name;
            if (!Allowed()) return false;
            string help = info.HelpText;
            if (!Allowed()) return false;
            string type = info.LocalizedControlType;
            if (!Allowed() || SecureFieldDetector.IsSecure(name, help, type)) return false;
            bool hasValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueRaw);
            if (!Allowed()) return false;
            if (hasValue && valueRaw is ValuePattern valuePattern && valuePattern.Current.IsReadOnly) return false;
            return Allowed();
        }
        catch (Exception) { return false; }
    }
}
