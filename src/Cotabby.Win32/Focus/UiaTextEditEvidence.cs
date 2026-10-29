using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Runtime.CompilerServices;
using Cotabby.Core.Focus;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;
using HRESULT = Windows.Win32.Foundation.HRESULT;

namespace Cotabby.Win32.Focus;

internal interface ICompositionEvidenceOwner : IDisposable
{
    long Version { get; }
    bool Execute(Action action);
    (InputCompositionState State, long Version) Observe(int pid, string field, long focusEpoch, Func<bool>? allowed = null);
    long Invalidate();
    void RetireAsync(long expectedEpoch);
}

internal interface IExpressionRetainedEvidence
{
    long? Register(int processId, string fieldId, nint hostWindow, nint panelWindow, Action changed, Func<bool> allowed, CancellationToken ct);
    (InputCompositionState State, long Version) Observe(long token, Func<bool> panelOwnsKeyboard, CancellationToken ct);
    bool IsCurrent(long token, long version);
    void Retire(long token);
    Task DrainAsync();
}

internal readonly record struct UiaBootstrapResult(int InitializeHResult, int? FactoryHResult, bool HasAutomation);
internal readonly record struct UiaOwnedElementResult(int ElementHResult, string? RuntimeId, int? PatternHResult,
    bool HasTextEditPattern, int? CompositionRegistrationHResult, int? FinalizedRegistrationHResult,
    int? CompositionRemovalHResult, int? FinalizedRemovalHResult, int? ActiveCompositionHResult, bool? HasActiveComposition);

