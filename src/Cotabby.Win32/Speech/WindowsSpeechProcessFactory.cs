using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Speech;

/// <summary>The independent speech runtime borrows this Windows launch boundary. Binary pipes
/// and a NUL diagnostic sink exist before CreateProcess; an explicit inherited-handle list and
/// atomic job assignment prevent unrelated handle inheritance and orphaned model processes.
/// Startup/termination run on workers and never invoke a shell or steal application focus.</summary>
public sealed class WindowsSpeechProcessFactory : ISpeechHelperProcessFactory
{
    public Task<ISpeechHelperProcess> StartAsync(string executable, SpeechLocalConfiguration configuration, CancellationToken cancellationToken) =>
        Task.Run<ISpeechHelperProcess>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            WindowsSpeechJob? job = null; FileStream? input = null, output = null;
            SafeFileHandle? parentInput = null, parentOutput = null;
            SafeProcessHandle? process = null;
            try
            {
                job = new WindowsSpeechJob();
                var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = 0 };
                if (!CreatePipe(out var sourceInput, out parentInput, ref security, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (sourceInput)
                {
                    if (!CreatePipe(out parentOutput, out var sourceOutput, ref security, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    using (sourceOutput)
                    using (var sourceErrors = CreateFileW("NUL", 0x40000000, 3, ref security, 3, 0x80, IntPtr.Zero))
                    {
                        if (sourceErrors.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                        // Parent endpoints are never inheritable, even briefly. Only the three
                        // duplicated child endpoints become inheritable for CreateProcess's list.
                        using var childInput = InheritableCopy(sourceInput);
                        using var childOutput = InheritableCopy(sourceOutput);
                        using var errors = InheritableCopy(sourceErrors);
                        var inputHandle = parentInput;
                        input = new FileStream(parentInput, FileAccess.Write, 4096, isAsync: false); parentInput = null;
                        output = new FileStream(parentOutput, FileAccess.Read, 4096, isAsync: false); parentOutput = null;
                        using var attributes = new WindowsSpeechStartupAttributes(job.Handle, childInput, childOutput, errors);
                        var startup = new StartupInfoEx
                        {
                            Info = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                                Input = childInput.DangerousGetHandle(), Output = childOutput.DangerousGetHandle(), Error = errors.DangerousGetHandle() },
                            Attributes = attributes.Pointer,
                        };
                        string program = Path.GetFullPath(executable);
                        string[] arguments = [program, "--model", configuration.ModelUrl.LocalPath, "--mmproj", configuration.ProjectionUrl.LocalPath, "--parent-pid", unchecked((uint)Environment.ProcessId).ToString(System.Globalization.CultureInfo.InvariantCulture)];
                        var command = new StringBuilder(string.Join(" ", arguments.Select(Quote)));
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!CreateProcessW(program, command, IntPtr.Zero, IntPtr.Zero, true, 0x08080000, IntPtr.Zero, AppContext.BaseDirectory, ref startup, out var information))
                            throw new Win32Exception(Marshal.GetLastWin32Error()); // CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT
                        process = new SafeProcessHandle(information.Process, ownsHandle: true);
                        using var primaryThread = new SafeWaitHandle(information.Thread, ownsHandle: true);
                        var owned = new OwnedProcess(process, job, input, inputHandle, output, unchecked((int)information.ProcessId));
                        process = null; job = null; input = null; output = null;
                        return owned; // A cancellation racing successful creation returns an owned child for retirement.
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { throw new SpeechException(SpeechFailure.RuntimeUnavailable); }
            finally
            {
                // Before successful ownership transfer, closing the job terminates any created
                // child. Attribute creation failures occur before CreateProcess can run a model.
                job?.Dispose();
                if (process is not null) { _ = TerminateProcess(process, 1); _ = WaitForSingleObject(process, 3000); process.Dispose(); }
                input?.Dispose(); output?.Dispose(); parentInput?.Dispose(); parentOutput?.Dispose();
            }
        });

    private static SafeFileHandle InheritableCopy(SafeFileHandle source)
    {
        var current = new IntPtr(-1); // GetCurrentProcess pseudo-handle; never close it.
        if (DuplicateHandle(current, source, current, out var copy, 0, true, 2)) return copy; // DUPLICATE_SAME_ACCESS
        int error = Marshal.GetLastWin32Error(); copy?.Dispose(); throw new Win32Exception(error);
    }

    /// <summary>MSVC argv quoting retains literal quotes/backslashes without shell expansion.
    /// The program and each model path remain separate arguments even with spaces or Unicode.</summary>
    private static string Quote(string argument)
    {
        if (argument.Contains('\0')) throw new ArgumentException("A helper argument contains a null character.");
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char value in argument)
        {
            if (value == '\\') { slashes++; continue; }
            result.Append('\\', value == '"' ? slashes * 2 + 1 : slashes); result.Append(value); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private sealed class OwnedProcess(SafeProcessHandle process, WindowsSpeechJob job, FileStream input, SafeFileHandle inputHandle, FileStream output, int id) : ISpeechHelperProcess
    {
        private readonly object _gate = new();
        private Task<bool>? _stop;
        private bool _disposed;
        public int Id => id;
        public Stream Input => input;
        public Stream Output => output;
        public bool HasExited { get { lock (_gate) return _disposed || WaitForSingleObject(process, 0) == 0; } }
        public Task<bool> StopAndReapAsync()
        {
            lock (_gate)
            {
                if (_disposed) return Task.FromResult(true);
                if (_stop is not null) return _stop;
                var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<bool> work = Task.FromResult(false); work = ReapAsync(); _stop = work; admitted.SetResult(); return work;
                async Task<bool> ReapAsync()
                {
                    await admitted.Task.ConfigureAwait(false);
                    bool stopped = await Task.Run(() =>
                    {
                        if (WaitForSingleObject(process, 0) == 0) return true;
                        // Windows has no SIGTERM equivalent for a detached console helper.
                        // Close stdin without flushing buffered work so its request loop can
                        // observe EOF; allow the source's one-second graceful stage before kill.
                        // Keep this handle separately: FileStream.SafeFileHandle's getter flushes.
                        inputHandle.Dispose();
                        if (WaitForSingleObject(process, 1000) == 0) return true;
                        _ = TerminateProcess(process, 1);
                        return WaitForSingleObject(process, 2000) == 0;
                    }).ConfigureAwait(false);
                    if (!stopped) { lock (_gate) { if (_stop == work) _stop = null; } }
                    return stopped;
                }
            }
        }
        public async ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (WaitForSingleObject(process, 0) != 0) throw new InvalidOperationException("Reap the speech helper before releasing its handles.");
                _disposed = true;
            }
            job.Dispose(); // Close any descendant's remaining pipe writer before closing the parent's streams.
            var errors = new List<Exception>();
            try { await input.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or ObjectDisposedException) { /* Stdin was deliberately closed before reaping; abandon its buffered write. */ }
            catch (Exception error) { errors.Add(error); }
            try { await output.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            finally { process.Dispose(); }
            if (errors.Count > 0) throw new AggregateException(errors);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { internal int Length; internal IntPtr Descriptor; internal int Inherit; }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        internal int Size;
        internal IntPtr Reserved, Desktop, Title;
        internal uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        internal ushort ShowWindow, ReservedSize;
        internal IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { internal StartupInfo Info; internal IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { internal IntPtr Process, Thread; internal uint ProcessId, ThreadId; }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes security, uint size);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess, out SafeFileHandle target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, ref SecurityAttributes security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation information);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
}
