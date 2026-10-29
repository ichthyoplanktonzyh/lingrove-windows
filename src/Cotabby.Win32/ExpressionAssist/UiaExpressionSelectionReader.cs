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

    internal static ValueSelection? Read(AutomationElement element)
    {
        try
        {
            var info = element.Current;
            // Reject secure and read-only controls before requesting any text or selection.
            if (!info.IsEnabled || info.IsPassword || SecureFieldDetector.IsSecure(info.Name, info.HelpText, info.LocalizedControlType)) return null;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueRaw)
                && valueRaw is ValuePattern valuePattern && valuePattern.Current.IsReadOnly) return null;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var raw) || raw is not TextPattern pattern) return null;
            var selections = pattern.GetSelection();
            if (selections is not { Length: 1 }) return null;
            var selection = selections[0];
            var document = pattern.DocumentRange;
            var before = document.Clone();
            before.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection, TextPatternRangeEndpoint.Start);
            var after = document.Clone();
            after.MoveEndpointByRange(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End);
            const int limit = ExpressionEditorReceiptPolicy.MaximumValueUtf16Units;
            string full = document.GetText(limit + 1);
            string left = before.GetText(limit + 1);
            string selected = selection.GetText(limit + 1);
            string right = after.GetText(limit + 1);
            if (full is null || left is null || selected is null || right is null || full.Length > limit
                || left.Length > limit || selected.Length > limit || right.Length > limit
                || left.Length + selected.Length + right.Length != full.Length
                || !string.Equals(full, string.Concat(left, selected, right), StringComparison.Ordinal)) return null;
            bool nonEmptyRange = selection.CompareEndpoints(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End) != 0;
            if (nonEmptyRange != (selected.Length > 0)) return null;
            return new(full, new(left.Length, selected.Length));
        }
        catch (Exception) { return null; }
    }
}
