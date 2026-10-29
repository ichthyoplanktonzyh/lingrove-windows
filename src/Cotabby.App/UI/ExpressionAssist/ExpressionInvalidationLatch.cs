namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>Terminal invalidation survives asynchronous native-to-UI subscription handoffs.
/// Callbacks run outside the gate; a late subscriber observes the terminal state immediately.</summary>
internal sealed class ExpressionInvalidationLatch
{
    private readonly object _gate = new();
    private Action? _subscribers;
    private int _retired;
    internal bool IsRetired => Volatile.Read(ref _retired) != 0;
    internal event Action Invalidated
    {
        add
        {
            bool replay;
            lock (_gate) { replay = IsRetired; if (!replay) _subscribers += value; }
            if (replay) value();
        }
        remove { lock (_gate) _subscribers -= value; }
    }
    internal void Retire()
    {
        Action? notify;
        lock (_gate)
        {
            if (IsRetired) return;
            Volatile.Write(ref _retired, 1); notify = _subscribers; _subscribers = null;
        }
        notify?.Invoke();
    }
}
