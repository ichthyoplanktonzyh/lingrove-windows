using System.Runtime.InteropServices;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Database-owned cross-process lock also spans future media operations. Each database access
/// owns one shared/exclusive byte-range lease; reentrancy/upgrade policy remains in the database's
/// synchronous gate. Windows file identity is checked on both sides of bounded lock acquisition.
/// </summary>
internal sealed class VocabularyWindowsFileLock : IDisposable
{
    private readonly VocabularyWindowsFile _file;
    private bool _disposed;
    internal VocabularyWindowsFileLock(string database) => _file = VocabularyWindowsFile.Open(database + ".lock", createLock: true);
    internal IDisposable Enter(bool write)
    {
        if (_disposed) throw Invalid(); _file.Verify();
        var position = new VocabularyWindowsFile.LockOverlapped();
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (VocabularyWindowsFile.Native.LockFileEx(_file.Handle, write ? 3u : 1u, 0, 1, 0, ref position))
            {
                var lease = new Lease(this);
                try { _file.Verify(); return lease; } catch { lease.Dispose(); throw; }
            }
            if (Marshal.GetLastPInvokeError() != 33) throw Invalid();
            Thread.Sleep(20);
        }
        throw new VocabularyException(VocabularyFailure.StorageBusy);
    }
    private void Exit()
    {
        if (_disposed) return;
        var position = new VocabularyWindowsFile.LockOverlapped();
        if (!VocabularyWindowsFile.Native.UnlockFileEx(_file.Handle, 0, 1, 0, ref position)) Dispose();
    }
    private sealed class Lease(VocabularyWindowsFileLock owner) : IDisposable
    {
        private VocabularyWindowsFileLock? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
    public void Dispose() { if (_disposed) return; _disposed = true; _file.Dispose(); }
}
