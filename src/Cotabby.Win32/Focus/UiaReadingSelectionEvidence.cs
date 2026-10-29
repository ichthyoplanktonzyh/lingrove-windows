using System.Runtime.InteropServices;
using System.Text;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Native = Cotabby.Win32.Interop.NativeMethods;

namespace Cotabby.Win32.Focus;

internal interface IReadingSelectionEvidence
{
    UiaTextEditEvidence.ReadingLease? CaptureReading(nint host, int pid, Func<bool> allowed, Action invalidated, bool ownedTest = false);
    UiaTextEditEvidence.ReadingLease? CapturePointer(nint host, int pid, System.Drawing.Point point, Func<bool> allowed, Action invalidated) => null;
    UiaTextEditEvidence.ReadingLease? CaptureScreenGuard(nint host, int pid, Func<bool> allowed, Action invalidated) => null;
    bool IsReadingCurrent(UiaTextEditEvidence.ReadingLease lease, Func<bool> allowed);
    ReadingAcquisitionWindow? ReadReadingContext(UiaTextEditEvidence.ReadingLease lease, Func<bool> allowed) => null;
    void RetireReading(UiaTextEditEvidence.ReadingLease lease);
    Task DrainRetainedAsync();
}

// This slot is independent of expression editing. Native references never leave the MTA owner.
internal sealed unsafe partial class UiaTextEditEvidence : IReadingSelectionEvidence
{
    internal sealed class ReadingLease
    {
        internal readonly Guid Anchor = Guid.NewGuid();
        internal readonly Action Invalidated;
        internal int Active = 1;
        internal long Version = 1;
        internal int ProcessId;
        internal string FieldId = "";
        internal nint Host;
        internal string Text = "";
        internal bool Ready;
        internal bool OwnedTest;
        internal System.Drawing.Point? Pointer;
        internal bool ScreenGuard;
        internal nint FieldWindow;
        internal int FieldLeft, FieldTop, FieldRight, FieldBottom;
        internal IUIAutomationElement* Element;
        internal IUIAutomationTextEditPattern* Edit;
        internal IUIAutomationTextPattern* TextPattern;
        internal IUIAutomationTextRange* Range;
        internal IUIAutomationTextEditTextChangedEventHandler* Composition;
        internal IUIAutomationTextEditTextChangedEventHandler* Finalized;
        internal IUIAutomationEventHandler* Changed;
        internal IUIAutomationEventHandler* Selection;
        internal bool AddedComposition, AddedFinalized, AddedChanged, AddedSelection;
        internal ReadingLease(Action invalidated) => Invalidated = invalidated;
    }

    private ReadingLease? _reading;

