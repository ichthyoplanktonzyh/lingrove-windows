using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Cotabby.App.Settings;
using Cotabby.Core.Runtime.Metrics;

namespace Cotabby.App.Runtime;

/// <summary>One app-owned UI-context chronological history, capped at the original 100 rows.
/// Telemetry calls Record only under its explicit opt-in policy. Late patches replace immutable
/// snapshots once and never revive a cleared/evicted ID. Preferences and the real diagnostic sink
/// are borrowed; this library does not create another settings file or logger.</summary>
internal sealed class PerformanceMetricsStore : INotifyPropertyChanged
{
    internal const int MaximumEntries = 100;
    private readonly Func<AppSettings> _current;
    private readonly Action _persist;
    private readonly Action<Exception, int> _persistenceFailure;
    private PerformanceMetricEntry[] _entries;
    internal IReadOnlyList<PerformanceMetricEntry> Entries => Array.AsReadOnly(_entries);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal PerformanceMetricsStore(Func<AppSettings> current, Action persist, Action<Exception, int> persistenceFailure)
    {
        _current = current; _persist = persist; _persistenceFailure = persistenceFailure;
        IReadOnlyList<PerformanceMetricEntry>? decoded = null;
        if (current().PerformanceMetricEntriesValue is { ValueKind: JsonValueKind.String } raw && raw.TryGetBytesFromBase64(out var data)) decoded = PerformanceMetricCodec.Decode(data);
        _entries = (decoded ?? []).TakeLast(MaximumEntries).ToArray();
    }
    internal Guid Record(string modelName, long latencyMs, DateTimeOffset? timestamp = null,
        ModelRequestSource source = ModelRequestSource.Completion, ModelRequestOutcome outcome = ModelRequestOutcome.Completed,
        string? requestId = null, long? modelLoadMs = null, long? firstOutputMs = null, long? usableResultMs = null, long? displayMs = null)
    {
        var entry = new PerformanceMetricEntry(Guid.NewGuid(), timestamp ?? DateTimeOffset.UtcNow, modelName, latencyMs, source, outcome, requestId, modelLoadMs, firstOutputMs, usableResultMs, displayMs);
        _entries = _entries.Append(entry).TakeLast(MaximumEntries).ToArray(); Changed(); Persist(); return entry.Id;
    }
    internal void RecordDisplay(Guid entryId, long elapsedMs)
    {
        int index = Array.FindIndex(_entries, value => value.Id == entryId); if (index < 0 || _entries[index].DisplayMs is not null) return;
        var next = (PerformanceMetricEntry[])_entries.Clone(); next[index] = next[index] with { DisplayMs = Math.Max(0, elapsedMs) }; _entries = next; Changed(); Persist();
    }
    internal void RecordLoad(Guid entryId, long elapsedMs)
    {
        int index = Array.FindIndex(_entries, value => value.Id == entryId); if (index < 0 || _entries[index].ModelLoadMs is not null) return;
        var next = (PerformanceMetricEntry[])_entries.Clone(); next[index] = next[index] with { ModelLoadMs = Math.Max(0, elapsedMs) }; _entries = next; Changed(); Persist();
    }
    internal void Clear()
    {
        if (_entries.Length == 0) return;
        _entries = []; Changed(); _current().PerformanceMetricEntriesValue = null;
        try { _persist(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _persistenceFailure(error, 0); }
    }
    private void Persist()
    {
        try { _current().PerformanceMetricEntriesValue = JsonSerializer.SerializeToElement(PerformanceMetricCodec.Encode(_entries)); _persist(); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException) { _persistenceFailure(error, _entries.Length); }
    }
    private void Changed() => PropertyChanged?.Invoke(this, new(nameof(Entries)));
}
