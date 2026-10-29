using System.Runtime.InteropServices;
using System.Windows.Automation;
using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Insertion;
using Cotabby.Win32.Interop;

namespace Cotabby.Win32.ExpressionAssist;

// Test seam is internal: production uses the existing guarded keyboard/clipboard implementations,
// while the session retains all admission, focus, receipt and one-shot responsibilities.
internal interface IExpressionSessionEffects
{
    nint Foreground { get; }
    bool Activate(nint host);
    UiaExpressionSelectionReader.ValueSelection? Read(AutomationElement? element, Func<bool> allowed);
    Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct);
    bool SendUnicode(FocusedField field, int backspaces, string text, Func<bool> authorized);
    bool SendPaste(FocusedField field, int backspaces, Func<bool> authorized);
}

internal sealed class Win32ExpressionSessionEffects(ClipboardTextReader? clipboard) : IExpressionSessionEffects
{
    private readonly KeySender _sender = new();
    private readonly Win32ClipboardPasteLeaseFactory _clipboard = new(clipboard);
    public nint Foreground => NativeMethods.GetForegroundWindow();
    public bool Activate(nint host) => SetForegroundWindow(host);
    public UiaExpressionSelectionReader.ValueSelection? Read(AutomationElement? element, Func<bool> allowed) =>
        element is null ? null : UiaExpressionSelectionReader.Read(element, allowed);
    public Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct) => _clipboard.PrepareAsync(text, ct);
    public bool SendUnicode(FocusedField field, int backspaces, string text, Func<bool> authorized) =>
        _sender.SendUnicodeEdit(field, backspaces, text, authorized);
    public bool SendPaste(FocusedField field, int backspaces, Func<bool> authorized) =>
        _sender.SendPasteEdit(field, backspaces, authorized);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);
}
