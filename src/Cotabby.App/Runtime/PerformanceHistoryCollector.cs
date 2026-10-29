using System.Windows.Threading;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Core.Suggestions;
using Cotabby.App.Settings;

namespace Cotabby.App.Runtime;

/// <summary>Marshals content-free completion observations to the one UI-owned history. A single
/// pending dispatcher operation drains at most the history's capacity; retiring closes admission
/// and flushes accepted records before the shared settings owner goes away.</summary>
internal sealed class PerformanceHistoryCollector : IDisposable
{
    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly PerformanceMetricsStore _history;
    private readonly Queue<PerformanceRequestObservation> _pending = new();
    private bool _enabled, _posted, _closed;
    internal PerformanceHistoryCollector(Dispatcher dispatcher, PerformanceMetricsStore history, bool enabled)
    { _dispatcher = dispatcher; _history = history; _enabled = enabled; }
    internal void SetEnabled(bool enabled) { lock (_gate) { if (!_closed) _enabled = enabled; } }
    internal void ApplySettings(AppSettings settings)
    { _dispatcher.VerifyAccess(); SetEnabled(settings.PerformanceTrackingEnabled); }
    internal void Observe(CompletionObservation value)
        => Observe(new PerformanceRequestObservation(ModelRequestSource.Completion, value.RequestId, value.ModelName,
            value.Timestamp, value.LatencyMs, value.Outcome));
    internal void Observe(PerformanceRequestObservation value)
    {
        lock (_gate)
        {
            if (_closed || !_enabled) return;
            if (_pending.Count == PerformanceMetricsStore.MaximumEntries) _pending.Dequeue();
            _pending.Enqueue(value);
            if (_posted) return;
            _posted = true;
            try { _dispatcher.BeginInvoke(Drain); }
            catch (InvalidOperationException) { _posted = false; _pending.Clear(); }
        }
    }
    private void Drain()
    {
        _dispatcher.VerifyAccess(); PerformanceRequestObservation[] values;
        lock (_gate) { values = _pending.ToArray(); _pending.Clear(); _posted = false; }
        foreach (var value in values)
            _history.Record(value.ModelName, value.LatencyMs, value.Timestamp, value.Source, value.Outcome, value.RequestId,
                firstOutputMs: value.FirstOutputMs, usableResultMs: value.UsableResultMs);
    }
    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        lock (_gate) { if (_closed) return; _closed = true; }
        Drain();
    }
}
