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

    internal static ValueSelection? Read(AutomationElement element, Func<bool>? stillAllowed = null)
    {
        try
        {
            bool Allowed() => stillAllowed?.Invoke() ?? true;
            if (!Allowed()) return null;
            var info = element.Current;
            if (!Allowed()) return null;
            // Reject secure and read-only controls before requesting any text or selection.
            if (!info.IsEnabled || !Allowed() || info.IsPassword || !Allowed()) return null;
            string name = info.Name;
            if (!Allowed()) return null;
            string help = info.HelpText;
            if (!Allowed()) return null;
            string type = info.LocalizedControlType;
            if (!Allowed() || SecureFieldDetector.IsSecure(name, help, type)) return null;
            bool hasValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueRaw);
            if (!Allowed()) return null;
            if (hasValue && valueRaw is ValuePattern valuePattern && valuePattern.Current.IsReadOnly) return null;
            if (!Allowed()) return null;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var raw) || raw is not TextPattern pattern) return null;
            if (!Allowed()) return null;
            var selections = pattern.GetSelection();
            if (!Allowed() || selections is not { Length: 1 }) return null;
            var selection = selections[0];
            var document = pattern.DocumentRange;
            // GetText flattens embedded object boundaries. Without a native object/range anchor,
            // an equal text concatenation cannot prove a safe selection replacement.
            if (!Allowed() || document.GetChildren().Length != 0) return null;
            if (!Allowed()) return null;
            var before = document.Clone();
            if (!Allowed()) return null;
            before.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection, TextPatternRangeEndpoint.Start);
            if (!Allowed()) return null;
            var after = document.Clone();
            if (!Allowed()) return null;
            after.MoveEndpointByRange(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End);
            const int limit = ExpressionEditorReceiptPolicy.MaximumValueUtf16Units;
            if (!Allowed()) return null;
            string full = document.GetText(limit + 1);
            if (!Allowed()) return null;
            string left = before.GetText(limit + 1);
            if (!Allowed()) return null;
            string selected = selection.GetText(limit + 1);
            if (!Allowed()) return null;
            string right = after.GetText(limit + 1);
            if (!Allowed()) return null;
            if (full is null || left is null || selected is null || right is null || full.Length > limit
                || left.Length > limit || selected.Length > limit || right.Length > limit
                || left.Length + selected.Length + right.Length != full.Length
                || !string.Equals(full, string.Concat(left, selected, right), StringComparison.Ordinal)) return null;
            bool nonEmptyRange = selection.CompareEndpoints(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End) != 0;
            if (!Allowed() || nonEmptyRange != (selected.Length > 0)) return null;
            return new(full, new(left.Length, selected.Length));
        }
        catch (Exception) { return null; }
    }
}
