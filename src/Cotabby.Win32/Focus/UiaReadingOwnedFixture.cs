using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Cotabby.Win32.Focus;

internal sealed unsafe partial class UiaTextEditEvidence
{
    /// <summary>Test-only owned subtree entry. Never asks UIA for the user's focused/hit element;
    /// CaptureReadingCore independently verifies current-process HWND ownership.</summary>
    internal ReadingLease? CaptureOwnedReadingDescendantForTest(nint host, string runtimeId, Func<bool> allowed, Action invalidated) =>
        string.IsNullOrWhiteSpace(runtimeId) ? null : CaptureReadingCore(host, Environment.ProcessId, null, false, allowed, invalidated, true, runtimeId);
    internal ReadingLease? CaptureOwnedReadingRootsForTest(nint host, Func<string?> focused, string pointed, Func<bool> allowed, Action invalidated) =>
        CaptureReadingCore(host, Environment.ProcessId, null, false, allowed, invalidated, true, ownedPointId: pointed, ownedFocus: focused);
    private IUIAutomationElement* OwnedReadingRoot(nint host, string id, Func<bool> allowed)
    {
        IUIAutomationElement* root = null; IUIAutomationTreeWalker* walker = null; var pending = new Stack<nint>();
        try
        {
            if (_automation is null || !allowed() || _automation->ElementFromHandle(new HWND(host), &root).Failed || root is null || !allowed()
                || _automation->get_RawViewWalker(&walker).Failed || walker is null || !allowed()) return null;
            root->AddRef(); pending.Push((nint)root); int budget = 64;
            while (pending.Count > 0 && budget-- > 0 && allowed())
            {
                var current = (IUIAutomationElement*)pending.Pop();
                try
                {
                    if (RuntimeId(current) == id && allowed()) { var result = current; current = null; return result; }
                    IUIAutomationElement* next = null;
                    if (walker->GetNextSiblingElement(current, &next).Succeeded && next is not null) pending.Push((nint)next);
                    if (!allowed()) break;
                    IUIAutomationElement* first = null;
                    if (walker->GetFirstChildElement(current, &first).Succeeded && first is not null) pending.Push((nint)first);
                }
                finally { if (current is not null) current->Release(); }
            }
            return null;
        }
        finally { while (pending.Count > 0) ((IUIAutomationElement*)pending.Pop())->Release(); if (walker is not null) walker->Release(); if (root is not null) root->Release(); }
    }
}
