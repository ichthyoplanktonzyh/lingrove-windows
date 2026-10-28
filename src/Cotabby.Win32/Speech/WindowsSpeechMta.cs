using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Cotabby.Win32.Speech;

/// <summary>One native codec operation owns its COM apartment through complete synchronous
/// activation, reads and release. Its caller awaits both the result and the actual thread's exit.
/// No async continuation carries COM references to another thread. Cancellation is checked by
/// bounded reads; it never cancels only a waiter and leaves native work behind. The PCM output
/// device has its own long-lived owner, independent of these short-lived decoding operations.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsSpeechMta
{
    internal static async Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            T value = default!; Exception? failure = null; bool initialized = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0)); initialized = true;
                value = operation(); cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception error) { failure = error; }
            finally { if (initialized) CoUninitialize(); }
            if (failure is OperationCanceledException cancelled) result.TrySetCanceled(cancelled.CancellationToken);
            else if (failure is not null) result.TrySetException(failure);
            else result.TrySetResult(value);
        }) { IsBackground = true, Name = "Lingrove speech codec" };
        thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        try { return await result.Task.ConfigureAwait(false); }
        finally { await Task.Run(thread.Join).ConfigureAwait(false); }
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint options);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
