using System.Text.Json.Serialization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>One speech destination's durable sampler and optional voice inputs. The settings
/// owner keys this immutable value by model/decoder or endpoint/model identity. Playback rate
/// and installed system voices have separate owners; requests freeze this value before awaits.</summary>
public sealed record SpeechGenerationProfile
{
    [JsonPropertyName("sampling")] public LearningModelSamplingOverrides Sampling { get; init; } = new();
    [JsonPropertyName("referenceAudioPath")] public string? ReferenceAudioPath { get; init; }
    [JsonPropertyName("endpointVoice")] public string? EndpointVoice { get; init; }
    public bool Equals(SpeechGenerationProfile? other) => other is not null && Sampling == other.Sampling
        && CanonicalStringComparer.Instance.Equals(ReferenceAudioPath, other.ReferenceAudioPath)
        && CanonicalStringComparer.Instance.Equals(EndpointVoice, other.EndpointVoice);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Sampling);
        hash.Add(ReferenceAudioPath is null ? 0 : CanonicalStringComparer.Instance.GetHashCode(ReferenceAudioPath));
        hash.Add(EndpointVoice is null ? 0 : CanonicalStringComparer.Instance.GetHashCode(EndpointVoice)); return hash.ToHashCode();
    }
}

/// <summary>The explicit task-local speech choice. Constructing preferences never activates a
/// provider, loads a model or sends text. The original durable identifiers remain lowercase.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearningSpeechProviderKind>))]
public enum LearningSpeechProviderKind
{
    [JsonStringEnumMemberName("system")] System,
    [JsonStringEnumMemberName("local")] Local,
    [JsonStringEnumMemberName("endpoint")] Endpoint,
}
