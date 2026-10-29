using System.IO;
using System.Runtime.InteropServices;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Win32.CLI;

/// <summary>App-owned private mailbox. Directory events wake one serialized consumer; one
/// identity-verified byte-range lease owns consumption. Stop drains a claimed request and its
/// acknowledgement before releasing the lease. The handler borrows the existing canonical Store.</summary>
public sealed class CliMailboxServer : IAsyncDisposable
{
    private readonly string _path;
    private readonly Func<CliRequest, Task<CliReply>> _handler;
    private readonly object _gate = new();
    private CliMailboxDirectory? _root, _requests, _working, _replies;
    private VocabularyWindowsFile? _lease;
    private FileSystemWatcher? _watcher;
    private Timer? _recovery;
    private Task? _work, _stop;
    private bool _stopping, _dirty, _started, _leaseHeld;
    public CliMailboxServer(string root, Func<CliRequest, Task<CliReply>> handler) { _path = root; _handler = handler ?? throw new ArgumentNullException(nameof(handler)); }
    public Task StartAsync()
    {
        lock (_gate)
        {
            if (_started || _stopping) throw new InvalidOperationException("CLI server is already started or stopping.");
            try
            {
                _root = new(_path); _requests = new(System.IO.Path.Combine(_path, "requests")); _working = new(System.IO.Path.Combine(_path, "working")); _replies = new(System.IO.Path.Combine(_path, "replies"));
                _lease = VocabularyWindowsFile.Open(System.IO.Path.Combine(_path, "server.lock"), createLock: true); _lease.Verify();
                var position = new VocabularyWindowsFile.LockOverlapped();
                if (!VocabularyWindowsFile.Native.LockFileEx(_lease.Handle, 3, 0, 1, 0, ref position))
                    throw new CliException(Marshal.GetLastPInvokeError() == 33 ? CliErrorCode.Busy : CliErrorCode.Io, "Another instance owns the CLI mailbox.");
                _leaseHeld = true;
                _lease.Verify();
                while (true)
                {
                    var interrupted = _working.Names(); if (interrupted.Count == 0) break;
                    bool requeued = false;
                    foreach (string name in interrupted)
                    {
                        if (_requests.Exists(name)) { _working.Remove(name); requeued = true; }
                        else requeued |= _working.Claim(name, _requests);
                    }
                    if (!requeued) throw new CliException(CliErrorCode.Io, "Cannot recover the CLI mailbox.");
                }
                StartWatcher();
                _started = true;
                ScheduleLocked(); return Task.CompletedTask;
            }
            catch { Cleanup(); throw; }
        }
    }
    private void Event(object sender, FileSystemEventArgs e) => Schedule();
    private void RenameEvent(object sender, RenamedEventArgs e) => Schedule();
    private void StartWatcher()
    {
        var watcher = new FileSystemWatcher(_requests!.Path) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, IncludeSubdirectories = false, Filter = "*.json" };
        try
        {
            watcher.Created += Event; watcher.Changed += Event; watcher.Renamed += RenameEvent; watcher.Error += ErrorEvent;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch { watcher.Dispose(); throw; }
    }
    private void ErrorEvent(object sender, ErrorEventArgs e)
    {
        lock (_gate)
        {
            if (!_started || _stopping || !ReferenceEquals(sender, _watcher)) return;
            _watcher.Dispose(); _watcher = null;
            _recovery ??= new Timer(RecoveryTick, null, Timeout.Infinite, Timeout.Infinite);
            _recovery.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            ScheduleLocked();
        }
    }
    // Recover after a lost notification, including watcher-buffer overflow. No timer or
    // periodic disk scan runs during normal operation.
    private void RecoveryTick(object? state)
    {
        lock (_gate)
        {
            if (!_started || _stopping) return;
            if (_watcher is null)
            {
                try { StartWatcher(); _recovery?.Dispose(); _recovery = null; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { _recovery?.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan); }
            }
            ScheduleLocked();
        }
    }
    private void Schedule() { lock (_gate) { if (!_started || _stopping) return; ScheduleLocked(); } }
    private void ScheduleLocked()
    {
        _dirty = true; if (_work is not null) return;
        _work = Task.Run(DrainAsync);
    }
    private async Task DrainAsync()
    {
        try
        {
            while (true)
            {
                lock (_gate) { if (_stopping) return; _dirty = false; }
                var requests = _requests!; var working = _working!; var replies = _replies!;
                bool progressed = false;
                foreach (string name in requests.Names())
                {
                    lock (_gate) if (_stopping) return;
                    VocabularyWindowsFile.FileIdentity? claimed;
                    try { claimed = requests.ClaimOwned(name, working); if (claimed is null) continue; }
                    catch (Exception e) when (e is CliException or IOException or UnauthorizedAccessException or VocabularyException) { continue; }
                    progressed = true;
                    Guid id = Guid.ParseExact(name[..36], "D"); CliReply reply;
                    try
                    {
                        byte[] bytes = working.Read(name, expected: claimed) ?? throw new CliException(CliErrorCode.Io, "Cannot read a CLI request.");
                        var request = CliWire.DecodeRequest(bytes);
                        if (request.Id != id || request.Version != 1) throw new CliException(CliErrorCode.InvalidInput, "Request identity or protocol version does not match.");
                        reply = await _handler(request).ConfigureAwait(false);
                        if (reply.RequestId != id || reply.Version != 1) throw new CliException(CliErrorCode.Io, "CLI response identity mismatch.");
                    }
                    catch (CliException e) { reply = CliWire.Failure(id, e); }
                    catch (Exception) { reply = CliWire.Failure(id, new(CliErrorCode.Io, "Local CLI operation failed.")); }
                    try
                    {
                        byte[] bytes;
                        try { bytes = CliWire.Encode(reply); } catch (CliException) { bytes = CliWire.Encode(CliWire.Failure(id, new(CliErrorCode.InvalidInput, "Response exceeds 32 MiB. Use the app's export for this entry."))); }
                        if (!replies.Exists(name)) replies.Publish(name, bytes);
                        working.RemoveOwned(name, claimed.Value);
                    }
                    catch (Exception e) when (e is CliException or IOException or UnauthorizedAccessException or VocabularyException) { /* Retain claimed input for restart. */ }
                }
                foreach (string name in replies.Names()) { try { replies.RemoveIfOlderThan(name, DateTime.UtcNow.AddDays(-1)); } catch (Exception e) when (e is CliException or IOException or UnauthorizedAccessException or VocabularyException) { } }
                lock (_gate) if ((!_dirty && (!progressed || requests.Names().Count == 0)) || _stopping) return;
            }
        }
        finally
        {
            lock (_gate) { _work = null; if (_dirty && !_stopping) ScheduleLocked(); }
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_stop is not null) return new(_stop); _stopping = true; _recovery?.Dispose(); _recovery = null; _watcher?.Dispose(); _watcher = null;
            var pending = _work; _stop = Finish(pending); return new(_stop);
        }
    }
    private async Task Finish(Task? pending) { try { if (pending is not null) await pending.ConfigureAwait(false); } finally { lock (_gate) Cleanup(); } }
    private void Cleanup()
    {
        _recovery?.Dispose(); _recovery = null; _watcher?.Dispose(); _watcher = null;
        if (_lease is not null) { if (_leaseHeld) { var position = new VocabularyWindowsFile.LockOverlapped(); _ = VocabularyWindowsFile.Native.UnlockFileEx(_lease.Handle, 0, 1, 0, ref position); } _leaseHeld = false; _lease.Dispose(); _lease = null; }
        _replies?.Dispose(); _replies = null; _working?.Dispose(); _working = null; _requests?.Dispose(); _requests = null; _root?.Dispose(); _root = null; _started = false;
    }
}
