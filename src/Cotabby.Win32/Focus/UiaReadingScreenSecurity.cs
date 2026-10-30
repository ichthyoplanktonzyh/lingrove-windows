using Cotabby.Win32.Reading;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.Variant;

namespace Cotabby.Win32.Focus;

internal sealed record ReadingScreenSecurity(int ProcessId, string RuntimeId);

// Explicit screenshot permission is independent of TextPattern/caret support. Read only password
// metadata and focused identity on the existing MTA; never request a field's value or selected text.
internal sealed unsafe partial class UiaTextEditEvidence
{
    internal ReadingScreenSecurity? ReadScreenSecurity(int originProcess, nint originWindow, Func<bool> permit, System.Drawing.Point? pointer = null)
    {
        int live = 1; ReadingScreenSecurity? result = null;
        bool Allowed() => Volatile.Read(ref live) != 0 && permit();
        bool complete = ExecuteTextRead(() =>
        {
            IUIAutomationElement* element = null; IUIAutomationElement* current = null;
            try
            {
                if (_automation is null || !Allowed() || _automation->GetFocusedElement(&element).Failed || element is null || !Allowed()) return;
                var security = ReadScreenSecurityCore(element, originProcess, originWindow, Allowed);
                if (security is null || !Allowed() || _automation->GetFocusedElement(&current).Failed || current is null || !Allowed()) return;
                var fresh = ReadScreenSecurityCore(current, originProcess, originWindow, Allowed);
                if (fresh != security || !Allowed()) return;
                if (pointer is { } point)
                {
                    var hit = ReadPointerSecurity(point, originProcess, Allowed);
                    if (hit is null || !Allowed() || ReadPointerSecurity(point, originProcess, Allowed) != hit || !Allowed()) return;
                    fresh = fresh with { RuntimeId = fresh.RuntimeId + "/point/" + hit.RuntimeId };
                }
                if (Allowed()) result = fresh;
            }
            finally { if (current is not null) current->Release(); if (element is not null) element->Release(); }
        });
        Interlocked.Exchange(ref live, 0); return complete ? result : null;
    }
    private ReadingScreenSecurity? ReadPointerSecurity(System.Drawing.Point point, int pid, Func<bool> allowed)
    {
        IUIAutomationElement* element = null;
        try
        {
            if (!allowed() || _automation is null || _automation->ElementFromPoint(point, &element).Failed || element is null || !allowed()) return null;
            nint pointedWindow = ReadingWindowTopology.PointWindow(pid, point);
            if (pointedWindow == 0 || !allowed()) return null;
            if (!PointerWindowAdmitted(element, pointedWindow, allowed)) return null;
            var receipt = ReadScreenSecurityCore(element, pid, pointedWindow, allowed, windowAdmitted: true);
            return receipt is not null
                && ReadingNotSecurePath(element, receipt.ProcessId, allowed) && allowed()
                && ReadingWindowTopology.PointWindow(pid, point) == pointedWindow ? receipt : null;
        }
        finally { if (element is not null) element->Release(); }
    }
    private bool PointerWindowAdmitted(IUIAutomationElement* element, nint window, Func<bool> allowed)
    {
        IUIAutomationTreeWalker* walker = null; IUIAutomationElement* current = null;
        try
        {
            if (!allowed() || _automation->get_RawViewWalker(&walker).Failed || walker is null || !allowed()) return false;
            element->AddRef(); current = element;
            for (int depth = 0; depth < 7 && current is not null; depth++)
            {
                HWND handle = default;
                if (!allowed() || current->get_CurrentNativeWindowHandle(&handle).Failed || !allowed()) return false;
                if (handle.Value is not null) return ReadingWindowTopology.IsOriginDescendant(window, (nint)handle.Value) && allowed();
                IUIAutomationElement* parent = null;
                if (walker->GetParentElement(current, &parent).Failed || !allowed()) { if (parent is not null) parent->Release(); return false; }
                current->Release(); current = parent;
            }
            return false;
        }
        finally { if (current is not null) current->Release(); if (walker is not null) walker->Release(); }
    }
    private static ReadingScreenSecurity? ReadScreenSecurityCore(IUIAutomationElement* element, int originProcess, nint originWindow, Func<bool> allowed, bool windowAdmitted = false)
    {
        int pid = 0; BOOL password = default;
        if (!allowed() || element->get_CurrentProcessId(&pid).Failed || pid <= 0 || !allowed()
            || element->get_CurrentIsPassword(&password).Failed || password || !allowed()) return null;
        if (pid != originProcess && !windowAdmitted)
        {
            // Browser providers can live in a child process. A real native descendant HWND may
            // establish that relationship; missing or unrelated evidence stays unavailable.
            HWND handle = default;
            if (element->get_CurrentNativeWindowHandle(&handle).Failed || handle.Value is null || !allowed()
                || !ReadingWindowTopology.IsOriginDescendant(originWindow, (nint)handle.Value) || !allowed()) return null;
        }
        if (!ScreenCompositionAdmitted(element, allowed)) return null;
        string? id = RuntimeId(element);
        return !string.IsNullOrWhiteSpace(id) && allowed() ? new(pid, id) : null;
    }
    private static bool ScreenCompositionAdmitted(IUIAutomationElement* element, Func<bool> allowed)
    {
        if (!PatternAvailable(element, UIA_PROPERTY_ID.UIA_IsTextEditPatternAvailablePropertyId, allowed, out bool edit)) return false;
        if (edit)
        {
            IUnknown* raw = null; IUIAutomationTextEditPattern* pattern = null; IUIAutomationTextRange* composition = null;
            try
            {
                if (!allowed() || element->GetCurrentPattern(UIA_PATTERN_ID.UIA_TextEditPatternId, &raw).Failed || raw is null || !allowed()) return false;
                Guid iid = IUIAutomationTextEditPattern.IID_Guid;
                return raw->QueryInterface(&iid, (void**)&pattern).Succeeded && pattern is not null && allowed()
                    && pattern->GetActiveComposition(&composition).Succeeded && composition is null && allowed();
            }
            finally { if (composition is not null) composition->Release(); if (pattern is not null) pattern->Release(); if (raw is not null) raw->Release(); }
        }
        if (!PatternAvailable(element, UIA_PROPERTY_ID.UIA_IsValuePatternAvailablePropertyId, allowed, out bool value)
            || !PatternAvailable(element, UIA_PROPERTY_ID.UIA_IsTextPatternAvailablePropertyId, allowed, out bool text)) return false;
        // A missing TextEdit pattern never certifies editable text as committed. Positively
        // read-only text and explicit non-text control types do not contain an editable preedit.
        if (value)
        {
            IUnknown* raw = null; IUIAutomationValuePattern* pattern = null;
            try
            {
                if (!allowed() || element->GetCurrentPattern(UIA_PATTERN_ID.UIA_ValuePatternId, &raw).Failed || raw is null || !allowed()) return false;
                Guid iid = IUIAutomationValuePattern.IID_Guid; BOOL readOnly = default;
                return raw->QueryInterface(&iid, (void**)&pattern).Succeeded && pattern is not null && allowed()
                    && pattern->get_CurrentIsReadOnly(&readOnly).Succeeded && readOnly && allowed();
            }
            finally { if (pattern is not null) pattern->Release(); if (raw is not null) raw->Release(); }
        }
        if (text)
        {
            IUnknown* raw = null; IUIAutomationTextPattern* pattern = null; IUIAutomationTextRange* document = null; VARIANT readOnly = default;
            try
            {
                if (!allowed() || element->GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId, &raw).Failed || raw is null || !allowed()) return false;
                Guid iid = IUIAutomationTextPattern.IID_Guid;
                return raw->QueryInterface(&iid, (void**)&pattern).Succeeded && pattern is not null && allowed()
                    && pattern->get_DocumentRange(&document).Succeeded && document is not null && allowed()
                    && document->GetAttributeValue(UIA_TEXTATTRIBUTE_ID.UIA_IsReadOnlyAttributeId, &readOnly).Succeeded && allowed()
                    && readOnly.Anonymous.Anonymous.vt == VARENUM.VT_BOOL && readOnly.Anonymous.Anonymous.Anonymous.boolVal != 0;
            }
            finally { SpeechNativeMethods.VariantClear(&readOnly); if (document is not null) document->Release(); if (pattern is not null) pattern->Release(); if (raw is not null) raw->Release(); }
        }
        UIA_CONTROLTYPE_ID control = default;
        return allowed() && element->get_CurrentControlType(&control).Succeeded && allowed() && control is
            UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId or UIA_CONTROLTYPE_ID.UIA_CheckBoxControlTypeId
            or UIA_CONTROLTYPE_ID.UIA_RadioButtonControlTypeId or UIA_CONTROLTYPE_ID.UIA_ImageControlTypeId
            or UIA_CONTROLTYPE_ID.UIA_TextControlTypeId or UIA_CONTROLTYPE_ID.UIA_HyperlinkControlTypeId
            or UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId or UIA_CONTROLTYPE_ID.UIA_TabItemControlTypeId;
    }
    private static bool PatternAvailable(IUIAutomationElement* element, UIA_PROPERTY_ID property, Func<bool> allowed, out bool supported)
    {
        supported = false; VARIANT value = default;
        try
        {
            if (!allowed() || element->GetCurrentPropertyValue(property, &value).Failed || !allowed()
                || value.Anonymous.Anonymous.vt != VARENUM.VT_BOOL) return false;
            supported = value.Anonymous.Anonymous.Anonymous.boolVal != 0; return true;
        }
        finally { SpeechNativeMethods.VariantClear(&value); }
    }
    internal static ReadingScreenSecurity? ReadOwnedScreenSecurityForTest(nint ownedElement, Func<bool> allowed)
    {
        var element = (IUIAutomationElement*)ownedElement; int pid = 0;
        if (element is null || !allowed() || element->get_CurrentProcessId(&pid).Failed || pid != Environment.ProcessId) return null;
        return ReadScreenSecurityCore(element, pid, 0, allowed);
    }
}
