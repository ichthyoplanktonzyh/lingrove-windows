using Cotabby.Core.Runtime.Metrics;

namespace Cotabby.Core.Suggestions;

/// <summary>Content-free observation from an actual completion backend invocation. Model identity
/// is captured from that invocation's selected owner, never inferred from a later UI setting.</summary>
public sealed record CompletionObservation(string RequestId, string ModelName, DateTimeOffset Timestamp,
    long LatencyMs, ModelRequestOutcome Outcome);
