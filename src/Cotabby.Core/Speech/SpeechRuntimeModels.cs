using System.Collections.Frozen;
using System.Text.Json.Serialization;
using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>The process owner keeps PID/assets visible through Stopping until it has reaped the
/// helper. A fresh session identity separates late events from the next model's runtime.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpeechLocalRuntimePhase>))]
public enum SpeechLocalRuntimePhase
{
    [JsonStringEnumMemberName("unloaded")] Unloaded, [JsonStringEnumMemberName("loading")] Loading,
    [JsonStringEnumMemberName("ready")] Ready, [JsonStringEnumMemberName("generating")] Generating,
    [JsonStringEnumMemberName("stopping")] Stopping, [JsonStringEnumMemberName("failed")] Failed,
}
/// <summary>The original English runtime labels, independent of process control. A future
/// localization adapter can translate presentation without changing the lifecycle identifiers.</summary>
public static class SpeechLocalRuntimePhases
{
    public static string Label(this SpeechLocalRuntimePhase value) => value switch
    {
        SpeechLocalRuntimePhase.Unloaded => "Not loaded", SpeechLocalRuntimePhase.Loading => "Loading", SpeechLocalRuntimePhase.Ready => "Ready",
        SpeechLocalRuntimePhase.Generating => "Generating speech…", SpeechLocalRuntimePhase.Stopping => "Unloading", SpeechLocalRuntimePhase.Failed => "Failed", _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}

/// <summary>Detached published runtime identity and exact asset-name snapshot. It owns its set
/// rather than borrowing mutable generator/settings collections. No full path/audio/text/key
/// belongs in this value; lifecycle and performance observe the same source.</summary>
public sealed record SpeechRuntimeSnapshot
{
    private readonly IReadOnlySet<string> _filenames = Array.Empty<string>().ToFrozenSet(CanonicalStringComparer.Instance);
    public Guid? SessionId { get; init; }
    public ulong Generation { get; init; }
    public SpeechLocalRuntimePhase Phase { get; init; }
    public int? ProcessId { get; init; }
    public string ModelName { get; init; } = "";
    public IReadOnlySet<string> LoadedModelFilenames { get => _filenames; init => _filenames = value.ToFrozenSet(CanonicalStringComparer.Instance); }
    public bool Equals(SpeechRuntimeSnapshot? other) => other is not null && SessionId == other.SessionId && Generation == other.Generation
        && Phase == other.Phase && ProcessId == other.ProcessId && CanonicalStringComparer.Instance.Equals(ModelName, other.ModelName) && _filenames.SetEquals(other._filenames);
    public override int GetHashCode()
    {
        int names = 0; foreach (string name in _filenames) names ^= CanonicalStringComparer.Instance.GetHashCode(name);
        return HashCode.Combine(SessionId, Generation, Phase, ProcessId, CanonicalStringComparer.Instance.GetHashCode(ModelName), names);
    }
}

/// <summary>Only operation identities, timings and fixed reason labels cross to telemetry.
/// Sequence zero retains the source's unsequenced-event behavior; other events must increase.</summary>
public sealed record SpeechRuntimeEvent(SpeechRuntimeSnapshot Snapshot, SpeechPlaybackToken? Token = null, ulong Sequence = 0, long? ModelLoadMs = null, string? Reason = null);