    public ReadingLease? CaptureReading(nint host, int pid, Func<bool> allowed, Action invalidated, bool ownedTest = false) =>
        CaptureReadingCore(host, pid, null, false, allowed, invalidated, ownedTest);
    public ReadingLease? CapturePointer(nint host, int pid, System.Drawing.Point point, Func<bool> allowed, Action invalidated) =>
        CaptureReadingCore(host, pid, point, false, allowed, invalidated, false);
    public ReadingLease? CaptureScreenGuard(nint host, int pid, Func<bool> allowed, Action invalidated) =>
        CaptureReadingCore(host, pid, null, true, allowed, invalidated, false);
    private ReadingLease? CaptureReadingCore(nint host, int pid, System.Drawing.Point? pointer, bool screenGuard, Func<bool> allowed, Action invalidated, bool ownedTest)
    {
        if (ownedTest && (Native.GetWindowThreadProcessId(host, out uint ownerPid) == 0 || ownerPid != (uint)Environment.ProcessId || pid != Environment.ProcessId)) return null;
        var lease = new ReadingLease(invalidated) { Host = host, ProcessId = pid, OwnedTest = ownedTest, Pointer = pointer, ScreenGuard = screenGuard };
        bool Allowed() => Volatile.Read(ref lease.Active) != 0 && allowed() && (ownedTest || Native.GetForegroundWindow() == host);
        bool ok = ExecuteTextRead(() =>
        {
            if (!Allowed() || _automation is null) return;
            RetireReadingCore();
            if (!Allowed()) return;
            _reading = lease;
            IUIAutomationElement* element = null;
            try
            {
                if (!Allowed()) return;
                var elementResult = ownedTest
                    ? _automation->ElementFromHandle(new HWND(host), &element)
                    : _automation->GetFocusedElement(&element);
                if (elementResult.Failed || element is null || !Allowed()) return;
                lease.Element = element; element = null;
                if (pointer is { } point)
                {
                    IUIAutomationElement* hit = null;
                    try
                    {
                        BOOL same = default;
                        if (_automation->ElementFromPoint(point, &hit).Failed || hit is null || !Allowed()
                            || _automation->CompareElements(lease.Element, hit, &same).Failed || !same || !Allowed()) return;
                    }
                    finally { if (hit is not null) hit->Release(); }
                }
                int actual = 0;
                if (lease.Element->get_CurrentProcessId(&actual).Failed || actual != pid || !Allowed()) return;
                lease.FieldId = RuntimeId(lease.Element) ?? "";
                if (string.IsNullOrEmpty(lease.FieldId) || !Allowed() || !ReadingNotSecurePath(lease.Element, pid, Allowed)) return;
                if (screenGuard)
                {
                    HWND fieldWindow = default; RECT bounds = default;
                    if (lease.Element->get_CurrentNativeWindowHandle(&fieldWindow).Failed || fieldWindow.IsNull || !Allowed()
                        || Native.GetWindowThreadProcessId((nint)fieldWindow.Value, out uint owner) == 0 || owner != (uint)pid || !Allowed()
                        || lease.Element->get_CurrentBoundingRectangle(&bounds).Failed || !Allowed()
                        || bounds.right <= bounds.left || bounds.bottom <= bounds.top) return;
                    lease.FieldWindow = (nint)fieldWindow.Value;
                    lease.FieldLeft = bounds.left; lease.FieldTop = bounds.top; lease.FieldRight = bounds.right; lease.FieldBottom = bounds.bottom;
                }
                Guid editId = IUIAutomationTextEditPattern.IID_Guid;
                IUIAutomationTextEditPattern* edit = null;
                var editResult = lease.Element->GetCurrentPatternAs(UIA_PATTERN_ID.UIA_TextEditPatternId, &editId, (void**)&edit);
                lease.Edit = edit;
                if (editResult.Failed || edit is null || !Allowed()) return;
                Guid textId = IUIAutomationTextPattern.IID_Guid;
                IUIAutomationTextPattern* textPattern = null;
                var textResult = lease.Element->GetCurrentPatternAs(UIA_PATTERN_ID.UIA_TextPatternId, &textId, (void**)&textPattern);
                lease.TextPattern = textPattern;
                if (textResult.Failed || textPattern is null || !Allowed()) return;
                Action change = () => RetireReading(lease);
                lease.Composition = Callback.CreateReading(this, change);
                lease.Finalized = Callback.CreateReading(this, change);
                lease.Changed = RetainedEventCallback.CreateReading(this, change);
                lease.Selection = RetainedEventCallback.CreateReading(this, change);
                if (!Allowed() || _automation->AddTextEditTextChangedEventHandler(lease.Element, TreeScope.TreeScope_Element,
                    TextEditChangeType.TextEditChangeType_Composition, null, lease.Composition).Failed) return;
                lease.AddedComposition = true;
                if (!Allowed() || _automation->AddTextEditTextChangedEventHandler(lease.Element, TreeScope.TreeScope_Element,
                    TextEditChangeType.TextEditChangeType_CompositionFinalized, null, lease.Finalized).Failed) return;
                lease.AddedFinalized = true;
                if (!Allowed() || _automation->AddAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextChangedEventId, lease.Element,
                    TreeScope.TreeScope_Element, null, lease.Changed).Failed) return;
                lease.AddedChanged = true;
                if (!Allowed() || _automation->AddAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextSelectionChangedEventId, lease.Element,
                    TreeScope.TreeScope_Element, null, lease.Selection).Failed) return;
                lease.AddedSelection = true;
                if (!Allowed() || !ReadingClear(lease, Allowed)) return;
                if (lease.ScreenGuard) { lease.Ready = ReadingCurrentCore(lease, Allowed); return; }
                IUIAutomationTextRange* selected = null;
                try
                {
                    selected = CurrentReadingRange(lease, Allowed);
                    if (selected is null || !Allowed()) return;
                    IUIAutomationTextRange* range = null;
                    var cloneResult = selected->Clone(&range);
                    lease.Range = range;
                    if (cloneResult.Failed || range is null || !Allowed()) return;
                    lease.Text = ReadingText(selected, Allowed) ?? "";
                    if (lease.Text.Length == 0 || !Allowed() || !ReadingCurrentCore(lease, Allowed)) return;
                    lease.Ready = true;
                }
                finally { if (selected is not null) selected->Release(); }
            }
            finally { if (element is not null) element->Release(); }
        });
        if (!ok || !lease.Ready || !Allowed()) { RetireReading(lease); return null; }
        return lease;
    }

    public bool IsReadingCurrent(ReadingLease lease, Func<bool> allowed)
    {
        bool valid = false;
        bool Allowed() => Volatile.Read(ref lease.Active) != 0 && allowed() && (lease.OwnedTest || Native.GetForegroundWindow() == lease.Host);
        bool completed = ExecuteTextRead(() => { if (ReferenceEquals(_reading, lease) && Allowed()) valid = ReadingCurrentCore(lease, Allowed); });
        if (!completed || !valid || !Allowed()) { RetireReading(lease); return false; }
        return true;
    }

    public void RetireReading(ReadingLease lease)
    {
        if (Interlocked.Exchange(ref lease.Active, 0) == 0) return;
        Interlocked.Increment(ref lease.Version);
        try { _queue.Add(() => { if (ReferenceEquals(_reading, lease)) RetireReadingCore(); }); } catch (InvalidOperationException) { }
        lease.Invalidated();
    }

    private void RetireReadingCore()
    {
        var lease = _reading; _reading = null;
        if (lease is null) return;
        if (Interlocked.Exchange(ref lease.Active, 0) != 0) lease.Invalidated();
        if (lease.AddedComposition) _automation->RemoveTextEditTextChangedEventHandler(lease.Element, lease.Composition);
        if (lease.AddedFinalized) _automation->RemoveTextEditTextChangedEventHandler(lease.Element, lease.Finalized);
        if (lease.AddedChanged) _automation->RemoveAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextChangedEventId, lease.Element, lease.Changed);
        if (lease.AddedSelection) _automation->RemoveAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextSelectionChangedEventId, lease.Element, lease.Selection);
        if (lease.Composition is not null) lease.Composition->Release();
        if (lease.Finalized is not null) lease.Finalized->Release();
        if (lease.Changed is not null) lease.Changed->Release();
        if (lease.Selection is not null) lease.Selection->Release();
        if (lease.Range is not null) lease.Range->Release();
        if (lease.TextPattern is not null) lease.TextPattern->Release();
        if (lease.Edit is not null) lease.Edit->Release();
        if (lease.Element is not null) lease.Element->Release();
        lease.Text = "";
    }

    private bool ReadingCurrentCore(ReadingLease lease, Func<bool> allowed)
    {
        IUIAutomationElement* focused = null;
        IUIAutomationElement* hit = null;
        IUIAutomationTextRange* selected = null;
        try
        {
            long before = Interlocked.Read(ref lease.Version);
            if (!allowed() || !ReadingNotSecurePath(lease.Element, lease.ProcessId, allowed) || !allowed()) return false;
            if (!lease.OwnedTest)
            {
                if (_automation->GetFocusedElement(&focused).Failed || focused is null || !allowed()) return false;
                BOOL same = default;
                if (_automation->CompareElements(lease.Element, focused, &same).Failed || !same || !allowed()) return false;
            }
            if (lease.Pointer is { } point)
            {
                BOOL same = default;
                if (_automation->ElementFromPoint(point, &hit).Failed || hit is null || !allowed()
                    || _automation->CompareElements(lease.Element, hit, &same).Failed || !same || !allowed()) return false;
            }
            if (!ReadingClear(lease, allowed)) return false;
            if (lease.ScreenGuard) return allowed() && before == Interlocked.Read(ref lease.Version);
            selected = CurrentReadingRange(lease, allowed);
            if (selected is null || !allowed()) return false;
            int start = 0, end = 0;
            if (selected->CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, lease.Range,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, &start).Failed || start != 0 || !allowed()) return false;
            if (selected->CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, lease.Range,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, &end).Failed || end != 0 || !allowed()) return false;
            string? text = ReadingText(selected, allowed);
            return allowed() && string.Equals(text, lease.Text, StringComparison.Ordinal)
                && ReadingClear(lease, allowed) && allowed() && before == Interlocked.Read(ref lease.Version);
        }
        finally { if (selected is not null) selected->Release(); if (focused is not null) focused->Release(); if (hit is not null) hit->Release(); }
    }

    private static bool ReadingClear(ReadingLease lease, Func<bool> allowed)
    {
        IUIAutomationTextRange* composition = null;
        try
        {
            long version = Interlocked.Read(ref lease.Version);
            return allowed() && lease.Edit->GetActiveComposition(&composition).Succeeded && composition is null
                && allowed() && version == Interlocked.Read(ref lease.Version);
        }
        finally { if (composition is not null) composition->Release(); }
    }

    private static IUIAutomationTextRange* CurrentReadingRange(ReadingLease lease, Func<bool> allowed)
    {
        if (lease.Pointer is { } point) return PointerReadingRange(lease, point, allowed);
        IUIAutomationTextRangeArray* ranges = null;
        IUIAutomationTextRange* selected = null;
        try
        {
            if (!allowed() || lease.TextPattern->GetSelection(&ranges).Failed || ranges is null || !allowed()) return null;
            int count = 0;
            if (ranges->get_Length(&count).Failed || count != 1 || !allowed()) return null;
            if (ranges->GetElement(0, &selected).Failed || selected is null || !allowed()) return null;
            int comparison = 0;
            if (selected->CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, selected,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, &comparison).Failed || comparison == 0 || !allowed()) return null;
            var result = selected; selected = null; return result;
        }
        finally { if (selected is not null) selected->Release(); if (ranges is not null) ranges->Release(); }
    }

    private static IUIAutomationTextRange* PointerReadingRange(ReadingLease lease, System.Drawing.Point point, Func<bool> allowed)
    {
        IUIAutomationTextRange* range = null;
        Windows.Win32.System.Com.SAFEARRAY* rectangles = null;
        try
        {
            if (!allowed() || lease.TextPattern->RangeFromPoint(point, &range).Failed || range is null || !allowed()
                || range->ExpandToEnclosingUnit(TextUnit.TextUnit_Word).Failed || !allowed()
                || range->GetBoundingRectangles(&rectangles).Failed || rectangles is null || !allowed()
                || rectangles->cDims != 1 || rectangles->cbElements != sizeof(double)) return null;
            int lower = 0, upper = -1;
            if (SpeechNativeMethods.SafeArrayGetLBound(rectangles, 1, &lower).Failed
                || SpeechNativeMethods.SafeArrayGetUBound(rectangles, 1, &upper).Failed
                || upper < lower || (long)upper - lower >= 4096 || ((long)upper - lower + 1) % 4 != 0 || !allowed()) return null;
            void* data = null;
            if (SpeechNativeMethods.SafeArrayAccessData(rectangles, &data).Failed || data is null) return null;
            bool hit = false;
            try
            {
                var values = new ReadOnlySpan<double>(data, (int)((long)upper - lower + 1));
                for (int i = 0; i < values.Length; i += 4)
                {
                    double x = values[i], y = values[i + 1], w = values[i + 2], h = values[i + 3];
                    if (double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(w) && double.IsFinite(h) && double.IsFinite(x + w) && double.IsFinite(y + h)
                        && w > 0 && h > 0 && point.X >= x && point.X < x + w && point.Y >= y && point.Y < y + h) { hit = true; break; }
                }
            }
            finally { SpeechNativeMethods.SafeArrayUnaccessData(rectangles); }
            if (!hit || !allowed()) return null;
            var result = range; range = null; return result;
        }
        finally { if (rectangles is not null) SpeechNativeMethods.SafeArrayDestroy(rectangles); if (range is not null) range->Release(); }
    }

    private static string? ReadingText(IUIAutomationTextRange* range, Func<bool> allowed)
    {
        const int max = 262144;
        BSTR text = default;
        try
        {
            if (!allowed() || range->GetText(max + 1, &text).Failed || !allowed() || text.Value is null || text.Length > max) return null;
            string value = text.ToString();
            if (!allowed() || new UTF8Encoding(false, true).GetByteCount(value) > max) return null;
            return value;
        }
        catch (EncoderFallbackException) { return null; }
        finally { if (text.Value is not null) Marshal.FreeBSTR((nint)text.Value); }
    }

    private static bool ReadingNotSecure(IUIAutomationElement* element, Func<bool> allowed)
    {
        BSTR name = default, help = default, type = default, className = default;
        try
        {
            BOOL enabled = default, password = default;
            if (!allowed() || element->get_CurrentIsEnabled(&enabled).Failed || !enabled || !allowed()) return false;
            if (element->get_CurrentIsPassword(&password).Failed || password || !allowed()) return false;
            if (element->get_CurrentName(&name).Failed || !allowed() || name.Length > 4096) return false;
            if (element->get_CurrentHelpText(&help).Failed || !allowed() || help.Length > 4096) return false;
            if (element->get_CurrentLocalizedControlType(&type).Failed || !allowed() || type.Length > 4096) return false;
            if (element->get_CurrentClassName(&className).Failed || !allowed() || className.Length > 4096) return false;
            return ReadingControlAllowed(name.ToString(), help.ToString(), type.ToString(), className.ToString());
        }
        finally
        {
            if (name.Value is not null) Marshal.FreeBSTR((nint)name.Value);
            if (help.Value is not null) Marshal.FreeBSTR((nint)help.Value);
            if (type.Value is not null) Marshal.FreeBSTR((nint)type.Value);
            if (className.Value is not null) Marshal.FreeBSTR((nint)className.Value);
        }
    }

    internal static bool ReadingControlAllowed(string name, string help, string type, string className) =>
        !SecureFieldDetector.IsSecure(name, help, type)
        && !Cotabby.Core.Focus.TerminalAppDetector.IsIntegratedTerminal(className);

    private bool ReadingNotSecurePath(IUIAutomationElement* element, int pid, Func<bool> allowed)
    {
        IUIAutomationTreeWalker* walker = null;
        IUIAutomationElement* current = null;
        try
        {
            if (!allowed() || _automation->get_RawViewWalker(&walker).Failed || walker is null || !allowed()) return false;
            element->AddRef(); current = element;
            for (int depth = 0; depth < 7 && current is not null; depth++)
            {
                int owner = 0;
                if (!allowed() || current->get_CurrentProcessId(&owner).Failed || !allowed()) return false;
                if (owner != pid) return true;
                if (!ReadingNotSecure(current, allowed) || !allowed()) return false;
                IUIAutomationElement* parent = null;
                if (walker->GetParentElement(current, &parent).Failed || !allowed()) { if (parent is not null) parent->Release(); return false; }
                current->Release(); current = parent;
            }
            return allowed();
        }
        finally { if (current is not null) current->Release(); if (walker is not null) walker->Release(); }
    }
}
