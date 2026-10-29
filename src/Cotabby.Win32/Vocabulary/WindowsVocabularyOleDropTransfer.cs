using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Cotabby.Win32.Vocabulary;

/// <summary>One editor-owned helper process, not an OLE object, crosses the parent boundary.
/// OLE runs only in that process; cancellation closes its kill-on-close job and joins the child.</summary>
public sealed class WindowsVocabularyOleDropTransfer : IAsyncDisposable
{
    private readonly Process _process;
    private readonly WindowsVocabularyDropJob _job;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly TaskCompletionSource _dropStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task<byte[]?> _read;
    private Task? _retirement;
    public IntPtr Window { get; }
    internal int ProcessId { get; }
    public Task<byte[]?> Result => _read;
    public Task DropStarted => _dropStarted.Task;
    internal string? TestTarget { get; private set; }
    private WindowsVocabularyOleDropTransfer(Process process, WindowsVocabularyDropJob job, IntPtr window)
    {
        _process = process; _job = job; Window = window; ProcessId = process.Id;
        // Even COM argument unmarshalling can stall before our DROP marker. Recycle the
        // isolated receiver on a bounded lifetime, including while it is idle.
        _lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        _read = ReadDropAsync(process.StandardOutput.BaseStream, _lifetime.Token);
    }
    public static async Task<WindowsVocabularyOleDropTransfer> StartAsync(IntPtr parent, CancellationToken token, string label = "Drop image here")
        => await StartCoreAsync(parent, token, label, false).ConfigureAwait(false);
    internal static Task<WindowsVocabularyOleDropTransfer> StartForTestAsync(IntPtr parent, CancellationToken token)
        => StartCoreAsync(parent, token, "Drop image here", true);
    private static async Task<WindowsVocabularyOleDropTransfer> StartCoreAsync(IntPtr parent, CancellationToken token, string label, bool test)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 120 || label.Any(char.IsControl)) throw new ArgumentException("Invalid drop label.", nameof(label));
        if (!IsWindow(parent) || GetWindowThreadProcessId(parent, out uint pid) == 0 || pid != Environment.ProcessId) throw new InvalidDataException();
        var job = new WindowsVocabularyDropJob(); Process? process = null;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Cotabby.ImageTransferHost.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = false };
            start.ArgumentList.Add(test ? "--ole-drop-test" : "--ole-drop"); start.ArgumentList.Add(parent.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!test) start.ArgumentList.Add(label);
            process = Process.Start(start) ?? throw new InvalidDataException();
            if (!AssignProcessToJobObject(job.Handle, process.Handle)) throw new InvalidDataException();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await process.StandardInput.WriteLineAsync("GO").WaitAsync(timeout.Token).ConfigureAwait(false); await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
            string line = await ReadLineAsync(process.StandardOutput.BaseStream, timeout.Token).ConfigureAwait(false);
            if (!line.StartsWith("READY ", StringComparison.Ordinal) || !long.TryParse(line.AsSpan(6), out long value) || value <= 0) throw new InvalidDataException();
            IntPtr child = new(value);
            if (!IsWindow(child) || GetParent(child) != parent || GetWindowThreadProcessId(child, out uint owner) == 0 || owner != process.Id) throw new InvalidDataException();
            string? testTarget = null;
            if (test)
            {
                string target = await ReadLineAsync(process.StandardOutput.BaseStream, timeout.Token, 65536).ConfigureAwait(false);
                if (!target.StartsWith("TARGET ", StringComparison.Ordinal)) throw new InvalidDataException(); testTarget = target[7..];
            }
            return new(process, job, child) { TestTarget = testTarget };
        }
        catch (Exception original)
        {
            job.Dispose();
            if (process is not null)
            {
                try
                {
                    try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) when (process.HasExited) { }
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
                finally { process.Dispose(); }
            }
            throw;
        }
    }
    public void Position(int x, int y, int width, int height, bool show)
    {
        // The helper's GUI thread can be blocked in a foreign provider. Never synchronously
        // wait for that thread from the editor's dispatcher.
        lock (_gate) if (_retirement is not null) return;
        try
        {
            if (!_process.HasExited && IsWindow(Window) && GetWindowThreadProcessId(Window, out uint pid) != 0 && pid == _process.Id)
                _ = SetWindowPos(Window, IntPtr.Zero, x, y, width, height, 0x4000 | 0x0010 | 0x0004 | (show ? 0x0040u : 0x0080u));
        }
        catch (InvalidOperationException) { /* Retirement can close the process between checks. */ }
    }
    private async Task<byte[]?> ReadDropAsync(Stream input, CancellationToken token)
    {
        try
        {
            if (await ReadLineAsync(input, token).ConfigureAwait(false) != "DROP") throw new InvalidDataException();
            _dropStarted.TrySetResult();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            return await VocabularyImageTransferProtocol.ReadAsync(input, timeout.Token).ConfigureAwait(false);
        }
        catch { _job.Dispose(); throw; }
    }
    private static async Task<string> ReadLineAsync(Stream input, CancellationToken token, int limit = 80)
    {
        byte[] one = new byte[1], data = new byte[limit]; int length = 0;
        while (length < data.Length)
        {
            await input.ReadExactlyAsync(one, token).ConfigureAwait(false);
            if (one[0] == '\n') return System.Text.Encoding.ASCII.GetString(data, 0, length);
            if (one[0] < 32 || one[0] > 126) throw new InvalidDataException(); data[length++] = one[0];
        }
        throw new InvalidDataException();
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource done;
        lock (_gate)
        {
            if (_retirement is not null) return new(_retirement);
            done = new(TaskCreationOptions.RunContinuationsAsynchronously); _retirement = done.Task;
        }
        _ = RetireAsync(done); return new(done.Task);
    }
    private async Task RetireAsync(TaskCompletionSource done)
    {
        Exception? failure = null;
        try
        {
            _lifetime.Cancel(); _job.Dispose();
            try { if (!_process.HasExited) _process.Kill(); }
            catch (InvalidOperationException) when (_process.HasExited) { }
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            try { await _read.ConfigureAwait(false); } catch { }
        }
        catch (Exception error) { failure = error; }
        finally { _job.Dispose(); _process.Dispose(); _lifetime.Dispose(); }
        if (failure is null) done.TrySetResult(); else done.TrySetException(failure);
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeHandle job, IntPtr process);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
