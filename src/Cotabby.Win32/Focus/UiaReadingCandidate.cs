using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Accessibility;

namespace Cotabby.Win32.Focus;

/// <summary>Mac 3b selection roots/ancestor search adapted to the retained UIA MTA. A target
/// may be a readonly document above a focused inline fragment. Keep that original focused
/// element separately; no page walk, clipboard fallback or writable receipt is introduced.</summary>
internal sealed unsafe partial class UiaTextEditEvidence
{
    private enum ReadingCandidate { Unavailable, Rejected, Ready }
    private bool ReadingCandidateBelongs(IUIAutomationElement* element, nint host, int originProcess, Func<bool> allowed, out int actual)
    {
        actual = 0; int pid = 0;
        if (!allowed() || element is null || Cotabby.Win32.Interop.NativeMethods.GetWindowThreadProcessId(host, out uint owner) == 0
            || owner != (uint)originProcess || !allowed() || element->get_CurrentProcessId(&pid).Failed || pid <= 0 || !allowed()
            || !PointerWindowAdmitted(element, host, allowed) || !allowed()) return false;
        // UIA browser providers may run in a child process. Positive native ancestry binds the
        // provider to this exact original window; its own PID drives the sensitive-path checks.
        actual = pid; return originProcess > 0 && allowed();
    }
    private bool ReadingRootAllowed(IUIAutomationElement* root, nint host, int pid, Func<bool> allowed) =>
        ReadingCandidateBelongs(root, host, pid, allowed, out int actual) && ReadingNotSecurePath(root, actual, allowed)
        && ScreenCompositionAdmitted(root, allowed) && allowed();
    private ReadingCandidate FindReadingCandidate(ReadingLease lease, IUIAutomationElement* root, bool ancestors, Func<bool> allowed)
    {
        IUIAutomationTreeWalker* walker = null; IUIAutomationElement* current = null;
        bool Permit() => allowed();
        try
        {
            if (!Permit() || _automation->get_RawViewWalker(&walker).Failed || walker is null || !Permit()) return ReadingCandidate.Rejected;
            root->AddRef(); current = root;
            for (int depth = 0; depth < (ancestors ? 7 : 1) && current is not null && Permit(); depth++)
            {
                if (!ReadingCandidateBelongs(current, lease.Host, lease.ProcessId, Permit, out int actual)) return ReadingCandidate.Unavailable;
                if (!ReadingNotSecurePath(current, actual, Permit)) return ReadingCandidate.Rejected;
                current->AddRef(); lease.Element = current; lease.ProviderProcessId = actual;
                lease.FieldId = RuntimeId(current) ?? "";
                var result = string.IsNullOrWhiteSpace(lease.FieldId) ? ReadingCandidate.Rejected : ProbeReadingCandidate(lease, Permit);
                if (result == ReadingCandidate.Ready) return result;
                ReleaseUnchosenReadingCandidate(lease);
                if (result == ReadingCandidate.Rejected || !Permit()) return ReadingCandidate.Rejected;
                IUIAutomationElement* parent = null;
                if (walker->GetParentElement(current, &parent).Failed || !Permit())
                { if (parent is not null) parent->Release(); return ReadingCandidate.Rejected; }
                current->Release(); current = parent;
            }
            return Permit() ? ReadingCandidate.Unavailable : ReadingCandidate.Rejected;
        }
        finally { if (current is not null) current->Release(); if (walker is not null) walker->Release(); }
    }
    private static ReadingCandidate ProbeReadingCandidate(ReadingLease lease, Func<bool> allowed)
    {
        if (!PatternAvailable(lease.Element, UIA_PROPERTY_ID.UIA_IsTextPatternAvailablePropertyId, allowed, out bool text)
            || !PatternAvailable(lease.Element, UIA_PROPERTY_ID.UIA_IsTextEditPatternAvailablePropertyId, allowed, out bool edit)) return ReadingCandidate.Rejected;
        if (!text) return ReadingCandidate.Unavailable;
        Guid textId = IUIAutomationTextPattern.IID_Guid; IUIAutomationTextPattern* pattern = null;
        var textResult = lease.Element->GetCurrentPatternAs(UIA_PATTERN_ID.UIA_TextPatternId, &textId, (void**)&pattern); lease.TextPattern = pattern;
        if (textResult.Failed || pattern is null || !allowed()) return ReadingCandidate.Rejected;
        if (edit)
        {
            Guid editId = IUIAutomationTextEditPattern.IID_Guid; IUIAutomationTextEditPattern* editing = null;
            var editResult = lease.Element->GetCurrentPatternAs(UIA_PATTERN_ID.UIA_TextEditPatternId, &editId, (void**)&editing); lease.Edit = editing;
            if (editResult.Failed || editing is null || !allowed()) return ReadingCandidate.Rejected;
        }
        else lease.ReadOnly = true;
        if (!ReadingClear(lease, allowed) || lease.RequireReadOnlyTarget && !ReadingReadOnlyRanges(lease, allowed)) return ReadingCandidate.Rejected;
        if (lease.ScreenGuard) return lease.Edit is null ? ReadingCandidate.Unavailable : ReadingCandidate.Ready;
        IUIAutomationTextRange* selected = null;
        try
        {
            selected = CurrentReadingRange(lease, allowed);
            if (selected is null) return allowed() ? ReadingCandidate.Unavailable : ReadingCandidate.Rejected;
            IUIAutomationTextRange* clone = null;
            var cloned = selected->Clone(&clone); lease.Range = clone;
            if (cloned.Failed || clone is null || !allowed()) return ReadingCandidate.Rejected;
            if (!ReadingClear(lease, allowed)) return ReadingCandidate.Rejected;
            if ((lease.ReadOnly || lease.RequireReadOnlyTarget) && !RangeReadOnly(selected, allowed)) return ReadingCandidate.Rejected;
            string? value = ReadingText(selected, allowed);
            if (value is null || !allowed()) return ReadingCandidate.Rejected;
            if (value.Length == 0) return ReadingCandidate.Unavailable;
            lease.Text = value;
            return ReadingClear(lease, allowed) ? ReadingCandidate.Ready : ReadingCandidate.Rejected;
        }
        finally { if (selected is not null) selected->Release(); }
    }
    private static void ReleaseUnchosenReadingCandidate(ReadingLease lease)
    {
        if (lease.Range is not null) { lease.Range->Release(); lease.Range = null; }
        if (lease.TextPattern is not null) { lease.TextPattern->Release(); lease.TextPattern = null; }
        if (lease.Edit is not null) { lease.Edit->Release(); lease.Edit = null; }
        if (lease.Element is not null) { lease.Element->Release(); lease.Element = null; }
        lease.ReadOnly = false; lease.Text = ""; lease.FieldId = ""; lease.ProviderProcessId = 0;
    }
    private static bool ReadingReadOnly(ReadingLease lease, Func<bool> allowed)
    {
        if (!allowed() || !PatternAvailable(lease.Element, UIA_PROPERTY_ID.UIA_IsTextEditPatternAvailablePropertyId, allowed, out bool edit) || edit
            || !ScreenCompositionAdmitted(lease.Element, allowed) || lease.TextPattern is null) return false;
        return ReadingReadOnlyRanges(lease, allowed);
    }
    private static bool ReadingReadOnlyRanges(ReadingLease lease, Func<bool> allowed)
    {
        if (lease.TextPattern is null) return false;
        IUIAutomationTextRange* document = null;
        try
        {
            return allowed() && lease.TextPattern->get_DocumentRange(&document).Succeeded && document is not null && allowed()
                && RangeReadOnly(document, allowed) && (lease.Range is null || RangeReadOnly(lease.Range, allowed)) && allowed();
        }
        finally { if (document is not null) document->Release(); }
    }
    private static bool RangeReadOnly(IUIAutomationTextRange* range, Func<bool> allowed)
    {
        VARIANT value = default;
        try
        {
            return allowed() && range->GetAttributeValue(UIA_TEXTATTRIBUTE_ID.UIA_IsReadOnlyAttributeId, &value).Succeeded && allowed()
                && value.Anonymous.Anonymous.vt == VARENUM.VT_BOOL && value.Anonymous.Anonymous.Anonymous.boolVal != 0;
        }
        finally { SpeechNativeMethods.VariantClear(&value); }
    }
}
