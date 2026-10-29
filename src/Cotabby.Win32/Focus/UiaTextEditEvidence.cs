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

// Owns all native TextEdit subscriptions on one MTA. COM retains each callback until its last
// native reference is released; callbacks carry only an epoch, never inspect the event payload.
[SupportedOSPlatform("windows8.1")]
internal sealed unsafe class UiaTextEditEvidence : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly Action _changed;
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
    private volatile bool _disposed;

    public UiaTextEditEvidence(Action changed)
    {
        _changed = changed;
        _thread = new Thread(Run) { IsBackground = true, Name = "Lingrove UIA TextEdit" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public long Version => Interlocked.Read(ref _version);

    // Narrow seam for model-free native callback lifetime tests; no host text is accessed.
    internal nint CreateCallbackForTest() => (nint)Callback.Create(this, Interlocked.Read(ref _epoch));
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

    public (InputCompositionState State, long Version) Observe(int pid, string field, long focusEpoch)
    {
        if (_disposed || string.IsNullOrEmpty(field)) return default;
        if (Interlocked.Increment(ref _pendingObservations) > 16)
        {
            Interlocked.Decrement(ref _pendingObservations);
            Invalidate();
            _changed();
            return default;
        }
        long requestedEpoch = Interlocked.Read(ref _epoch);
        var completion = new TaskCompletionSource<(InputCompositionState, long)>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = false;
        try
        {
            _queue.Add(() =>
            {
                try { completion.TrySetResult(requestedEpoch == Interlocked.Read(ref _epoch) ? ObserveCore(pid, field, focusEpoch) : default); }
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
        _changed();
        return default;
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _epoch);
        Interlocked.Increment(ref _version);
    }

    public void RetireAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _retirementQueued, 1) != 0) return;
        try { _queue.Add(() => { Interlocked.Exchange(ref _retirementQueued, 0); Retire(); }); }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _retirementQueued, 0); }
    }

    private (InputCompositionState, long) ObserveCore(int pid, string field, long focusEpoch)
    {
        if (_automation is null) return default;
        if (_element is null || _pid != pid || _field != field || _focusEpoch != focusEpoch || _subscriptionEpoch != Interlocked.Read(ref _epoch))
        {
            Retire();
            if (!Open(pid, field, focusEpoch)) return default;
        }
        IUIAutomationElement* current = null;
        try
        {
            if (_automation->GetFocusedElement(&current).Failed || current is null) return default;
            Windows.Win32.Foundation.BOOL same = default;
            if (_automation->CompareElements(_element, current, &same).Failed || !same) return default;
            long before = Version;
            IUIAutomationTextRange* range = null;
            HRESULT hr = _pattern->GetActiveComposition(&range);
            bool composing = range is not null;
            if (range is not null) range->Release();
            long after = Version;
            if (hr.Failed || before != after || _subscriptionEpoch != Interlocked.Read(ref _epoch)) return default;
            return (composing ? InputCompositionState.Composing : InputCompositionState.Clear, after);
        }
        finally { if (current is not null) current->Release(); }
    }

    private bool Open(int pid, string field, long focusEpoch)
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
            if (_automation->GetFocusedElement(&element).Failed || element is null) return false;
            int actualPid = 0;
            if (element->get_CurrentProcessId(&actualPid).Failed || actualPid != pid || RuntimeId(element) != field) return false;
            if (element->GetCurrentPattern(UIA_PATTERN_ID.UIA_TextEditPatternId, &raw).Failed || raw is null) return false;
            Guid iid = IUIAutomationTextEditPattern.IID_Guid;
            if (raw->QueryInterface(&iid, (void**)&pattern).Failed || pattern is null) return false;
            composition = Callback.Create(this, epoch);
            finalized = Callback.Create(this, epoch);
            if (_automation->AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element,
                TextEditChangeType.TextEditChangeType_Composition, null, composition).Failed) return false;
            firstAdded = true;
            if (_automation->AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element,
                TextEditChangeType.TextEditChangeType_CompositionFinalized, null, finalized).Failed) return false;
            secondAdded = true;
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
            if (init.Succeeded)
            {
                Guid clsid = new("FF48DBA4-60EF-4201-AA87-54103EEF594E"); // CUIAutomation
                Guid iid = IUIAutomation3.IID_Guid;
                IUIAutomation3* automation = null;
                if (SpeechNativeMethods.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, (void**)&automation).Succeeded)
                    _automation = automation;
            }
            foreach (Action action in _queue.GetConsumingEnumerable()) action();
        }
        finally
        {
            if (_automation is not null) { Retire(); _automation->Release(); _automation = null; }
            if (init.Succeeded) SpeechNativeMethods.CoUninitialize();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
        private Callback(UiaTextEditEvidence owner, long epoch) { _owner = owner; _epoch = epoch; }
        public static IUIAutomationTextEditTextChangedEventHandler* Create(UiaTextEditEvidence owner, long epoch)
        {
            IntPtr unknown = CallbackWrappers.Instance.GetOrCreateComInterfaceForObject(new Callback(owner, epoch), CreateComInterfaceFlags.None);
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
            _owner.OnChanged(_epoch);
            return (HRESULT)0;
        }
    }

    private sealed class CallbackWrappers : ComWrappers
    {
        public static readonly CallbackWrappers Instance = new();
        private static readonly ComInterfaceEntry* Entries = CreateEntries();
        private static ComInterfaceEntry* CreateEntries()
        {
            var entries = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(CallbackWrappers), sizeof(ComInterfaceEntry));
            entries->IID = IUIAutomationTextEditTextChangedEventHandler.IID_Guid;
            entries->Vtable = (nint)GetVtable<IUIAutomationTextEditTextChangedEventHandler>();
            return entries;
        }
        private static Windows.Win32.System.Com.IUnknown.Vtbl* GetVtable<T>() where T : unmanaged, IVTable => T.VTable;
        protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count) { count = 1; return Entries; }
        protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) => throw new NotSupportedException();
        protected override void ReleaseObjects(System.Collections.IEnumerable objects) => throw new NotSupportedException();
    }
}
