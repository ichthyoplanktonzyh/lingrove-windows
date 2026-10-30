using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Focus;
using Cotabby.Win32.Interop;

namespace Cotabby.Win32.Insertion;

/// <summary>Writing-only range adapter. Borrows the existing focus MTA, insertion gate, keyboard
/// and clipboard effects; it creates no global listener or independent editor lifetime.</summary>
public sealed class Win32RangeTextInserter : IRangeTextInserter, IRangeSelectionDispatcher
{
    private readonly UiaFocusTracker _focus;
    private readonly GuardedRangeTextInserter _inserter;
    public Win32RangeTextInserter(UiaFocusTracker focus, InsertionTransactionGate gate, ClipboardTextReader clipboard)
    { _focus = focus; _inserter = new(new Win32InsertionBackend(focus, clipboard), this, gate); }

    public Task<RangeEditDispatch> ReplaceRangeAsync(FocusedField target, LearningTextRange range, string source,
        string replacement, Func<bool> permit, CancellationToken cancellationToken) =>
        _inserter.ReplaceRangeAsync(target, range, source, replacement, permit, cancellationToken);

    Task<RangeEditDispatch> IRangeSelectionDispatcher.SelectAndDispatchAsync(FocusedField target, LearningTextRange range,
        string source, Func<bool> permit, Func<Func<bool>, bool> dispatch)
    {
        bool Allowed()
        {
            if (!permit() || _focus.ActiveFocusEpoch != target.FocusChangeSequence) return false;
            var foreground = NativeMethods.GetForegroundWindow();
            return foreground != 0 && NativeMethods.GetWindowThreadProcessId(foreground, out uint pid) != 0
                && pid == (uint)target.ProcessId;
        }
        return _focus.ExpressionCompositionOwner?.ReplaceWritingRangeAsync(target, range, source, Allowed, dispatch)
            ?? Task.FromResult(RangeEditDispatch.Rejected);
    }
}
