using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Clipboard;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// App-lifetime single-flight owner of the sibling clipboard reader. A blocked foreign renderer
/// cannot strand the UI or Store shutdown: cancellation kills our own extraction process, and
/// only detached bounded bytes can subsequently enter the archive. This service changes no
/// clipboard contents, loads no model and is not a security sandbox. Composition owns/drains it.
/// </summary>
public sealed class WindowsVocabularyClipboardTransfer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _work = [];
    private bool _busy;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<uint, ProcessStartInfo> _start;
    private readonly TimeSpan _timeout;
    private Task? _disposal;
    public WindowsVocabularyClipboardTransfer() : this(sequence =>
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Cotabby.ImageTransferHost.exe"));
        start.ArgumentList.Add("--clipboard"); start.ArgumentList.Add(sequence.ToString(CultureInfo.InvariantCulture)); return start;
    }, TimeSpan.FromSeconds(30)) { }
    /// <summary>Tests launch actual fixture processes with bounded protocol output, not a clipboard.</summary>
    internal WindowsVocabularyClipboardTransfer(Func<uint, ProcessStartInfo> start, TimeSpan timeout)
    { _start = start; _timeout = timeout; }
    public static uint SequenceNumber => ClipboardNativeMethods.GetClipboardSequenceNumber();
    public Task<byte[]?> ReadAsync(uint expectedSequence, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_disposal is not null) return Task.FromException<byte[]?>(new ObjectDisposedException(nameof(WindowsVocabularyClipboardTransfer)));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<byte[]?>(cancellationToken);
            if (_busy || expectedSequence == 0) return Task.FromResult<byte[]?>(null);
            // Decline before scheduling: even a burst of callers owns at most one worker/process.
            _busy = true;
            var task = Task.Run(() => ReadCoreAsync(expectedSequence, cancellationToken)); _work.Add(task);
            _ = task.ContinueWith(completed => { lock (_gate) _work.Remove(completed); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default); return task;
        }
    }
    private async Task<byte[]?> ReadCoreAsync(uint sequence, CancellationToken caller)
    {
        using var timeout = new CancellationTokenSource(_timeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token, timeout.Token); var token = operation.Token;
        Process? process = null; Task? errors = null; Task<byte[]?>? reading = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var start = _start(sequence); start.UseShellExecute = false; start.CreateNoWindow = true;
            start.RedirectStandardOutput = true; start.RedirectStandardError = true; start.RedirectStandardInput = false;
            process = Process.Start(start) ?? throw Invalid(); errors = DrainErrorsAsync(process.StandardError.BaseStream, token);
            reading = VocabularyImageTransferProtocol.ReadAsync(process.StandardOutput.BaseStream, token);
            if (await Task.WhenAny(reading, errors).ConfigureAwait(false) == errors) await errors.ConfigureAwait(false);
            byte[]? result = await reading.ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false); await errors.ConfigureAwait(false);
            if (process.ExitCode != 0) throw Invalid(); return result;
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested || _lifetime.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or Win32Exception or UnauthorizedAccessException or OperationCanceledException)
        { throw Invalid(); }
        finally
        {
            if (process is not null)
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: false); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception or TimeoutException) { }
                process.Dispose();
            }
            if (errors is not null) _ = errors.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (reading is not null) _ = reading.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            lock (_gate) _busy = false;
        }
    }
    private static async Task DrainErrorsAsync(Stream input, CancellationToken token)
    {
        byte[] buffer = new byte[4096]; int total = 0, count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        { total += count; if (total > 32768) throw new InvalidDataException(); }
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_disposal ??= Drain(_work.ToArray()));
        async Task Drain(Task[] work)
        {
            _lifetime.Cancel(); try { await Task.WhenAll(work).ConfigureAwait(false); } catch { }
            _lifetime.Dispose();
        }
    }
}
