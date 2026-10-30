using System.Diagnostics;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.ExpressionAssist;
using Windows.Win32.UI.Accessibility;

namespace Cotabby.Win32.Focus;

// The panel's retained receipt is consumed at the native commit boundary. Its text/selection
// notifications must not mistake our own acknowledged Select for a new user action. After this
// one-way transfer, the existing exact range transaction and live field/IME checks own admission.
internal sealed unsafe partial class UiaTextEditEvidence
{
    internal static NativeTextRangeReader.ValueSelection? ReadOwnedExpressionValueForTest(nint ownedElement)
    {
        var element = (IUIAutomationElement*)ownedElement; int pid = 0;
        return element is not null && element->get_CurrentProcessId(&pid).Succeeded && pid == Environment.ProcessId
            ? NativeTextRangeReader.Read(element, () => true) : null;
    }
    internal Task<ExpressionEditOutcome> ReplaceExpressionRangeAsync(long token, long retainedVersion, FocusedField saved,
        LearningTextRange range, string source, string expected, Func<bool> permit, Func<Func<bool>, bool> dispatch) =>
        QueueExpressionRange(token, retainedVersion, saved, range, source, expected, permit, dispatch, ownedTest: false);

    internal Task<ExpressionEditOutcome> ReplaceOwnedExpressionRangeForTest(long token, long retainedVersion, FocusedField saved,
        LearningTextRange range, string source, string expected, Func<bool> permit, Func<Func<bool>, bool> dispatch)
    {
        if (saved.ProcessId != Environment.ProcessId) throw new ArgumentException("Only an owned fixture is permitted.");
        return QueueExpressionRange(token, retainedVersion, saved, range, source, expected, permit, dispatch, ownedTest: true);
    }

    private Task<ExpressionEditOutcome> QueueExpressionRange(long token, long retainedVersion, FocusedField saved,
        LearningTextRange range, string source, string expected, Func<bool> permit, Func<Func<bool>, bool> dispatch, bool ownedTest)
    {
        if (_disposed) return Task.FromResult(ExpressionEditOutcome.Rejected);
        if (Interlocked.Increment(ref _pendingTextReads) > 4)
        { Interlocked.Decrement(ref _pendingTextReads); return Task.FromResult(ExpressionEditOutcome.Rejected); }
        long queued = Stopwatch.GetTimestamp();
        var completion = new TaskCompletionSource<ExpressionEditOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                IUIAutomationElement* element = null;
                bool consumed = false;
                try
                {
                    long started = Stopwatch.GetTimestamp();
                    bool Allowed() => !_disposed && Version == saved.CompositionSequence && permit()
                        && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2);
                    if (Stopwatch.GetElapsedTime(queued) > TimeSpan.FromMilliseconds(500) || !Allowed()
                        || !IsRetainedReceiptCurrent(token, retainedVersion) || _retainedHost is not { } retained
                        || retained.ProcessId != saved.ProcessId || retained.FieldId != saved.FieldId)
                    { completion.TrySetResult(ExpressionEditOutcome.Rejected); return; }
                    if (ownedTest) { element = retained.Element; element->AddRef(); }
                    else if (!WritingFocused(saved, Allowed, &element))
                    { completion.TrySetResult(ExpressionEditOutcome.Rejected); return; }
                    nint target = (nint)element;
                    bool Focused() => ownedTest ? RuntimeId((IUIAutomationElement*)target) == saved.FieldId : WritingFocusedMatches(saved, Allowed);
                    var original = NativeTextRangeReader.Read(element, Allowed);
                    if (original is not { } value || value.Value != saved.Text || value.Start != saved.CaretOffset
                        || value.Length != saved.SelectionLength || !ScreenCompositionAdmitted(element, Allowed)
                        || !Allowed() || !Focused() || !IsRetainedReceiptCurrent(token, retainedVersion)
                        || Interlocked.CompareExchange(ref _retainedGeneration, token + 1, token) != token)
                    { completion.TrySetResult(ExpressionEditOutcome.Rejected); return; }
                    consumed = true; RetireRetainedCore();
                    var result = ReplaceWritingRangeOnElement(element, saved, range, source, Allowed, dispatch, Focused);
                    if (result != RangeEditDispatch.Dispatched)
                    { completion.TrySetResult(result == RangeEditDispatch.Rejected ? ExpressionEditOutcome.Rejected : ExpressionEditOutcome.Unverified); return; }
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        Thread.Sleep(60);
                        if (!Allowed() || !Focused() || !ScreenCompositionAdmitted(element, Allowed)) break;
                        var after = NativeTextRangeReader.Read(element, Allowed);
                        if (after is { } observed && observed.Value == expected && Allowed() && Focused() && Allowed())
                        { completion.TrySetResult(ExpressionEditOutcome.Verified); return; }
                    }
                    completion.TrySetResult(ExpressionEditOutcome.Unverified);
                }
                catch { completion.TrySetResult(consumed ? ExpressionEditOutcome.Unverified : ExpressionEditOutcome.Rejected); }
                finally { if (element is not null) element->Release(); Interlocked.Decrement(ref _pendingTextReads); }
            });
        }
        catch (InvalidOperationException)
        { Interlocked.Decrement(ref _pendingTextReads); completion.TrySetResult(ExpressionEditOutcome.Rejected); }
        return completion.Task;
    }
}
