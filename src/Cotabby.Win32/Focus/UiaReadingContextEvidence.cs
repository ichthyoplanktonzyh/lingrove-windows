using System.Runtime.InteropServices;
using Cotabby.Core.LanguageLearning.Reading;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Cotabby.Win32.Focus;

// Called only on the existing retained-reading MTA. Clone the exact selected range and move only
// its endpoints; never search for the selected spelling or expose native pointers to the caller.
internal sealed unsafe partial class UiaTextEditEvidence
{
    public ReadingAcquisitionWindow? ReadReadingContext(ReadingLease lease, Func<bool> allowed)
    {
        var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(System.Diagnostics.Stopwatch.Frequency * 0.18);
        bool Permit() => Volatile.Read(ref lease.Active) != 0 && allowed() && System.Diagnostics.Stopwatch.GetTimestamp() < deadline;
        ReadingAcquisitionWindow? result = null;
        bool completed = ExecuteTextRead(() =>
        {
            if (!ReferenceEquals(_reading, lease) || !Permit() || !ReadingCurrentCore(lease, Permit)) return;
            var candidate = ReadingContextWindow(lease, Permit);
            if (candidate is not null && Permit() && ReadingCurrentCore(lease, Permit) && Permit()) result = candidate;
        });
        return completed && Permit() ? result : null;
    }
    private static ReadingAcquisitionWindow? ReadingContextWindow(ReadingLease lease, Func<bool> allowed)
    {
        if (lease.Text.Length is 0 or > ReadingContextPolicy.MaximumSelection || lease.Range is null || lease.TextPattern is null) return null;
        IUIAutomationTextRange* left = null; IUIAutomationTextRange* right = null; IUIAutomationTextRange* document = null;
        try
        {
            var start = TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start;
            var end = TextPatternRangeEndpoint.TextPatternRangeEndpoint_End;
            if (!allowed() || lease.TextPattern->get_DocumentRange(&document).Failed || document is null || !allowed()) return null;
            if (lease.Range->Clone(&left).Failed || left is null || !allowed() || left->MoveEndpointByRange(end, lease.Range, start).Failed || !allowed()) return null;
            int moved = 0;
            if (left->MoveEndpointByUnit(start, TextUnit.TextUnit_Character, -ReadingContextPolicy.Surrounding, &moved).Failed || !allowed()) return null;
            string? before = ReadingContextText(left, allowed); if (before is null || !allowed()) return null;
            if (lease.Range->Clone(&right).Failed || right is null || !allowed() || right->MoveEndpointByRange(start, lease.Range, end).Failed || !allowed()) return null;
            int afterLimit = ReadingContextPolicy.MaximumSurrounding - before.Length;
            if (afterLimit < 0 || right->MoveEndpointByUnit(end, TextUnit.TextUnit_Character, afterLimit, &moved).Failed || !allowed()) return null;
            string? after = ReadingContextText(right, allowed); if (after is null || before.Length + after.Length > ReadingContextPolicy.MaximumSurrounding || !allowed()) return null;
            int extra = ReadingContextPolicy.MaximumSurrounding - before.Length - after.Length;
            if (extra > 0)
            {
                if (left->MoveEndpointByUnit(start, TextUnit.TextUnit_Character, -extra, &moved).Failed || !allowed()) return null;
                before = ReadingContextText(left, allowed); if (before is null || before.Length + after.Length > ReadingContextPolicy.MaximumSurrounding || !allowed()) return null;
            }
            int compareLeft = 0, compareRight = 0;
            if (left->CompareEndpoints(start, document, start, &compareLeft).Failed || !allowed()
                || right->CompareEndpoints(end, document, end, &compareRight).Failed || !allowed()) return null;
            // Generic text/document nodes may only be browser inline fragments. A real Edit
            // control is a structural container; only it certifies its document edges.
            UIA_CONTROLTYPE_ID control = default;
            bool structural = allowed() && lease.Element->get_CurrentControlType(&control).Succeeded && allowed()
                && control == UIA_CONTROLTYPE_ID.UIA_EditControlTypeId;
            var l = compareLeft == 0 ? (structural ? ReadingBoundary.Container : ReadingBoundary.Unknown) : ReadingBoundary.Crop;
            var r = compareRight == 0 ? (structural ? ReadingBoundary.Container : ReadingBoundary.Unknown) : ReadingBoundary.Crop;
            return new(before + lease.Text + after, before.Length, lease.Text.Length, l, r, ReadingContextMethod.Characters);
        }
        finally { if (document is not null) document->Release(); if (right is not null) right->Release(); if (left is not null) left->Release(); }
    }
    private static string? ReadingContextText(IUIAutomationTextRange* range, Func<bool> allowed)
    {
        BSTR result = default;
        try
        {
            int comparison = 0;
            if (!allowed() || range->CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, range,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, &comparison).Failed || !allowed()) return null;
            if (comparison == 0) return "";
            int limit = ReadingContextPolicy.MaximumSurrounding;
            if (!allowed() || range->GetText(limit + 1, &result).Failed || !allowed() || result.Value is null || result.Length > limit) return null;
            return result.ToString();
        }
        finally { if (result.Value is not null) Marshal.FreeBSTR((nint)result.Value); }
    }
}
