using System.Text.Json;
using System.Text.Json.Serialization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;

namespace Cotabby.Inference.Speech;

/// <summary>One immutable generation action freezes only the clicked text, resolved language,
/// source and sampler. Recognition context never crosses this seam. Credentials live only in the
/// endpoint source; JSON/diagnostic serialization of a request is deliberately unsupported.</summary>
[JsonConverter(typeof(SpeechAudioRequest.EphemeralConverter))]
public sealed class SpeechAudioRequest(SpeechPlaybackToken token, string text, LearningLanguage language,
    SpeechAudioSource source, LearningModelSamplingOverrides? sampling = null)
{
    public SpeechPlaybackToken Token { get; } = token;
    public string Text { get; } = text;
    public LearningLanguage Language { get; } = language;
    [JsonIgnore] public SpeechAudioSource Source { get; } = source;
    public LearningModelSamplingOverrides Sampling { get; } = sampling ?? new();
    public sealed class EphemeralConverter : JsonConverter<SpeechAudioRequest>
    {
        public override SpeechAudioRequest Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException("Speech generation requests are ephemeral.");
        public override void Write(Utf8JsonWriter writer, SpeechAudioRequest value, JsonSerializerOptions options) => throw new NotSupportedException("Speech generation requests are ephemeral.");
    }
}
/// <summary>The shared speech controller borrows one independent generator. Every ordered chunk
/// awaits its receiver, so PCM backpressure reaches the producer. Implementations own native/HTTP
/// work away from UI, keep token cancellation separate from autocomplete/learning runtimes and
/// never change providers after an error. This interface supplies no production generator.</summary>
public interface ISpeechAudioGenerating
{
    Task GenerateAsync(SpeechAudioRequest request, Func<SpeechAudioChunk, Task> receive, CancellationToken cancellationToken);
    Task CancelAsync(SpeechPlaybackToken token);
    Task ShutdownAsync();
}
/// <summary>Explicit system recording uses the same selected voice as playback, then returns
/// detached export audio. A caller-owned cancellation token ends that recording; no device or
/// automatic voice fallback is constructed by this contract.</summary>
public interface ISystemSpeechAudioExporting
{
    Task<SpeechExportAudio> AudioAsync(string text, string voiceId, CancellationToken cancellationToken);
}
/// <summary>The platform codec inspects an already bounded export without playing it. The
/// controller still owns the source's finite, positive, at-most90-second admission. This is a
/// decoder boundary, not a WAV-header duration guess or a replacement for export normalization.</summary>
public interface ISpeechAudioDuration
{
    double Duration(ReadOnlySpan<byte> audio);
}
