namespace Cotabby.Core.Runtime.Metrics;

/// <summary>Detached content-free request measurement; optional stages are omitted when not observed.</summary>
public sealed record PerformanceRequestObservation(ModelRequestSource Source, string RequestId, string ModelName,
    DateTimeOffset Timestamp, long LatencyMs, ModelRequestOutcome Outcome, long? FirstOutputMs = null, long? UsableResultMs = null);