// Owns all native TextEdit subscriptions on one MTA. COM retains each callback until its last
// native reference is released; callbacks carry only an epoch, never inspect the event payload.
[SupportedOSPlatform("windows8.1")]
internal sealed unsafe class UiaTextEditEvidence : ICompositionEvidenceOwner, IExpressionRetainedEvidence
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly Action _changed;
    private readonly Action _failed;
    private readonly TaskCompletionSource<UiaBootstrapResult> _bootstrap = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _version = 1;
    private long _epoch;
    private IUIAutomation3* _automation;
    private IUIAutomationElement* _element;
    private IUIAutomationTextEditPattern* _pattern;
    private IUIAutomationTextEditTextChangedEventHandler* _composition;
    private IUIAutomationTextEditTextChangedEventHandler* _finalized;
    private string? _field;
    private int _pid;
    private long _focusEpoch;
    private long _subscriptionEpoch;
    private int _pendingObservations;
    private int _retirementQueued;
    private long _retirementEpoch;
    private long _retainedGeneration;
    private int _retainedPending;
    private RetainedHost? _retainedHost;
    private volatile bool _disposed;

    public UiaTextEditEvidence(Action changed, Action? failed = null)
    {
        _changed = changed;
        _failed = failed ?? (() => { });
        _thread = new Thread(Run) { IsBackground = true, Name = "Lingrove UIA TextEdit" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public long Version => Interlocked.Read(ref _version);

    // Separate request-scoped host subscription lives on the existing UIA MTA owner. It is
    // independent of the ordinary focused-element slot, which must continue to retire normally.
    private sealed class RetainedHost
    {
        public required long Token;
        public required int ProcessId;
        public required string FieldId;
        public required nint HostWindow;
        public required nint PanelWindow;
        public required Action Changed;
        public long Version = 1;
        public IUIAutomationElement* Element;
        public IUIAutomationTextEditPattern* Pattern;
        public IUIAutomationTextEditTextChangedEventHandler* Composition;
        public IUIAutomationTextEditTextChangedEventHandler* Finalized;
        public IUIAutomationEventHandler* TextChanged;
        public IUIAutomationEventHandler* SelectionChanged;
    }

    internal long? RegisterRetainedHost(int processId, string fieldId, nint hostWindow,
        nint panelWindow, Action changed, CancellationToken ct, Func<bool>? requestAllowed = null)
    {
        if (_disposed || processId <= 0 || string.IsNullOrWhiteSpace(fieldId) || hostWindow == 0 || panelWindow == 0
            || hostWindow == panelWindow || changed is null) return null;
        if (Interlocked.CompareExchange(ref _retainedPending, 1, 0) != 0) return null;
        long token = Interlocked.Increment(ref _retainedGeneration);
        var completion = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = false;
        try
        {
            _queue.Add(() =>
            {
                long? result;
                try { result = RegisterRetainedHostCore(token, processId, fieldId, hostWindow, panelWindow, changed, requestAllowed); }
                catch { result = null; }
                Interlocked.Exchange(ref _retainedPending, 0);
                completion.TrySetResult(result);
            });
            queued = true;
            if (completion.Task.Wait(TimeSpan.FromMilliseconds(500), ct))
            {
                long? result = completion.Task.Result;
                if (result is null || ct.IsCancellationRequested || !(requestAllowed?.Invoke() ?? true)) { RetireRetainedHost(token); return null; }
                return result;
            }
            RetireRetainedHost(token);
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or InvalidOperationException)
        {
            RetireRetainedHost(token);
            return null;
        }
        finally { if (!queued) Interlocked.Exchange(ref _retainedPending, 0); }
    }

    private long? RegisterRetainedHostCore(long token, int pid, string field, nint host, nint panel, Action changed, Func<bool>? requestAllowed)
    {
        bool Allowed() => token == Interlocked.Read(ref _retainedGeneration) && (requestAllowed?.Invoke() ?? true);
        if (_automation is null || !Allowed()) return null;
        RetireRetainedCore();
        if (!Allowed() || Cotabby.Win32.Interop.NativeMethods.GetForegroundWindow() != host || !Allowed()) return null;
        if (Cotabby.Win32.Interop.NativeMethods.GetWindowThreadProcessId(host, out uint hostPid) == 0 || hostPid != (uint)pid || !Allowed()) return null;
        if (Cotabby.Win32.Interop.NativeMethods.GetWindowThreadProcessId(panel, out uint panelPid) == 0 || panelPid != (uint)Environment.ProcessId || !Allowed()) return null;
        if (!Allowed() || Cotabby.Win32.Interop.NativeMethods.GetForegroundWindow() != host) return null;
        IUIAutomationElement* element = null;
        try
        {
            if (_automation->GetFocusedElement(&element).Failed || element is null) return null;
            if (!Allowed() || Cotabby.Win32.Interop.NativeMethods.GetForegroundWindow() != host) return null;
            return BindRetainedElement(token, pid, field, host, panel, changed, element, requireForeground: true, requestAllowed);
        }
        finally { if (element is not null) element->Release(); }
    }

    private long? BindRetainedElement(long token, int pid, string field, nint host, nint panel, Action changed,
        IUIAutomationElement* element, bool requireForeground, Func<bool>? requestAllowed = null)
    {
        IUnknown* raw = null;
        IUIAutomationTextEditPattern* pattern = null;
        IUIAutomationTextEditTextChangedEventHandler* composition = null;
        IUIAutomationTextEditTextChangedEventHandler* finalized = null;
        IUIAutomationEventHandler* textChanged = null;
        IUIAutomationEventHandler* selectionChanged = null;
        bool first = false, second = false, textAdded = false, selectionAdded = false;
        try
        {
            bool Allowed() => token == Interlocked.Read(ref _retainedGeneration)
                && (requestAllowed?.Invoke() ?? true)
                && (!requireForeground || Cotabby.Win32.Interop.NativeMethods.GetForegroundWindow() == host);
            if (!Allowed()) return null;
            int actualPid = 0;
            Windows.Win32.Foundation.BOOL password = default;
            if (element->get_CurrentProcessId(&actualPid).Failed || actualPid != pid || !Allowed()) return null;
            if (RuntimeId(element) != field || !Allowed()) return null;
            if (element->get_CurrentIsPassword(&password).Failed || password || !Allowed()) return null;
            if (element->GetCurrentPattern(UIA_PATTERN_ID.UIA_TextEditPatternId, &raw).Failed || raw is null) return null;
            if (!Allowed()) return null;
            Guid iid = IUIAutomationTextEditPattern.IID_Guid;
            if (raw->QueryInterface(&iid, (void**)&pattern).Failed || pattern is null) return null;
            if (!Allowed()) return null;
            composition = Callback.CreateRetained(this, token);
            finalized = Callback.CreateRetained(this, token);
            if (!Allowed()) return null;
            if (_automation->AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element,
                TextEditChangeType.TextEditChangeType_Composition, null, composition).Failed) return null;
            first = true;
            if (!Allowed()) return null;
            if (_automation->AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element,
                TextEditChangeType.TextEditChangeType_CompositionFinalized, null, finalized).Failed) return null;
            second = true;
            if (!Allowed()) return null;
            textChanged = RetainedEventCallback.Create(this, token);
            selectionChanged = RetainedEventCallback.Create(this, token);
            if (!Allowed()) return null;
            if (_automation->AddAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextChangedEventId, element,
                TreeScope.TreeScope_Element, null, textChanged).Failed) return null;
            textAdded = true;
            if (!Allowed()) return null;
            if (_automation->AddAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextSelectionChangedEventId, element,
                TreeScope.TreeScope_Element, null, selectionChanged).Failed) return null;
            selectionAdded = true;
            if (!Allowed()) return null;
            var retained = new RetainedHost { Token = token, ProcessId = pid, FieldId = field, HostWindow = host,
                PanelWindow = panel, Changed = changed, Element = element, Pattern = pattern, Composition = composition, Finalized = finalized,
                TextChanged = textChanged, SelectionChanged = selectionChanged };
            element->AddRef();
            Volatile.Write(ref _retainedHost, retained);
            pattern = null; composition = null; finalized = null; textChanged = null; selectionChanged = null;
            return token;
        }
        finally
        {
            if (composition is not null && first) _automation->RemoveTextEditTextChangedEventHandler(element, composition);
            if (finalized is not null && second) _automation->RemoveTextEditTextChangedEventHandler(element, finalized);
            if (textChanged is not null && textAdded) _automation->RemoveAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextChangedEventId, element, textChanged);
            if (selectionChanged is not null && selectionAdded) _automation->RemoveAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextSelectionChangedEventId, element, selectionChanged);
            if (raw is not null) raw->Release();
            if (pattern is not null) pattern->Release();
            if (composition is not null) composition->Release();
            if (finalized is not null) finalized->Release();
            if (textChanged is not null) textChanged->Release();
            if (selectionChanged is not null) selectionChanged->Release();
        }
    }

    internal (InputCompositionState State, long Version) ObserveRetainedHost(long token,
        Func<bool> panelOwnsKeyboard, CancellationToken ct)
    {
        if (_disposed || token != Interlocked.Read(ref _retainedGeneration)) return default;
        if (Interlocked.CompareExchange(ref _retainedPending, 1, 0) != 0) { RetireRetainedHost(token); return default; }
        var completion = new TaskCompletionSource<(InputCompositionState, long)>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = false;
        try
        {
            _queue.Add(() =>
            {
                (InputCompositionState, long) result;
                try { result = ObserveRetainedCore(token, panelOwnsKeyboard); }
                catch { result = default; }
                Interlocked.Exchange(ref _retainedPending, 0);
                completion.TrySetResult(result);
            });
            queued = true;
            if (completion.Task.Wait(TimeSpan.FromMilliseconds(500), ct))
            {
                var result = completion.Task.Result;
                if (result.Item1 != InputCompositionState.Clear || ct.IsCancellationRequested)
                { RetireRetainedHost(token); return default; }
                return result;
            }
            RetireRetainedHost(token);
            return default;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or InvalidOperationException)
        {
            RetireRetainedHost(token);
            return default;
        }
        finally { if (!queued) Interlocked.Exchange(ref _retainedPending, 0); }
    }

    private (InputCompositionState, long) ObserveRetainedCore(long token, Func<bool> panelOwnsKeyboard)
    {
        var saved = _retainedHost;
        if (saved is null || saved.Token != token || token != Interlocked.Read(ref _retainedGeneration)) return default;
        nint foreground = Cotabby.Win32.Interop.NativeMethods.GetForegroundWindow();
        bool Allowed() => token == Interlocked.Read(ref _retainedGeneration)
            && Cotabby.Win32.Interop.NativeMethods.GetForegroundWindow() == foreground
            && (foreground == saved.HostWindow || (foreground == saved.PanelWindow && panelOwnsKeyboard()));
        if (!Allowed()) return default;
        int actualPid = 0;
        Windows.Win32.Foundation.BOOL password = default;
        if (saved.Element->get_CurrentProcessId(&actualPid).Failed || actualPid != saved.ProcessId || !Allowed()) return default;
        if (saved.Element->get_CurrentIsPassword(&password).Failed || password || !Allowed()) return default;
        if (RuntimeId(saved.Element) != saved.FieldId || !Allowed()) return default;
        if (Cotabby.Win32.Interop.NativeMethods.GetWindowThreadProcessId(saved.HostWindow, out uint hostPid) == 0 || hostPid != (uint)saved.ProcessId
            || Cotabby.Win32.Interop.NativeMethods.GetWindowThreadProcessId(saved.PanelWindow, out uint panelPid) == 0 || panelPid != (uint)Environment.ProcessId) return default;
        if (Cotabby.Win32.Interop.NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundPid) == 0
            || foregroundPid != (uint)(foreground == saved.HostWindow ? saved.ProcessId : Environment.ProcessId)) return default;
        if (foreground == saved.HostWindow)
        {
            IUIAutomationElement* focused = null;
            try
            {
                Windows.Win32.Foundation.BOOL same = default;
                if (!Allowed() || _automation->GetFocusedElement(&focused).Failed || focused is null || !Allowed()
                    || _automation->CompareElements(saved.Element, focused, &same).Failed || !same) return default;
            }
            finally { if (focused is not null) focused->Release(); }
        }
        long before = Interlocked.Read(ref saved.Version);
        if (!Allowed()) return default;
        IUIAutomationTextRange* range = null;
        HRESULT hr = saved.Pattern->GetActiveComposition(&range);
        bool composing = range is not null;
        if (range is not null) range->Release();
        long after = Interlocked.Read(ref saved.Version);
        if (hr.Failed || before != after || !Allowed()) return default;
        return (composing ? InputCompositionState.Composing : InputCompositionState.Clear, after);
    }

    internal void RetireRetainedHost(long token)
    {
        if (Interlocked.CompareExchange(ref _retainedGeneration, token + 1, token) != token) return;
        try { _queue.Add(() => { if (_retainedHost?.Token == token) RetireRetainedCore(); }); }
        catch (InvalidOperationException) { }
    }

    internal bool IsRetainedReceiptCurrent(long token, long version)
    {
        var saved = Volatile.Read(ref _retainedHost);
        return saved is not null && saved.Token == token && token == Interlocked.Read(ref _retainedGeneration)
            && version > 0 && version == Interlocked.Read(ref saved.Version);
    }

    internal Task DrainRetainedAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try { _queue.Add(() => completion.TrySetResult()); }
        catch (InvalidOperationException) { completion.TrySetResult(); }
        return completion.Task;
    }

    private void RetireRetainedCore()
    {
        var saved = _retainedHost;
        Volatile.Write(ref _retainedHost, null);
        if (saved is null) return;
        _automation->RemoveTextEditTextChangedEventHandler(saved.Element, saved.Composition);
        _automation->RemoveTextEditTextChangedEventHandler(saved.Element, saved.Finalized);
        _automation->RemoveAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextChangedEventId, saved.Element, saved.TextChanged);
        _automation->RemoveAutomationEventHandler(UIA_EVENT_ID.UIA_Text_TextSelectionChangedEventId, saved.Element, saved.SelectionChanged);
        saved.Composition->Release(); saved.Finalized->Release();
        saved.TextChanged->Release(); saved.SelectionChanged->Release();
        saved.Pattern->Release(); saved.Element->Release();
    }

    private void OnRetainedChanged(long token)
    {
        var saved = Volatile.Read(ref _retainedHost);
        if (saved is null || saved.Token != token || token != Interlocked.Read(ref _retainedGeneration)) return;
        Interlocked.Increment(ref saved.Version);
        RetireRetainedHost(token);
        saved.Changed();
    }

    // Test-only path exercises the same native subscription against an explicitly owned hidden
    // HWND, without consulting or changing foreground focus. It never grants a production receipt.
    internal Task<long?> RegisterOwnedRetainedForTest(nint handle, string fieldId, Action changed)
    {
        var completion = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (handle == 0 || Cotabby.Win32.Interop.NativeMethods.GetWindowThreadProcessId(handle, out uint pid) == 0
            || pid != (uint)Environment.ProcessId) { completion.SetResult(null); return completion.Task; }
        long token = Interlocked.Increment(ref _retainedGeneration);
        try
        {
            _queue.Add(() =>
            {
                IUIAutomationElement* element = null;
                try
                {
                    if (_automation is null || token != Interlocked.Read(ref _retainedGeneration)) { completion.TrySetResult(null); return; }
                    RetireRetainedCore();
                    if (_automation->ElementFromHandle(new Windows.Win32.Foundation.HWND(handle), &element).Failed || element is null)
                    { completion.TrySetResult(null); return; }
                    completion.TrySetResult(BindRetainedElement(token, Environment.ProcessId, fieldId, handle, handle, changed, element, false));
                }
                catch { completion.TrySetResult(null); }
                finally { if (element is not null) element->Release(); }
            });
        }
        catch (InvalidOperationException) { completion.TrySetResult(null); }
        return completion.Task;
    }

    internal Task<(InputCompositionState, long)> ObserveOwnedRetainedForTest(long token)
    {
        var completion = new TaskCompletionSource<(InputCompositionState, long)>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                var slot = _retainedHost;
                if (slot is null || slot.Token != token || token != Interlocked.Read(ref _retainedGeneration))
                { completion.TrySetResult(default); return; }
                IUIAutomationTextRange* range = null;
                long before = Interlocked.Read(ref slot.Version);
                HRESULT hr = slot.Pattern->GetActiveComposition(&range);
                bool composing = range is not null;
                if (range is not null) range->Release();
                long after = Interlocked.Read(ref slot.Version);
                completion.TrySetResult(hr.Succeeded && before == after && token == Interlocked.Read(ref _retainedGeneration)
                    ? (composing ? InputCompositionState.Composing : InputCompositionState.Clear, after) : default);
            });
        }
        catch (InvalidOperationException) { completion.TrySetResult(default); }
        return completion.Task;
    }

    internal Task<UiaBootstrapResult> BootstrapForTest => _bootstrap.Task;
    internal bool RetainedRegistrationPendingForTest => Volatile.Read(ref _retainedPending) != 0;

    long? IExpressionRetainedEvidence.Register(int pid, string field, nint host, nint panel, Action changed, Func<bool> allowed, CancellationToken ct) =>
        RegisterRetainedHost(pid, field, host, panel, changed, ct, allowed);
    (InputCompositionState State, long Version) IExpressionRetainedEvidence.Observe(long token, Func<bool> panel, CancellationToken ct) =>
        ObserveRetainedHost(token, panel, ct);
    bool IExpressionRetainedEvidence.IsCurrent(long token, long version) => IsRetainedReceiptCurrent(token, version);
    void IExpressionRetainedEvidence.Retire(long token) => RetireRetainedHost(token);
    Task IExpressionRetainedEvidence.DrainAsync() => DrainRetainedAsync();

    // This diagnostic is deliberately limited to a handle owned by this process. It never asks
    // UIA for the focused element and does not read text or authorize a composition receipt.
    internal Task<UiaOwnedElementResult> ProbeOwnedHandleForTest(nint handle)
    {
        var result = new TaskCompletionSource<UiaOwnedElementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (handle == 0 || Cotabby.Win32.Interop.NativeMethods.GetWindowThreadProcessId(handle, out uint pid) == 0 || pid != (uint)Environment.ProcessId)
        {
            result.SetException(new ArgumentException("An owned window handle is required.", nameof(handle)));
            return result.Task;
        }
        try
        {
            _queue.Add(() =>
            {
                try { result.TrySetResult(ProbeOwnedHandleCore(handle)); }
                catch (Exception ex) { result.TrySetException(ex); }
            });
        }
        catch (InvalidOperationException ex) { result.TrySetException(ex); }
        return result.Task;
    }

    internal Task<UiaOwnedElementResult?> ProbeOwnedDescendantForTest(nint handle, string runtimeId)
    {
        var result = new TaskCompletionSource<UiaOwnedElementResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (handle == 0 || Cotabby.Win32.Interop.NativeMethods.GetWindowThreadProcessId(handle, out uint pid) == 0 || pid != (uint)Environment.ProcessId || string.IsNullOrWhiteSpace(runtimeId))
        {
            result.SetException(new ArgumentException("An owned handle and descendant ID are required."));
            return result.Task;
        }
        try
        {
            _queue.Add(() =>
            {
                try { result.TrySetResult(ProbeOwnedDescendantCore(handle, runtimeId)); }
                catch (Exception ex) { result.TrySetException(ex); }
            });
        }
        catch (InvalidOperationException ex) { result.TrySetException(ex); }
        return result.Task;
    }

    private UiaOwnedElementResult? ProbeOwnedDescendantCore(nint handle, string runtimeId)
    {
        if (_automation is null) return null;
        IUIAutomationElement* root = null;
        IUIAutomationTreeWalker* walker = null;
        var pending = new Stack<nint>();
        try
        {
            if (_automation->ElementFromHandle(new Windows.Win32.Foundation.HWND(handle), &root).Failed || root is null ||
                _automation->get_RawViewWalker(&walker).Failed || walker is null) return null;
            IUIAutomationElement* child = null;
            if (walker->GetFirstChildElement(root, &child).Failed) return null;
            if (child is not null) pending.Push((nint)child);
            int remaining = 64;
            while (pending.Count > 0 && remaining-- > 0)
            {
                var element = (IUIAutomationElement*)pending.Pop();
                try
                {
                    if (RuntimeId(element) == runtimeId) return ProbeOwnedElementCore(element, 0);
                    IUIAutomationElement* next = null;
                    if (walker->GetNextSiblingElement(element, &next).Succeeded && next is not null) pending.Push((nint)next);
                    IUIAutomationElement* first = null;
                    if (walker->GetFirstChildElement(element, &first).Succeeded && first is not null) pending.Push((nint)first);
                }
                finally { element->Release(); }
            }
            return null;
        }
        finally
        {
            while (pending.Count > 0) ((IUIAutomationElement*)pending.Pop())->Release();
            if (walker is not null) walker->Release();
            if (root is not null) root->Release();
        }
    }

    private UiaOwnedElementResult ProbeOwnedHandleCore(nint handle)
    {
        if (_automation is null) return new(unchecked((int)0x80004005), null, null, false, null, null, null, null, null, null);
        IUIAutomationElement* element = null;
        try
        {
            HRESULT hr = _automation->ElementFromHandle(new Windows.Win32.Foundation.HWND(handle), &element);
            if (hr.Failed || element is null) return new((int)hr, null, null, false, null, null, null, null, null, null);
            return ProbeOwnedElementCore(element, (int)hr);
        }
        finally { if (element is not null) element->Release(); }
    }

    private UiaOwnedElementResult ProbeOwnedElementCore(IUIAutomationElement* element, int elementHResult)
    {
        IUnknown* pattern = null;
        IUIAutomationTextEditPattern* editPattern = null;
        IUIAutomationTextEditTextChangedEventHandler* composition = null;
        IUIAutomationTextEditTextChangedEventHandler* finalized = null;
        try
        {
            string? runtimeId = RuntimeId(element);
            HRESULT patternHr = element->GetCurrentPattern(UIA_PATTERN_ID.UIA_TextEditPatternId, &pattern);
            int? activeHr = null;
            bool? active = null;
            if (patternHr.Succeeded && pattern is not null)
            {
                Guid iid = IUIAutomationTextEditPattern.IID_Guid;
                HRESULT query = pattern->QueryInterface(&iid, (void**)&editPattern);
                if (query.Succeeded && editPattern is not null)
                {
                    IUIAutomationTextRange* range = null;
                    HRESULT activeResult = editPattern->GetActiveComposition(&range);
                    activeHr = (int)activeResult;
                    if (activeResult.Succeeded) active = range is not null;
                    if (range is not null) range->Release();
                }
                else activeHr = (int)query;
            }
            composition = Callback.Create(this, long.MinValue);
            finalized = Callback.Create(this, long.MinValue);
            HRESULT addComposition = _automation->AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element,
                TextEditChangeType.TextEditChangeType_Composition, null, composition);
            HRESULT addFinalized = _automation->AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element,
                TextEditChangeType.TextEditChangeType_CompositionFinalized, null, finalized);
            int? removeComposition = addComposition.Succeeded ? (int)_automation->RemoveTextEditTextChangedEventHandler(element, composition) : null;
            int? removeFinalized = addFinalized.Succeeded ? (int)_automation->RemoveTextEditTextChangedEventHandler(element, finalized) : null;
            return new(elementHResult, runtimeId, (int)patternHr, pattern is not null,
                (int)addComposition, (int)addFinalized, removeComposition, removeFinalized, activeHr, active);
        }
        finally
        {
            if (pattern is not null) pattern->Release();
            if (editPattern is not null) editPattern->Release();
            if (composition is not null) composition->Release();
            if (finalized is not null) finalized->Release();
        }
    }

    // Narrow seam for model-free native callback lifetime tests; no host text is accessed.
    internal nint CreateCallbackForTest() => (nint)Callback.Create(this, Interlocked.Read(ref _epoch));
    internal nint CreateRetainedCallbackForTest(long token) => (nint)Callback.CreateRetained(this, token);
    internal static void SignalCallbackForTest(nint callback) =>
        ((IUIAutomationTextEditTextChangedEventHandler*)callback)->HandleTextEditTextChangedEvent(null, TextEditChangeType.TextEditChangeType_Composition, null);

    // All UIA event registration and removal, including the managed focus handler, uses this
    // same owner thread. The caller does not hold the tracker lock while waiting.
    public bool Execute(Action action)
    {
        if (_disposed) return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() => { try { action(); completion.TrySetResult(true); } catch { completion.TrySetResult(false); } });
            return completion.Task.Wait(TimeSpan.FromMilliseconds(500)) && completion.Task.Result;
        }
        catch (InvalidOperationException) { return false; }
    }

    public (InputCompositionState State, long Version) Observe(int pid, string field, long focusEpoch, Func<bool>? allowed = null)
    {
        if (_disposed || string.IsNullOrEmpty(field)) return default;
        if (Interlocked.Increment(ref _pendingObservations) > 16)
        {
            Interlocked.Decrement(ref _pendingObservations);
            Invalidate();
            _failed();
            return default;
        }
        long requestedEpoch = Interlocked.Read(ref _epoch);
        var completion = new TaskCompletionSource<(InputCompositionState, long)>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = false;
        try
        {
            _queue.Add(() =>
            {
                try { completion.TrySetResult(requestedEpoch == Interlocked.Read(ref _epoch) && (allowed?.Invoke() ?? true) ? ObserveCore(pid, field, focusEpoch, allowed) : default); }
                catch { completion.TrySetResult(default); }
                finally { Interlocked.Decrement(ref _pendingObservations); }
            });
            queued = true;
            if (completion.Task.Wait(TimeSpan.FromMilliseconds(500))) return completion.Task.Result;
        }
        catch (InvalidOperationException) { }
        finally { if (!queued) Interlocked.Decrement(ref _pendingObservations); }
        // A timed-out operation may still complete; invalidate every receipt it could publish.
        Invalidate();
        _failed();
        return default;
    }

    public long Invalidate()
    {
        long epoch = Interlocked.Increment(ref _epoch);
        Interlocked.Increment(ref _version);
        return epoch;
    }

    public void RetireAsync(long expectedEpoch)
    {
        if (_disposed) return;
        long previous;
        do
        {
            previous = Interlocked.Read(ref _retirementEpoch);
            if (expectedEpoch <= previous) break;
        } while (Interlocked.CompareExchange(ref _retirementEpoch, expectedEpoch, previous) != previous);
        if (Interlocked.Exchange(ref _retirementQueued, 1) != 0) return;
        try { _queue.Add(() => { Interlocked.Exchange(ref _retirementQueued, 0); if (Interlocked.Read(ref _retirementEpoch) == Interlocked.Read(ref _epoch)) Retire(); }); }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _retirementQueued, 0); }
    }

    private (InputCompositionState, long) ObserveCore(int pid, string field, long focusEpoch, Func<bool>? allowed)
    {
        if (_automation is null) return default;
        if (_element is null || _pid != pid || _field != field || _focusEpoch != focusEpoch || _subscriptionEpoch != Interlocked.Read(ref _epoch))
        {
            Retire();
            if (!Open(pid, field, focusEpoch, allowed)) return default;
        }
        IUIAutomationElement* current = null;
        try
        {
            if (!(allowed?.Invoke() ?? true) || _automation->GetFocusedElement(&current).Failed || current is null || !(allowed?.Invoke() ?? true)) return default;
            Windows.Win32.Foundation.BOOL same = default;
            if (_automation->CompareElements(_element, current, &same).Failed || !same || !(allowed?.Invoke() ?? true)) return default;
            long before = Version;
            IUIAutomationTextRange* range = null;
            HRESULT hr = _pattern->GetActiveComposition(&range);
            bool composing = range is not null;
            if (range is not null) range->Release();
            long after = Version;
            if (hr.Failed || before != after || _subscriptionEpoch != Interlocked.Read(ref _epoch) || !(allowed?.Invoke() ?? true)) return default;
            return (composing ? InputCompositionState.Composing : InputCompositionState.Clear, after);
        }
        finally { if (current is not null) current->Release(); }
    }

    private bool Open(int pid, string field, long focusEpoch, Func<bool>? allowed)
    {
        IUIAutomationElement* element = null;
        IUnknown* raw = null;
        IUIAutomationTextEditPattern* pattern = null;
        IUIAutomationTextEditTextChangedEventHandler* composition = null;
        IUIAutomationTextEditTextChangedEventHandler* finalized = null;
        bool firstAdded = false, secondAdded = false;
        long epoch = Interlocked.Increment(ref _epoch);
        try
        {
            bool Allowed() => epoch == Interlocked.Read(ref _epoch) && (allowed?.Invoke() ?? true);
            if (!Allowed()) return false;
            if (_automation->GetFocusedElement(&element).Failed || element is null) return false;
            if (!Allowed()) return false;
            int actualPid = 0;
            if (element->get_CurrentProcessId(&actualPid).Failed || actualPid != pid || !Allowed() || RuntimeId(element) != field || !Allowed()) return false;
            if (element->GetCurrentPattern(UIA_PATTERN_ID.UIA_TextEditPatternId, &raw).Failed || raw is null) return false;
            if (!Allowed()) return false;
            Guid iid = IUIAutomationTextEditPattern.IID_Guid;
            if (raw->QueryInterface(&iid, (void**)&pattern).Failed || pattern is null) return false;
            if (!Allowed()) return false;
            composition = Callback.Create(this, epoch);
            finalized = Callback.Create(this, epoch);
            if (_automation->AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element,
                TextEditChangeType.TextEditChangeType_Composition, null, composition).Failed) return false;
            firstAdded = true;
            if (!Allowed()) return false;
            if (_automation->AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element,
                TextEditChangeType.TextEditChangeType_CompositionFinalized, null, finalized).Failed) return false;
            secondAdded = true;
            if (!Allowed()) return false;
            _element = element; element = null;
            _pattern = pattern; pattern = null;
            _composition = composition; composition = null;
            _finalized = finalized; finalized = null;
            _pid = pid; _field = field; _focusEpoch = focusEpoch;
            _subscriptionEpoch = epoch;
            Interlocked.Increment(ref _version);
            return true;
        }
        finally
        {
            if (element is not null)
            {
                Interlocked.Increment(ref _epoch);
                if (firstAdded) _automation->RemoveTextEditTextChangedEventHandler(element, composition);
                if (secondAdded) _automation->RemoveTextEditTextChangedEventHandler(element, finalized);
                element->Release();
            }
            if (raw is not null) raw->Release();
            if (pattern is not null) pattern->Release();
            if (composition is not null) composition->Release();
            if (finalized is not null) finalized->Release();
        }
    }

    private static string? RuntimeId(IUIAutomationElement* element)
    {
        SAFEARRAY* array = null;
        try
        {
            if (element->GetRuntimeId(&array).Failed || array is null) return null;
            int lower = 0, upper = -1;
            if (SpeechNativeMethods.SafeArrayGetLBound(array, 1, &lower).Failed ||
                SpeechNativeMethods.SafeArrayGetUBound(array, 1, &upper).Failed || upper < lower || upper - lower > 128) return null;
            void* data = null;
            if (SpeechNativeMethods.SafeArrayAccessData(array, &data).Failed || data is null) return null;
            try { return string.Join('.', new ReadOnlySpan<int>(data, upper - lower + 1).ToArray()); }
            finally { SpeechNativeMethods.SafeArrayUnaccessData(array); }
        }
        finally { if (array is not null) SpeechNativeMethods.SafeArrayDestroy(array); }
    }

    private void Retire()
    {
        Interlocked.Increment(ref _epoch);
        Interlocked.Increment(ref _version);
        if (_element is not null)
        {
            if (_composition is not null) _automation->RemoveTextEditTextChangedEventHandler(_element, _composition);
            if (_finalized is not null) _automation->RemoveTextEditTextChangedEventHandler(_element, _finalized);
            _element->Release(); _element = null;
        }
        if (_composition is not null) { _composition->Release(); _composition = null; }
        if (_finalized is not null) { _finalized->Release(); _finalized = null; }
        if (_pattern is not null) { _pattern->Release(); _pattern = null; }
        _field = null;
    }

    private void OnChanged(long epoch)
    {
        if (epoch != Interlocked.Read(ref _epoch)) return;
        Interlocked.Increment(ref _version);
        _changed();
    }

    private void Run()
    {
        HRESULT init = SpeechNativeMethods.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
        try
        {
            int? factoryResult = null;
            if (init.Succeeded)
            {
                Guid clsid = new("E22AD333-B25F-460C-83D0-0581107395C9"); // CUIAutomation8
                Guid iid = IUIAutomation3.IID_Guid;
                IUIAutomation3* automation = null;
                HRESULT factory = SpeechNativeMethods.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, (void**)&automation);
                factoryResult = (int)factory;
                if (factory.Succeeded)
                    _automation = automation;
            }
            _bootstrap.TrySetResult(new((int)init, factoryResult, _automation is not null));
            foreach (Action action in _queue.GetConsumingEnumerable()) action();
        }
        finally
        {
            if (_automation is not null) { RetireRetainedCore(); Retire(); _automation->Release(); _automation = null; }
            if (init.Succeeded) SpeechNativeMethods.CoUninitialize();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _retainedGeneration);
        Interlocked.Increment(ref _epoch);
        Interlocked.Increment(ref _version);
        _queue.CompleteAdding();
        _thread.Join();
        _queue.Dispose();
    }

    private sealed class Callback : IUIAutomationTextEditTextChangedEventHandler.Interface
    {
        private readonly UiaTextEditEvidence _owner;
        private readonly long _epoch;
        private readonly bool _retained;
        private Callback(UiaTextEditEvidence owner, long epoch, bool retained) { _owner = owner; _epoch = epoch; _retained = retained; }
        public static IUIAutomationTextEditTextChangedEventHandler* Create(UiaTextEditEvidence owner, long epoch) => CreateCore(owner, epoch, false);
        public static IUIAutomationTextEditTextChangedEventHandler* CreateRetained(UiaTextEditEvidence owner, long epoch) => CreateCore(owner, epoch, true);
        private static IUIAutomationTextEditTextChangedEventHandler* CreateCore(UiaTextEditEvidence owner, long epoch, bool retained)
        {
            IntPtr unknown = CallbackWrappers.Instance.GetOrCreateComInterfaceForObject(new Callback(owner, epoch, retained), CreateComInterfaceFlags.None);
            try
            {
                Guid iid = IUIAutomationTextEditTextChangedEventHandler.IID_Guid;
                if (Marshal.QueryInterface(unknown, in iid, out IntPtr result) < 0) throw new InvalidOperationException("UIA callback interface unavailable.");
                return (IUIAutomationTextEditTextChangedEventHandler*)result;
            }
            finally { Marshal.Release(unknown); }
        }
        public HRESULT HandleTextEditTextChangedEvent(IUIAutomationElement* sender, TextEditChangeType type, SAFEARRAY* payload)
        {
            if (_retained) _owner.OnRetainedChanged(_epoch); else _owner.OnChanged(_epoch);
            return (HRESULT)0;
        }
    }

    private sealed class RetainedEventCallback : IUIAutomationEventHandler.Interface
    {
        private readonly UiaTextEditEvidence _owner;
        private readonly long _token;
        private RetainedEventCallback(UiaTextEditEvidence owner, long token) { _owner = owner; _token = token; }
        public static IUIAutomationEventHandler* Create(UiaTextEditEvidence owner, long token)
        {
            IntPtr unknown = CallbackWrappers.Instance.GetOrCreateComInterfaceForObject(new RetainedEventCallback(owner, token), CreateComInterfaceFlags.None);
            try
            {
                Guid iid = IUIAutomationEventHandler.IID_Guid;
                if (Marshal.QueryInterface(unknown, in iid, out IntPtr result) < 0) throw new InvalidOperationException("UIA event callback interface unavailable.");
                return (IUIAutomationEventHandler*)result;
            }
            finally { Marshal.Release(unknown); }
        }
        public HRESULT HandleAutomationEvent(IUIAutomationElement* sender, UIA_EVENT_ID eventId)
        {
            _owner.OnRetainedChanged(_token);
            return (HRESULT)0;
        }
    }

    private sealed class CallbackWrappers : ComWrappers
    {
        public static readonly CallbackWrappers Instance = new();
        private static readonly ComInterfaceEntry* Entries = CreateEntries();
        private static readonly ComInterfaceEntry* EventEntries = CreateEventEntries();
        private static ComInterfaceEntry* CreateEntries()
        {
            var entries = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(CallbackWrappers), sizeof(ComInterfaceEntry));
            entries->IID = IUIAutomationTextEditTextChangedEventHandler.IID_Guid;
            entries->Vtable = (nint)GetVtable<IUIAutomationTextEditTextChangedEventHandler>();
            return entries;
        }
        private static ComInterfaceEntry* CreateEventEntries()
        {
            var entries = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(RetainedEventCallback), sizeof(ComInterfaceEntry));
            entries->IID = IUIAutomationEventHandler.IID_Guid;
            entries->Vtable = (nint)GetVtable<IUIAutomationEventHandler>();
            return entries;
        }
        private static Windows.Win32.System.Com.IUnknown.Vtbl* GetVtable<T>() where T : unmanaged, IVTable => T.VTable;
        protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        { count = 1; return obj is RetainedEventCallback ? EventEntries : Entries; }
        protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) => throw new NotSupportedException();
        protected override void ReleaseObjects(System.Collections.IEnumerable objects) => throw new NotSupportedException();
    }
}
