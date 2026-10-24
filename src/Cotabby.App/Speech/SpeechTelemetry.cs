using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Cotabby.App.Runtime;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Core.Speech;

namespace Cotabby.App.Speech;

/// <summary>One app-owned, content-free speech observer shares the optional bounded history and
/// publishes the helper's runtime identity. A real structured log sink is an explicit platform
/// dependency; opt-out suppresses history writes, never lifecycle events. UI-context callers
/// serialize this owner; token identity protects measurements from late provider callbacks.</summary>
internal sealed class SpeechTelemetry : INotifyPropertyChanged
{
    private sealed class Measurement(LearningRequestContext context, DateTimeOffset timestamp, double startedAt)
    {
        internal LearningRequestContext Context { get; } = context;
        internal DateTimeOffset Timestamp { get; } = timestamp;
        internal double StartedAt { get; } = startedAt;
        internal long? LoadMs, AudioReadyMs, PlaybackMs, GenerationMs;
        internal long AudioBytes;
    }
    private readonly PerformanceMetricsStore? _metrics;
    private readonly Func<bool> _trackingEnabled;
    private readonly Func<double> _now;
    private readonly Action<LearningRequestContext, string, IReadOnlyDictionary<string, string>> _event;
    private readonly Dictionary<SpeechPlaybackToken, Measurement> _active = [];
    private readonly Dictionary<SpeechPlaybackToken, LearningRequestContext> _recentContexts = [];
    private readonly Dictionary<SpeechPlaybackToken, Guid> _recentEntries = [];
    private readonly Queue<SpeechPlaybackToken> _recentTokens = [];
    private ulong _runtimeSequence;
    private LearningRequestContext? _runtimeContext;
    internal SpeechRuntimeSnapshot Runtime { get; private set; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    internal SpeechTelemetry(Action<LearningRequestContext, string, IReadOnlyDictionary<string, string>> emit,
        PerformanceMetricsStore? metrics = null, Func<bool>? trackingEnabled = null, Func<double>? now = null)
    {
        _event = emit; _metrics = metrics; _trackingEnabled = trackingEnabled ?? (() => false);
        _now = now ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }
    internal void Begin(SpeechPlaybackToken token, string model, string engine, LearningLanguage language)
    {
        var context = new LearningRequestContext(LearningRequestIds.Generate(), LearningRequestIds.Generate(), ModelRequestSource.Speech, model, engine);
        _active[token] = new(context, DateTimeOffset.UtcNow, _now());
        Emit(context, "started", new() { ["language"] = language.Code() }); Emit(context, "generation_started", []);
    }
    internal void Generated(SpeechPlaybackToken token, long bytes)
    {
        if (!_active.TryGetValue(token, out var measurement)) return;
        bool first = measurement.AudioReadyMs is null;
        if (first) measurement.AudioReadyMs = Elapsed(measurement);
        measurement.AudioBytes = checked(measurement.AudioBytes + bytes);
        if (first) Emit(measurement.Context, "audio_ready", new() { ["elapsed_ms"] = Number(Elapsed(measurement)), ["audio_bytes"] = Number(bytes) });
    }
    internal void GenerationCompleted(SpeechPlaybackToken token)
    {
        if (!_active.TryGetValue(token, out var measurement) || measurement.GenerationMs is not null) return;
        measurement.GenerationMs = Elapsed(measurement);
        Emit(measurement.Context, "generation_completed", new() { ["elapsed_ms"] = Number(Elapsed(measurement)), ["audio_bytes"] = Number(measurement.AudioBytes) });
    }
    internal void PlaybackStarted(SpeechPlaybackToken token)
    {
        if (!_active.TryGetValue(token, out var measurement) || measurement.PlaybackMs is not null) return;
        measurement.PlaybackMs = Elapsed(measurement); Emit(measurement.Context, "playback_started", new() { ["elapsed_ms"] = Number(Elapsed(measurement)) });
    }
    internal void PlaybackPrepared(SpeechPlaybackToken token, long milliseconds)
    {
        if (_active.TryGetValue(token, out var measurement)) Emit(measurement.Context, "playback_prepared", new() { ["preparation_ms"] = Number(milliseconds), ["elapsed_ms"] = Number(Elapsed(measurement)) });
    }
    internal void Finish(SpeechPlaybackToken token, ModelRequestOutcome outcome, string? errorType = null)
    {
        if (!_active.Remove(token, out var measurement)) return;
        _recentContexts[token] = measurement.Context; _recentTokens.Enqueue(token);
        if (_recentTokens.Count > 16) { var retired = _recentTokens.Dequeue(); _recentContexts.Remove(retired); _recentEntries.Remove(retired); }
        var details = new Dictionary<string, string> { ["elapsed_ms"] = Number(Elapsed(measurement)), ["outcome"] = System.Text.Json.JsonSerializer.SerializeToElement(outcome).GetString()! };
        if (errorType is not null) details["error_type"] = errorType; Emit(measurement.Context, "finished", details);
        if (!_trackingEnabled() || _metrics is null) return;
        _recentEntries[token] = _metrics.Record(measurement.Context.ModelName, measurement.PlaybackMs ?? Elapsed(measurement), measurement.Timestamp,
            ModelRequestSource.Speech, outcome, measurement.Context.RequestId, measurement.LoadMs, measurement.AudioReadyMs, measurement.GenerationMs, measurement.PlaybackMs);
    }
    internal void Receive(SpeechRuntimeEvent value)
    {
        var context = value.Token is { } token ? _active.GetValueOrDefault(token)?.Context ?? _recentContexts.GetValueOrDefault(token) : null;
        context ??= value.Snapshot.SessionId == Runtime.SessionId ? _runtimeContext : null;
        // Stale runtime state must not erase a valid late load measurement for its older token.
        // Correlation/load patching precedes publication admission in the original owner.
        RecordLoad(value);
        if (AcceptRuntime(value))
        {
            if (Runtime != value.Snapshot) { Runtime = value.Snapshot; PropertyChanged?.Invoke(this, new(nameof(Runtime))); }
            _runtimeContext = context;
        }
        if (context is not null) Emit(context, "runtime_" + System.Text.Json.JsonSerializer.SerializeToElement(value.Snapshot.Phase).GetString(), RuntimeDetails(value));
    }
    private void RecordLoad(SpeechRuntimeEvent value)
    {
        if (value.Token is not { } token || value.ModelLoadMs is not { } duration) return;
        if (_active.TryGetValue(token, out var measurement)) measurement.LoadMs = duration;
        else if (_trackingEnabled() && _recentEntries.TryGetValue(token, out var id)) _metrics?.RecordLoad(id, duration);
    }
    private bool AcceptRuntime(SpeechRuntimeEvent value)
    {
        var incoming = value.Snapshot;
        if (incoming.Generation < Runtime.Generation || incoming.Phase != SpeechLocalRuntimePhase.Loading && incoming.SessionId != Runtime.SessionId) return false;
        if (incoming.SessionId != Runtime.SessionId) _runtimeSequence = 0;
        if (value.Sequence != 0 && value.Sequence <= _runtimeSequence) return false;
        _runtimeSequence = value.Sequence; return true;
    }
    private static Dictionary<string, string> RuntimeDetails(SpeechRuntimeEvent value)
    {
        var details = new Dictionary<string, string>(); if (value.Snapshot.ProcessId is { } pid) details["helper_pid"] = Number(pid);
        if (value.ModelLoadMs is { } duration) details["model_load_ms"] = Number(duration); if (value.Reason is not null) details["reason"] = value.Reason; return details;
    }
    private long Elapsed(Measurement measurement) => Math.Max(0, checked((long)Math.Round((_now() - measurement.StartedAt) * 1_000, MidpointRounding.AwayFromZero)));
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private void Emit(LearningRequestContext context, string phase, Dictionary<string, string> details) => _event(context, phase, new ReadOnlyDictionary<string, string>(details));
}
