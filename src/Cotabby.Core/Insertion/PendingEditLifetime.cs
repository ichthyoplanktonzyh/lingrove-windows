namespace Cotabby.Core.Insertion;

/// <summary>
/// Owns cancellation for accepted edits that are queued or already running. Ordinary input,
/// settings changes, focus transitions, and dismissal advance the epoch and cancel every edit
/// captured under the previous epoch; consecutive accept keys keep the same epoch and remain
/// ordered by the coordinator's insertion chain.
/// </summary>
/// <remarks>
/// An edit lease is created when an accept is enqueued and disposed only after that queued task
/// finishes. This keeps each cancellation source alive for the whole period in which Core or a
/// platform inserter may observe its token.
/// </remarks>
internal sealed class PendingEditLifetime : IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<EditLease> _leases = [];
    private long _epoch = 1;
    private bool _disposed;

    /// <summary>Capture this on the hook thread when an accept key is first recognized.</summary>
    public long CaptureEpoch()
    {
        lock (_gate) return _epoch;
    }

    public bool IsCurrent(long epoch)
    {
        lock (_gate) return !_disposed && epoch == _epoch;
    }

    /// <summary>
    /// Register a queued edit only when the epoch captured by its accept key is still current.
    /// A null lease means a later key, focus change, settings change, or dismissal already revoked it.
    /// </summary>
    public EditLease? TryBegin(long epoch)
    {
        lock (_gate)
        {
            if (_disposed || epoch != _epoch) return null;
            var lease = new EditLease(this);
            _leases.Add(lease);
            return lease;
        }
    }

    /// <summary>Revoke the current epoch and cancel its queued and active edit work.</summary>
    public long Invalidate()
    {
        EditLease[] pending;
        long epoch;
        lock (_gate)
        {
            if (_disposed) return _epoch;
            epoch = ++_epoch;
            pending = _leases.ToArray();
        }

        foreach (var lease in pending) lease.Cancel();
        return epoch;
    }

    public void Dispose()
    {
        EditLease[] pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _epoch++;
            pending = _leases.ToArray();
        }

        foreach (var lease in pending) lease.Cancel();
    }

    private void Release(EditLease lease)
    {
        lock (_gate) _leases.Remove(lease);
        lease.DisposeSource();
    }

    /// <summary>
    /// A per-queued-task cancellation source. Cancel and disposal coordinate locally so an
    /// invalidation racing task completion cannot cancel a source after it has been disposed.
    /// </summary>
    internal sealed class EditLease : IDisposable
    {
        private readonly PendingEditLifetime _owner;
        private readonly object _gate = new();
        private CancellationTokenSource? _source = new();
        private int _activeCancels;
        private bool _disposeRequested;

        internal EditLease(PendingEditLifetime owner)
        {
            _owner = owner;
            Token = _source.Token;
        }

        public CancellationToken Token { get; }

        public void Dispose() => _owner.Release(this);

        internal void Cancel()
        {
            CancellationTokenSource? source;
            lock (_gate)
            {
                source = _source;
                if (source is null) return;
                _activeCancels++;
            }

            try
            {
                source.Cancel();
            }
            catch (Exception)
            {
                // Cancellation callbacks belong to the consumer of this token. A faulty callback
                // must not escape onto the keyboard or focus event thread that invalidated it.
            }
            finally
            {
                lock (_gate)
                {
                    _activeCancels--;
                    DisposeIfFinished();
                }
            }
        }

        internal void DisposeSource()
        {
            lock (_gate)
            {
                _disposeRequested = true;
                DisposeIfFinished();
            }
        }

        private void DisposeIfFinished()
        {
            if (!_disposeRequested || _activeCancels != 0 || _source is null) return;
            _source.Dispose();
            _source = null;
        }
    }
}
