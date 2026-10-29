namespace Cotabby.Core.Suggestions;

/// <summary>Per-surface status publication. Selection epoch and local operation sequence must
/// both match, so stale progress, completion and failure cannot replace a newer status.</summary>
public sealed class CompletionStatusGate
{
    public readonly record struct Token(long Epoch, long Sequence);
    private long _sequence;
    public Token Begin(long epoch) => new(epoch, Interlocked.Increment(ref _sequence));
    public void Invalidate() => Interlocked.Increment(ref _sequence);
    public bool IsCurrent(Token token, long? currentEpoch) => currentEpoch == token.Epoch
        && Volatile.Read(ref _sequence) == token.Sequence;
}
