using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Inference.OpenAICompatible;
using System.Text.Json.Serialization;

namespace Cotabby.Inference.Speech;

/// <summary>Frozen local speech assets, validated by the settings boundary before a request.
/// These URLs do no file IO and do not own a model. The pinned helper's explicit codec is24kHz.</summary>
public sealed record SpeechLocalConfiguration(Uri ModelUrl, Uri ProjectionUrl, Uri? ReferenceAudioUrl = null)
{
    public const int OutputSampleRate = 24_000;
}

/// <summary>The selected speech endpoint and speaker, independent of autocomplete/translation.
/// The existing validated endpoint value is reused in Inference so Core gains no transport edge.</summary>
public sealed record SpeechEndpointConfiguration(OpenAICompatibleEndpointConfiguration Endpoint, string Voice);

/// <summary>Closed, ephemeral generated-source choice. Credentials occur only in the endpoint
/// variant and are never part of a fingerprint or automatic diagnostic string. No source performs
/// a request or falls back to a different provider by being constructed.</summary>
public abstract class SpeechAudioSource
{
    private SpeechAudioSource() { }
    public sealed class Local(SpeechLocalConfiguration configuration) : SpeechAudioSource
    { public SpeechLocalConfiguration Configuration { get; } = configuration; }
    public sealed class Endpoint(SpeechEndpointConfiguration configuration, string? apiKey) : SpeechAudioSource
    {
        public SpeechEndpointConfiguration Configuration { get; } = configuration;
        [JsonIgnore] public string? ApiKey { get; } = apiKey;
    }
}

/// <summary>One explicit frozen export action. Text/language/provider/profile are captured before
/// awaiting a generator. Same-side recognition context has already been consumed and has no field
/// here. The transport-owned endpoint value keeps this contract in Inference rather than Core.</summary>
public sealed class SpeechExportRequest(string text, LearningLanguage language, string fingerprint, SpeechExportRequest.Source source)
{
    public SpeechPlaybackToken Token { get; } = new();
    public string Text { get; } = text;
    public LearningLanguage Language { get; } = language;
    public string Fingerprint { get; } = fingerprint;
    public Source AudioSource { get; } = source;

    /// <summary>System export captures only the chosen voice; generated export additionally
    /// freezes request-level sampling without borrowing or changing the resident runtime identity.</summary>
    public abstract class Source
    {
        private Source() { }
        public sealed class System(string voiceId) : Source { public string VoiceId { get; } = voiceId; }
        public sealed class Generated(SpeechAudioSource audio, LearningModelSamplingOverrides sampling) : Source
        {
            public SpeechAudioSource Audio { get; } = audio;
            public LearningModelSamplingOverrides Sampling { get; } = sampling;
        }
    }
}
