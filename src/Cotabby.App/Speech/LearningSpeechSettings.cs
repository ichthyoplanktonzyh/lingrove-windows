using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Core.Text;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Speech;

/// <summary>App-owned speech preferences adapted from the pinned Mac owner. One UI context edits
/// this instance; providers borrow frozen configurations rather than reading mutable preferences
/// during a request. Voice/path/rate changes invalidate playback before mutation. Sampler changes
/// commit only for the captured local identity and apply to the next request without stopping sound.
/// Files and credentials are borrowed boundaries; construction performs neither key reads nor IO.</summary>
internal sealed class LearningSpeechSettings : INotifyPropertyChanged
{
    internal const double MinimumRateMultiplier = .5, MaximumRateMultiplier = 1.5, DefaultRateMultiplier = 1;
    private readonly Func<AppSettings> _current;
    private readonly Action _persist;
    private readonly IOpenAICompatibleCredentialStore _credentials;
    private readonly ISpeechAssetFiles _files;
    private readonly SpeechParameterSettings _parameters;
    private readonly Dictionary<string, string> _voiceIds = new(CanonicalStringComparer.Instance);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action? PlaybackInvalidated;
    // Request-only admission is retired before a sampler or credential mutation can race a
    // queued reading effect. This does not stop ordinary playback or change source preferences.
    internal event Action? RequestConfigurationChanging;
    internal LearningSpeechProviderKind SelectedProvider { get; private set; }
    internal string LocalModelPath { get; private set; }
    internal string LocalProjectionPath { get; private set; }
    internal string ReferenceAudioPath { get; private set; }
    internal string EndpointBaseUrl { get; private set; }
    internal string EndpointModel { get; private set; }
    internal string EndpointVoice { get; private set; }
    internal double RateMultiplier { get; private set; }
    internal IReadOnlyDictionary<string, string> VoiceIds => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(_voiceIds, CanonicalStringComparer.Instance));
    internal LearningSpeechSettings(Func<AppSettings> current, Action persist, IOpenAICompatibleCredentialStore credentials, ISpeechAssetFiles? files = null)
    {
        _current = current; _persist = persist; _credentials = credentials; _files = files ?? new SpeechAssetFiles();
        var saved = current();
        _parameters = new(saved.SpeechParametersValue, bytes => { _current().SpeechParametersValue = JsonSerializer.SerializeToElement(bytes); Persist(); });
        SelectedProvider = FoundationPreferenceValue.StringValue(saved.SpeechProviderValue) switch { "local" => LearningSpeechProviderKind.Local, "endpoint" => LearningSpeechProviderKind.Endpoint, _ => LearningSpeechProviderKind.System };
        LocalModelPath = FoundationPreferenceValue.StringValue(saved.SpeechLocalModelValue) ?? "";
        LocalProjectionPath = FoundationPreferenceValue.StringValue(saved.SpeechLocalProjectionValue) ?? "";
        ReferenceAudioPath = FoundationPreferenceValue.StringValue(saved.SpeechReferenceAudioValue) ?? "";
        EndpointBaseUrl = FoundationPreferenceValue.StringValue(saved.SpeechEndpointUrlValue) ?? "http://127.0.0.1:8000/v1";
        EndpointModel = FoundationPreferenceValue.StringValue(saved.SpeechEndpointModelValue) ?? "";
        EndpointVoice = FoundationPreferenceValue.StringValue(saved.SpeechEndpointVoiceValue) ?? "";
        RateMultiplier = Clamp(saved.SpeechRateValue switch
        {
            { ValueKind: JsonValueKind.Number } rate when rate.TryGetDouble(out double number) => number,
            { ValueKind: JsonValueKind.True } => 1, { ValueKind: JsonValueKind.False } => 0,
            _ => DefaultRateMultiplier,
        });
        if (saved.SpeechVoiceIdsValue is { ValueKind: JsonValueKind.Object } voices)
            foreach (var voice in voices.EnumerateObject()) if (voice.Value.ValueKind == JsonValueKind.String) _voiceIds.TryAdd(voice.Name, voice.Value.GetString()!);
        // Only currently configured legacy destinations migrate, including an explicitly empty
        // legacy voice. A present profile with nil fields wins over that old global key.
        if (LocalModelPath.Length > 0 && _parameters.Profile(LocalIdentity) is null) _parameters.Set(new() { ReferenceAudioPath = ReferenceAudioPath }, LocalIdentity);
        if (EndpointModel.Length > 0 && _parameters.Profile(EndpointIdentity) is null) _parameters.Set(new() { EndpointVoice = EndpointVoice }, EndpointIdentity);
    }
    private string LocalIdentity => ModelDestinationIdentity.Create("speech-local", LocalModelPath, mode: LocalProjectionPath);
    private string EndpointIdentity => ModelDestinationIdentity.Create("speech-endpoint", EndpointModel, EndpointBaseUrl);
    internal string ParameterIdentity => SelectedProvider == LearningSpeechProviderKind.Local ? LocalIdentity : EndpointIdentity;
    internal SpeechGenerationProfile GenerationProfile => _parameters.Profile(ParameterIdentity) ?? new();
    internal void SelectProvider(LearningSpeechProviderKind value)
    {
        if (SelectedProvider == value) return;
        PlaybackInvalidated?.Invoke(); SelectedProvider = value; _current().SpeechProviderValue = JsonSerializer.SerializeToElement(value); Persist(); Changed();
    }
    internal void SetLocalAssets(string model, string projection)
    {
        model = Trim(model); projection = Trim(projection);
        if (Same(LocalModelPath, model) && Same(LocalProjectionPath, projection)) return;
        LocalModelPath = SetPath(model, LocalModelPath, (s, v) => s.SpeechLocalModelValue = v);
        LocalProjectionPath = SetPath(projection, LocalProjectionPath, (s, v) => s.SpeechLocalProjectionValue = v);
        ReferenceAudioPath = SetPath(_parameters.Profile(LocalIdentity)?.ReferenceAudioPath ?? "", ReferenceAudioPath, (s, v) => s.SpeechReferenceAudioValue = v);
        Changed();
    }
    internal void SetLocalModelPath(string value) => SetLocalAssets(value, LocalProjectionPath);
    internal void SetLocalProjectionPath(string value) => SetLocalAssets(LocalModelPath, value);
    internal void SetReferenceAudioPath(string value)
    {
        ReferenceAudioPath = SetPath(value, ReferenceAudioPath, (s, v) => s.SpeechReferenceAudioValue = v);
        _parameters.Set((_parameters.Profile(LocalIdentity) ?? new()) with { ReferenceAudioPath = ReferenceAudioPath }, LocalIdentity); Changed();
    }
    internal void SetEndpointBaseUrl(string value) { EndpointBaseUrl = SetPath(value, EndpointBaseUrl, (s, v) => s.SpeechEndpointUrlValue = v); RestoreEndpointVoice(); Changed(); }
    internal void SetEndpointModel(string value) { EndpointModel = SetPath(value, EndpointModel, (s, v) => s.SpeechEndpointModelValue = v); RestoreEndpointVoice(); Changed(); }
    internal void SetEndpointVoice(string value)
    {
        EndpointVoice = SetPath(value, EndpointVoice, (s, v) => s.SpeechEndpointVoiceValue = v);
        _parameters.Set((_parameters.Profile(EndpointIdentity) ?? new()) with { EndpointVoice = EndpointVoice }, EndpointIdentity); Changed();
    }
    private void RestoreEndpointVoice() => EndpointVoice = SetPath(_parameters.Profile(EndpointIdentity)?.EndpointVoice ?? "", EndpointVoice, (s, v) => s.SpeechEndpointVoiceValue = v);
    internal void SetSamplingOverrides(LearningModelSamplingOverrides value, string? expectedIdentity = null)
    {
        if (SelectedProvider != LearningSpeechProviderKind.Local || expectedIdentity is not null && !Same(expectedIdentity, ParameterIdentity)) return;
        RequestConfigurationChanging?.Invoke();
        if (_parameters.Set(GenerationProfile with { Sampling = value.Normalized }, ParameterIdentity)) Changed();
    }
    internal string? EndpointApiKey() => _credentials.ReadApiKey();
    internal void SaveEndpointApiKey(string? value) { RequestConfigurationChanging?.Invoke(); _credentials.SaveApiKey(value); PlaybackInvalidated?.Invoke(); Changed(); }
    internal string? VoiceId(LearningLanguage language) => _voiceIds.GetValueOrDefault(language.Code());
    internal void SetVoiceId(string? voiceId, LearningLanguage language)
    {
        string? value = voiceId is null ? null : Trim(voiceId); if (value?.Length == 0) value = null;
        string key = language.Code(); if (Same(_voiceIds.GetValueOrDefault(key), value)) return;
        PlaybackInvalidated?.Invoke(); if (value is null) _voiceIds.Remove(key); else _voiceIds[key] = value;
        _current().SpeechVoiceIdsValue = JsonSerializer.SerializeToElement(_voiceIds); Persist(); Changed();
    }
    internal void SetRateMultiplier(double value)
    {
        double clamped = Clamp(value); if (RateMultiplier == clamped) return;
        PlaybackInvalidated?.Invoke(); RateMultiplier = clamped; _current().SpeechRateValue = JsonSerializer.SerializeToElement(clamped); Persist(); Changed();
    }
    internal SpeechLocalConfiguration LocalConfiguration()
    {
        if (LocalModelPath.Length == 0 || LocalProjectionPath.Length == 0) throw new SpeechException(SpeechFailure.MissingLocalAssets);
        SpeechAssetFile model, decoder;
        try { model = _files.Inspect(LocalModelPath); decoder = _files.Inspect(LocalProjectionPath); }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { throw new SpeechException(SpeechFailure.MissingLocalAssets); }
        if (SpeechModelPairingPolicy.IsKnownDecoder(model.Filename) || !Gguf(model) || !Gguf(decoder) || Same(model.Url.AbsoluteUri, decoder.Url.AbsoluteUri) || !model.Readable || !decoder.Readable)
            throw new SpeechException(SpeechFailure.MissingLocalAssets);
        if (SpeechModelPairingPolicy.IsKnownMismatch(model.Filename, decoder.Filename)) throw new SpeechException(SpeechFailure.MismatchedLocalAssets);
        SpeechAssetFile? reference = null;
        if (ReferenceAudioPath.Length > 0)
        {
            try { reference = _files.Inspect(ReferenceAudioPath); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { throw new SpeechException(SpeechFailure.InvalidReferenceAudio); }
            if (!(reference.Extension.Equals("wav", StringComparison.OrdinalIgnoreCase) || reference.Extension.Equals("mp3", StringComparison.OrdinalIgnoreCase)) || !reference.Readable || reference.Size is not (> 0 and <= 10 * 1_024 * 1_024))
                throw new SpeechException(SpeechFailure.InvalidReferenceAudio);
        }
        return new(model.Url, decoder.Url, reference?.Url);
    }
    internal SpeechEndpointConfiguration EndpointConfiguration()
    {
        if (EndpointModel.Length == 0 || EndpointVoice.Length == 0 || Encoding.UTF8.GetByteCount(EndpointModel) > 256 || Encoding.UTF8.GetByteCount(EndpointVoice) > 256) throw new SpeechException(SpeechFailure.IncompleteConfiguration);
        return new(new OpenAICompatibleEndpointConfiguration(EndpointBaseUrl, EndpointModel, OpenAICompatibleApiMode.ChatCompletions), EndpointVoice);
    }
    private string SetPath(string value, string current, Action<AppSettings, JsonElement> save)
    {
        string normalized = Trim(value); if (Same(current, normalized)) return current;
        PlaybackInvalidated?.Invoke(); save(_current(), JsonSerializer.SerializeToElement(normalized)); Persist(); return normalized;
    }
    private void Persist() { try { _persist(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
    private static string Trim(string value) => FoundationText.TrimWhitespaceAndNewlines(value);
    private static bool Same(string? a, string? b) => CanonicalStringComparer.Instance.Equals(a, b);
    private static bool Gguf(SpeechAssetFile value) => value.Extension.Equals("gguf", StringComparison.OrdinalIgnoreCase);
    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, MinimumRateMultiplier, MaximumRateMultiplier) : DefaultRateMultiplier;
}
