using System.Runtime.InteropServices;
using System.Windows.Automation;
using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Insertion;
using Cotabby.Win32.Interop;
using Cotabby.Win32.Focus;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Win32.ExpressionAssist;

// Test seam is internal: production uses the existing guarded keyboard/clipboard implementations,
// while the session retains all admission, focus, receipt and one-shot responsibilities.
internal interface IExpressionSessionEffects
{
    nint Foreground { get; }
    bool Activate(nint host);
    UiaExpressionSelectionReader.ValueSelection? Read(AutomationElement? element, long token, Func<bool> allowed);
    Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct);
    bool SendUnicode(FocusedField field, int backspaces, string text, Func<bool> authorized);
    bool SendPaste(FocusedField field, int backspaces, Func<bool> authorized);
    // Caller already owns the shared gate/clipboard lease. The native owner consumes the retained
    // receipt once, selects the literal range, dispatches once and acknowledges the resulting value.
    Task<ExpressionEditOutcome> ReplaceRangeAsync(long token, long retainedVersion, FocusedField field,
        LearningTextRange range, string source, string expected, Func<bool> permit, Func<Func<bool>, bool> dispatch) =>
        Task.FromResult(ExpressionEditOutcome.Rejected);
}

internal sealed class Win32ExpressionSessionEffects(ClipboardTextReader? clipboard, IExpressionRetainedEvidence composition) : IExpressionSessionEffects
{
    private readonly KeySender _sender = new();
    private readonly Win32ClipboardPasteLeaseFactory _clipboard = new(clipboard);
    public nint Foreground => NativeMethods.GetForegroundWindow();
    public bool Activate(nint host) => SetForegroundWindow(host);
    public UiaExpressionSelectionReader.ValueSelection? Read(AutomationElement? element, long token, Func<bool> allowed)
    {
        if (element is null || !UiaExpressionSelectionReader.IsSafe(element, allowed)) return null;
        var value = composition.ReadRetained(token, allowed);
        return value is { } v && allowed() ? new(v.Value, new ExpressionEditorRange(v.Start, v.Length)) : null;
    }
    public Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct) => _clipboard.PrepareAsync(text, ct);
    public bool SendUnicode(FocusedField field, int backspaces, string text, Func<bool> authorized) =>
        _sender.SendUnicodeEdit(field, backspaces, text, authorized);
    public bool SendPaste(FocusedField field, int backspaces, Func<bool> authorized) =>
        _sender.SendPasteEdit(field, backspaces, authorized);
    public Task<ExpressionEditOutcome> ReplaceRangeAsync(long token, long retainedVersion, FocusedField field,
        LearningTextRange range, string source, string expected, Func<bool> permit, Func<Func<bool>, bool> dispatch) =>
        composition is UiaTextEditEvidence native
            ? native.ReplaceExpressionRangeAsync(token, retainedVersion, field, range, source, expected, permit, dispatch)
            : Task.FromResult(ExpressionEditOutcome.Rejected);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);
}
