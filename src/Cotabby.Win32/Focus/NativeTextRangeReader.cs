using System.Runtime.InteropServices;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;
using Windows.Win32;
using Windows.Win32.System.Variant;

namespace Cotabby.Win32.Focus;

// Called only by the existing UIA MTA owner. All COM references and BSTRs stay on that owner.
internal static unsafe class NativeTextRangeReader
{
    internal readonly record struct ValueSelection(string Value, int Start, int Length);
    internal readonly record struct Window(string Text, int Caret, bool HasSelection, bool ReliableSelection, bool Truncated,
        int SelectionLength = 0, bool SupportsRangeReplacement = false);

    // Read the prefix with an explicit limit and reject it if it cannot establish the caret seam.
    internal static Window? ReadWindow(IUIAutomationElement* element, Func<bool> allowed)
    {
        IUnknown* raw = null;
        IUIAutomationTextPattern* pattern = null;
        IUIAutomationTextRangeArray* selections = null;
        IUIAutomationTextRange* selected = null;
        IUIAutomationTextRange* document = null;
        IUIAutomationTextRange* before = null;
        IUIAutomationTextRange* after = null;
        try
        {
            BOOL password = default;
            if (!allowed() || element->get_CurrentIsPassword(&password).Failed || password || !allowed()) return null;
            if (element->GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId, &raw).Failed || raw is null || !allowed()) return null;
            Guid iid = IUIAutomationTextPattern.IID_Guid;
            if (raw->QueryInterface(&iid, (void**)&pattern).Failed || pattern is null || !allowed()) return null;
            if (pattern->GetSelection(&selections).Failed || selections is null || !allowed()) return null;
            int count = 0;
            if (selections->get_Length(&count).Failed || count < 1 || !allowed()) return null;
            if (selections->GetElement(0, &selected).Failed || selected is null || !allowed()) return null;
            if (pattern->get_DocumentRange(&document).Failed || document is null || !allowed()) return null;
            if (document->Clone(&before).Failed || before is null || !allowed()) return null;
            if (before->MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, selected,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start).Failed || !allowed()) return null;
            string? left = Text(before, ExpressionEditorReceiptPolicy.MaximumValueUtf16Units + 1, allowed);
            if (left is null || left.Length > ExpressionEditorReceiptPolicy.MaximumValueUtf16Units || !allowed()) return null;
            if (document->Clone(&after).Failed || after is null || !allowed()) return null;
            if (after->MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, selected,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_End).Failed || !allowed()) return null;
            string? right = Text(after, FocusTextWindowPolicy.MaxCharsAfterCaret + 1, allowed);
            if (right is null || !allowed()) return null;
            int selectedComparison = 0;
            if (selected->CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, selected,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, &selectedComparison).Failed || !allowed()) return null;
            string? middle = selectedComparison == 0 ? "" : Text(selected, ExpressionEditorReceiptPolicy.MaximumValueUtf16Units + 1, allowed);
            if (middle is null || middle.Length > ExpressionEditorReceiptPolicy.MaximumValueUtf16Units || !allowed()) return null;
            var (prefix, suffix, truncated) = FocusTextWindowPolicy.Bound(left, right);
            return new(prefix + middle + suffix, prefix.Length, selectedComparison != 0, count == 1, truncated,
                middle.Length, count == 1 && !truncated && IsWritable(document, allowed));
        }
        finally
        {
            if (after is not null) after->Release();
            if (before is not null) before->Release();
            if (document is not null) document->Release();
            if (selected is not null) selected->Release();
            if (selections is not null) selections->Release();
            if (pattern is not null) pattern->Release();
            if (raw is not null) raw->Release();
        }
    }

    internal static ValueSelection? Read(IUIAutomationElement* element, Func<bool> allowed)
    {
        IUnknown* raw = null;
        IUIAutomationTextPattern* pattern = null;
        IUIAutomationTextRangeArray* selections = null;
        IUIAutomationTextRange* selected = null;
        IUIAutomationTextRange* document = null;
        IUIAutomationTextRange* before = null;
        IUIAutomationTextRange* after = null;
        IUIAutomationElementArray* children = null;
        try
        {
            BOOL password = default;
            if (!allowed() || element->get_CurrentIsPassword(&password).Failed || password || !allowed()) return null;
            if (element->GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId, &raw).Failed || raw is null || !allowed()) return null;
            Guid iid = IUIAutomationTextPattern.IID_Guid;
            if (raw->QueryInterface(&iid, (void**)&pattern).Failed || pattern is null || !allowed()) return null;
            if (pattern->GetSelection(&selections).Failed || selections is null || !allowed()) return null;
            int count = 0;
            if (selections->get_Length(&count).Failed || count != 1 || !allowed()) return null;
            if (selections->GetElement(0, &selected).Failed || selected is null || !allowed()) return null;
            if (pattern->get_DocumentRange(&document).Failed || document is null || !allowed()) return null;
            if (document->GetChildren(&children).Failed || children is null || !allowed()) return null;
            int childCount = 0;
            if (children->get_Length(&childCount).Failed || childCount != 0 || !allowed()) return null;
            if (document->Clone(&before).Failed || before is null || !allowed()) return null;
            if (before->MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, selected,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start).Failed || !allowed()) return null;
            if (document->Clone(&after).Failed || after is null || !allowed()) return null;
            if (after->MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, selected,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_End).Failed || !allowed()) return null;
            int limit = ExpressionEditorReceiptPolicy.MaximumValueUtf16Units;
            string? full = Text(document, limit + 1, allowed);
            if (full is null || full.Length > limit || !allowed()) return null;
            string? left = Text(before, limit + 1, allowed);
            string? middle = Text(selected, limit + 1, allowed);
            string? right = Text(after, limit + 1, allowed);
            if (!allowed() || full is null || left is null || middle is null || right is null
                || full.Length > limit || left.Length + middle.Length + right.Length != full.Length
                || !string.Equals(full, string.Concat(left, middle, right), StringComparison.Ordinal)) return null;
            int comparison = 0;
            if (!allowed() || selected->CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                selected, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, &comparison).Failed || !allowed()
                || (comparison != 0) != (middle.Length != 0)) return null;
            return new(full, left.Length, middle.Length);
        }
        finally
        {
            if (children is not null) children->Release();
            if (after is not null) after->Release();
            if (before is not null) before->Release();
            if (document is not null) document->Release();
            if (selected is not null) selected->Release();
            if (selections is not null) selections->Release();
            if (pattern is not null) pattern->Release();
            if (raw is not null) raw->Release();
        }
    }

    internal static bool IsWritable(IUIAutomationTextRange* range, Func<bool> allowed)
    {
        VARIANT value = default;
        try
        {
            return allowed() && range->GetAttributeValue(UIA_TEXTATTRIBUTE_ID.UIA_IsReadOnlyAttributeId, &value).Succeeded
                && allowed() && value.Anonymous.Anonymous.vt == VARENUM.VT_BOOL
                && value.Anonymous.Anonymous.Anonymous.boolVal == 0;
        }
        finally { SpeechNativeMethods.VariantClear(&value); }
    }

    internal static string? Text(IUIAutomationTextRange* range, int max, Func<bool> allowed)
    {
        BSTR value = default;
        try
        {
            if (!allowed() || range->GetText(max, &value).Failed || !allowed() || value.Value is null || value.Length > max) return null;
            return value.ToString();
        }
        finally { if (value.Value is not null) Marshal.FreeBSTR((nint)value.Value); }
    }
}
