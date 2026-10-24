using System.Globalization;
using System.IO;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Core.Speech;
using Cotabby.Core.Text;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Speech;

/// <summary>Review borrows the same speech owner for explicit export/audition. Export has its own
/// token, frozen source and caller-owned cancellation, so playback Stop is not export cancellation.
/// Settings invalidation changes the configuration ID; the review session must compare it before
/// adopting returned bytes. No second warm runtime or automatic provider fallback is created.</summary>
internal sealed partial class LearningSpeechController : ISpeechExporting
{
    public string ExportConfigurationId
    {
        get
        {
            RequireOwner();
            var identity = new List<string> { _exportRevision.ToString(CultureInfo.InvariantCulture), ProviderName(), Settings.LocalModelPath,
                Settings.LocalProjectionPath, Settings.ReferenceAudioPath, Settings.EndpointBaseUrl, Settings.EndpointModel, Settings.EndpointVoice };
            identity.AddRange(Settings.VoiceIds.OrderBy(pair => pair.Key, CanonicalStringComparer.Instance).SelectMany(pair => new[] { pair.Key, pair.Value }));
            byte[] profile;
            try { profile = SpeechExportIdentity.Profile(Settings.GenerationProfile); } catch (JsonException) { profile = []; }
            return SpeechExportPolicy.Digest(SpeechExportIdentity.Strings(identity)) + SpeechExportPolicy.Digest(profile);
        }
    }
    public SpeechExportRequest ExportRequest(string text, LearningLanguage language, string? context = null)
    {
        RequireOwner(); if (!SpeechTextPolicy.WithinRequestLimit(text)) throw new SpeechException(SpeechFailure.TextTooLong);
        string cleaned = SpeechTextPolicy.SanitizedText(text) ?? throw new SpeechException(SpeechFailure.UnrecognizedLanguage);
        var resolved = SpeechLanguageResolver.Resolve(cleaned, language, _recognize, context) ?? throw new SpeechException(SpeechFailure.UnrecognizedLanguage);
        var identity = new List<string> { "pcm16-v1", cleaned, resolved.Code(), ProviderName() }; SpeechExportRequest.Source source;
        switch (Settings.SelectedProvider)
        {
            case LearningSpeechProviderKind.System:
                if (!CatalogIsLoaded) RefreshVoices();
                var voice = SpeechVoicePolicy.Select(VoiceCatalog, resolved, Settings.VoiceId(resolved), _voiceComparer) ?? throw new SpeechException(SpeechFailure.UnsupportedLanguage);
                identity.Add(voice.Id); source = new SpeechExportRequest.Source.System(voice.Id); break;
            case LearningSpeechProviderKind.Local:
                if (LocalSpeechLanguage.Code(resolved) is null) throw new SpeechException(SpeechFailure.UnsupportedLanguage);
                var local = Settings.LocalConfiguration(); identity.AddRange([local.ModelUrl.LocalPath, local.ProjectionUrl.LocalPath, local.ReferenceAudioUrl?.LocalPath ?? ""]);
                source = new SpeechExportRequest.Source.Generated(new SpeechAudioSource.Local(local), Settings.GenerationProfile.Sampling); break;
            case LearningSpeechProviderKind.Endpoint:
                var endpoint = Settings.EndpointConfiguration(); identity.AddRange([Settings.EndpointBaseUrl, endpoint.Endpoint.ModelName, endpoint.Voice]);
                source = new SpeechExportRequest.Source.Generated(new SpeechAudioSource.Endpoint(endpoint, Settings.EndpointApiKey()), new()); break;
            default: throw new ArgumentOutOfRangeException();
        }
        byte[] profile = SpeechExportIdentity.Profile(Settings.GenerationProfile);
        string fingerprint = SpeechExportPolicy.Digest(SpeechExportIdentity.Strings(identity).Concat(profile).ToArray());
        return new(cleaned, resolved, fingerprint, source);
    }
    public async Task<SpeechExportAudio> ExportAudioAsync(SpeechExportRequest request, CancellationToken cancellationToken = default)
    {
        RequireOwner(); if (ActiveTarget is not null || _exportToken is not null) throw new SpeechException(SpeechFailure.OperationInProgress);
        _exportToken = request.Token;
        var (model, engine) = request.AudioSource switch
        {
            SpeechExportRequest.Source.System system => (system.VoiceId, "system_speech"),
            SpeechExportRequest.Source.Generated { Audio: SpeechAudioSource.Local local } => (Path.GetFileName(local.Configuration.ModelUrl.LocalPath), "llama"),
            SpeechExportRequest.Source.Generated { Audio: SpeechAudioSource.Endpoint endpoint } => (endpoint.Configuration.Endpoint.ModelName, "openai_compatible"),
            _ => throw new ArgumentOutOfRangeException(),
        };
        Telemetry.Begin(request.Token, model, engine, request.Language);
        try
        {
            SpeechExportAudio audio;
            switch (request.AudioSource)
            {
                case SpeechExportRequest.Source.System system: audio = await _systemExporter.AudioAsync(request.Text, system.VoiceId, cancellationToken); break;
                case SpeechExportRequest.Source.Generated generated:
                    var collector = new SpeechExportBuffer();
                    var generation = new SpeechAudioRequest(request.Token, request.Text, request.Language, generated.Audio, generated.Sampling);
                    await _generator.GenerateAsync(generation, chunk => collector.AppendAsync(chunk, cancellationToken), cancellationToken);
                    audio = await collector.FinishAsync(); break;
                default: throw new ArgumentOutOfRangeException();
            }
            cancellationToken.ThrowIfCancellationRequested(); if (_exportToken != request.Token) throw new OperationCanceledException();
            double seconds = await _duration.DurationAsync(audio.CopyBytes(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); if (_exportToken != request.Token) throw new OperationCanceledException();
            if (!(seconds > 0 && seconds <= 90 && double.IsFinite(seconds))) throw new SpeechException(SpeechFailure.InvalidAudio);
            Telemetry.Generated(request.Token, audio.ByteCount); Telemetry.GenerationCompleted(request.Token); Telemetry.Finish(request.Token, ModelRequestOutcome.Completed);
            return audio;
        }
        catch (Exception error)
        {
            Telemetry.Finish(request.Token, error is OperationCanceledException ? ModelRequestOutcome.Cancelled : ModelRequestOutcome.Failed, error is OperationCanceledException ? null : ErrorType(error));
            await _generator.CancelAsync(request.Token); throw;
        }
        finally { if (_exportToken == request.Token) _exportToken = null; }
    }
    private string ProviderName() => Settings.SelectedProvider switch
    { LearningSpeechProviderKind.System => "system", LearningSpeechProviderKind.Local => "local", LearningSpeechProviderKind.Endpoint => "endpoint", _ => throw new ArgumentOutOfRangeException() };
}
