using System.Diagnostics;
using System.Runtime.InteropServices;
using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace Cotabby.Win32.Focus;

// This partial borrows the existing UIA MTA. Native references never leave it. Unlike a read,
// an edit must join the queued action even after its deadline, so the insertion gate and clipboard
// lease cannot be released while an already accepted native dispatch is still running.
internal sealed unsafe partial class UiaTextEditEvidence
{
    internal Task<RangeEditDispatch> ReplaceWritingRangeAsync(FocusedField saved, LearningTextRange range, string source,
        Func<bool> permit, Func<Func<bool>, bool> dispatch)
    {
        if (_disposed) return Task.FromResult(RangeEditDispatch.Rejected);
        if (Interlocked.Increment(ref _pendingTextReads) > 4)
        { Interlocked.Decrement(ref _pendingTextReads); return Task.FromResult(RangeEditDispatch.Rejected); }
        long queuedAt = Stopwatch.GetTimestamp();
        var completion = new TaskCompletionSource<RangeEditDispatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try
                {
                    if (_disposed || Stopwatch.GetElapsedTime(queuedAt) > TimeSpan.FromMilliseconds(500) || !permit())
                    { completion.TrySetResult(RangeEditDispatch.Rejected); return; }
                    long started = Stopwatch.GetTimestamp();
                    bool Allowed() => !_disposed && Version == saved.CompositionSequence && permit()
                        && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2);
                    completion.TrySetResult(ReplaceWritingRangeCore(saved, range, source, Allowed, dispatch));
                }
                catch { completion.TrySetResult(RangeEditDispatch.Uncertain); }
                finally { Interlocked.Decrement(ref _pendingTextReads); }
            });
        }
        catch (InvalidOperationException)
        { Interlocked.Decrement(ref _pendingTextReads); completion.TrySetResult(RangeEditDispatch.Rejected); }
        return completion.Task;
    }

    private RangeEditDispatch ReplaceWritingRangeCore(FocusedField saved, LearningTextRange range, string source,
        Func<bool> allowed, Func<Func<bool>, bool> dispatch)
    {
        IUIAutomationElement* element = null;
        try
        {
            if (!WritingFocused(saved, allowed, &element)) return RangeEditDispatch.Rejected;
            return ReplaceWritingRangeOnElement(element, saved, range, source, allowed, dispatch, () => WritingFocusedMatches(saved, allowed));
        }
        finally { if (element is not null) element->Release(); }
    }

    private static RangeEditDispatch ReplaceWritingRangeOnElement(IUIAutomationElement* element, FocusedField saved,
        LearningTextRange range, string source, Func<bool> allowed, Func<Func<bool>, bool> dispatch, Func<bool> focused)
    {
        IUnknown* raw = null;
        IUIAutomationTextPattern* pattern = null;
        IUIAutomationTextRangeArray* selections = null;
        IUIAutomationTextRange* document = null;
        IUIAutomationTextRange* target = null;
        IUIAutomationTextRange* prefix = null;
        IUIAutomationTextRange* through = null;
        bool selectionAttempted = false;
        try
        {
            var before = NativeTextRangeReader.Read(element, allowed);
            if (before is not { } original || original.Value != saved.Text || original.Start != saved.CaretOffset
                || original.Length != saved.SelectionLength || !allowed()) return RangeEditDispatch.Rejected;
            if (element->GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId, &raw).Failed || raw is null || !allowed()) return RangeEditDispatch.Rejected;
            Guid iid = IUIAutomationTextPattern.IID_Guid;
            if (raw->QueryInterface(&iid, (void**)&pattern).Failed || pattern is null || !allowed()
                || pattern->get_DocumentRange(&document).Failed || document is null || !allowed()
                || !NativeTextRangeReader.IsWritable(document, allowed)) return RangeEditDispatch.Rejected;

            // Find whole prefixes and verify their document-start anchor. A UTF-16 offset is not
            // a UIA Character count, and searching for source alone would target repeated text.
            bool alreadySelected = original.Start == range.Location && original.Length == range.Length;
            if (alreadySelected)
            {
                if (pattern->GetSelection(&selections).Failed || selections is null || !allowed()
                    || selections->GetElement(0, &target).Failed || target is null || !allowed()) return RangeEditDispatch.Rejected;
            }
            else
            {
                through = FindWritingPrefix(document, saved.Text[..(range.Location + range.Length)], allowed);
                if (through is null || !allowed()) return RangeEditDispatch.Rejected;
                if (range.Location > 0)
                {
                    prefix = FindWritingPrefix(document, saved.Text[..range.Location], allowed);
                    if (prefix is null || !allowed()) return RangeEditDispatch.Rejected;
                }
                if (through->Clone(&target).Failed || target is null || !allowed()
                    || target->MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                        prefix is null ? document : prefix, prefix is null ? TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start
                            : TextPatternRangeEndpoint.TextPatternRangeEndpoint_End).Failed || !allowed()) return RangeEditDispatch.Rejected;
            }
            if (NativeTextRangeReader.Text(target, source.Length + 1, allowed) != source
                || !NativeTextRangeReader.IsWritable(target, allowed)) return RangeEditDispatch.Rejected;
            // A provider read may itself reenter the host. Recheck original identity/value/selection
            // immediately before the first selection effect, then positively observe that effect.
            if (!focused() || !allowed()) return RangeEditDispatch.Rejected;
            before = NativeTextRangeReader.Read(element, allowed);
            if (before is not { } fresh || fresh != original || !allowed() || !focused() || !allowed()) return RangeEditDispatch.Rejected;
            if (!alreadySelected)
            {
                selectionAttempted = true;
                if (target->Select().Failed || !allowed()) return RangeEditDispatch.Uncertain;
            }
            for (int attempt = 0; attempt < WritingRangeSelectionAcknowledgement.MaximumAttempts; attempt++)
            {
                if (!allowed() || !focused() || !allowed()) return RangeEditDispatch.Uncertain;
                var selected = NativeTextRangeReader.Read(element, allowed);
                var decision = WritingRangeSelectionAcknowledgement.Assess(saved, range, allowed(), selected?.Value,
                    selected is { } receipt ? new(receipt.Start, receipt.Length) : null);
                if (decision == WritingRangeSelectionDecision.Rejected) return RangeEditDispatch.Uncertain;
                if (decision == WritingRangeSelectionDecision.Confirmed && selected is { } selectedValue)
                {
                    nint selectedElement = (nint)element, selectedRange = (nint)target;
                    bool FinalPermit() => allowed() && focused()
                        && NativeTextRangeReader.Read((IUIAutomationElement*)selectedElement, allowed) is { } last && last == selectedValue
                        && NativeTextRangeReader.IsWritable((IUIAutomationTextRange*)selectedRange, allowed)
                        && allowed() && focused() && allowed();
                    // The callback's KeySender checks the same live permit again at SendInput.
                    if (!FinalPermit()) return selectionAttempted ? RangeEditDispatch.Uncertain : RangeEditDispatch.Rejected;
                    selectionAttempted = true; // The content callback can partially dispatch before throwing.
                    return dispatch(FinalPermit) ? RangeEditDispatch.Dispatched : RangeEditDispatch.Uncertain;
                }
                // Only the old caret/selection can be a delayed publication. Any unrelated
                // selection is an intervening user action, never an invitation to select again.
                if (attempt < WritingRangeSelectionAcknowledgement.MaximumAttempts - 1) Thread.Sleep(WritingRangeSelectionAcknowledgement.PollInterval);
            }
            return RangeEditDispatch.Uncertain;
        }
        catch { return selectionAttempted ? RangeEditDispatch.Uncertain : RangeEditDispatch.Rejected; }
        finally
        {
            if (through is not null) through->Release(); if (prefix is not null) prefix->Release();
            if (target is not null) target->Release(); if (document is not null) document->Release();
            if (selections is not null) selections->Release();
            if (pattern is not null) pattern->Release(); if (raw is not null) raw->Release();
        }
    }

    // Native component verification only, on the existing owned-descendant test callback/MTA.
    // A same-process element is mandatory; this entry never accepts a user's editor or sends keys.
    internal static RangeEditDispatch SelectOwnedWritingRangeForTest(nint ownedElement, FocusedField saved,
        LearningTextRange range, string source, Func<bool> allowed, Func<Func<bool>, bool> dispatch, Func<bool> focused)
    {
        var element = (IUIAutomationElement*)ownedElement;
        int pid = 0;
        if (element is null || element->get_CurrentProcessId(&pid).Failed || pid != Environment.ProcessId
            || RuntimeId(element) != saved.FieldId) return RangeEditDispatch.Rejected;
        return ReplaceWritingRangeOnElement(element, saved, range, source, allowed, dispatch, focused);
    }

    private bool WritingFocused(FocusedField saved, Func<bool> allowed, IUIAutomationElement** result)
    {
        if (_automation is null || !allowed() || _automation->GetFocusedElement(result).Failed || *result is null || !allowed()) return false;
        int process = 0;
        return (*result)->get_CurrentProcessId(&process).Succeeded && process == saved.ProcessId && allowed()
            && RuntimeId(*result) == saved.FieldId && allowed();
    }
    private bool WritingFocusedMatches(FocusedField saved, Func<bool> allowed)
    {
        IUIAutomationElement* current = null;
        try { return WritingFocused(saved, allowed, &current); }
        finally { if (current is not null) current->Release(); }
    }
    private static IUIAutomationTextRange* FindWritingPrefix(IUIAutomationTextRange* document, string text, Func<bool> allowed)
    {
        BSTR query = new((char*)Marshal.StringToBSTR(text));
        IUIAutomationTextRange* found = null;
        try
        {
            if (!allowed() || document->FindText(query, false, false, &found).Failed || found is null || !allowed()) return null;
            int start = 1;
            if (found->CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, document,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, &start).Failed || start != 0 || !allowed()
                || NativeTextRangeReader.Text(found, text.Length + 1, allowed) != text) return null;
            var result = found; found = null; return result;
        }
        finally { if (found is not null) found->Release(); Marshal.FreeBSTR((nint)query.Value); }
    }
}
