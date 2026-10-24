using System.Runtime.InteropServices;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Speech;

/// <summary>The local runtime subscribes once on first use. Kernel resource events replace Mac
/// Dispatch memory-pressure notifications; a dedicated blocking wait consumes no polling CPU.
/// One low-memory notification requests an idle release, then high memory rearms the transition.</summary>
public sealed class WindowsSpeechMemoryPressure : ISpeechMemoryPressureSource
{
    public IDisposable Observe(Action warning) => new Binding(warning);

    /// <summary>The runtime owns this binding and disposes it away from UI. Its callback must
    /// queue work and return promptly, so cancellation can join the wait thread before closing
    /// any handle it might still be using. Callback/wait errors are reported at disposal.</summary>
    private sealed class Binding : IDisposable
    {
        private readonly ResourceWait _low, _high;
        private readonly EventWaitHandle _stop = new(false, EventResetMode.ManualReset);
        private readonly Thread _thread;
        private Exception? _error;
        private bool _started, _disposed;
        private readonly object _gate = new();
        internal Binding(Action warning)
        {
            try { _low = new(0); } catch { _stop.Dispose(); throw; }
            try { _high = new(1); }
            catch { _low.Dispose(); _stop.Dispose(); throw; }
            _thread = new(() =>
            {
                try
                {
                    WaitHandle[] low = [_stop, _low], high = [_stop, _high];
                    while (WaitHandle.WaitAny(low) == 1)
                    {
                        warning();
                        // Resource notification objects stay signaled while their condition holds.
                        // Waiting again on low would spin. Wait for the high transition instead.
                        if (WaitHandle.WaitAny(high) == 0) return;
                    }
                }
                catch (Exception error) { _error = error; }
            }) { IsBackground = true, Name = "Lingrove speech memory pressure" };
            try { _thread.Start(); _started = true; }
            catch { _low.Dispose(); _high.Dispose(); _stop.Dispose(); throw; }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _stop.Set();
                if (_started) _thread.Join();
                _disposed = true; _low.Dispose(); _high.Dispose(); _stop.Dispose();
                if (_error is not null) throw new AggregateException(_error);
            }
        }
    }
    private sealed class ResourceWait : WaitHandle
    {
        internal ResourceWait(int condition)
        {
            SafeWaitHandle = CreateMemoryResourceNotification(condition);
            if (SafeWaitHandle.IsInvalid) { Dispose(); throw new SpeechException(SpeechFailure.RuntimeUnavailable); }
        }
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateMemoryResourceNotification(int condition);
}
