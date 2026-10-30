using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.Core.Models;
using Cotabby.Core.Speech;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;
using Cotabby.Win32.Speech;

namespace Cotabby.App.UI;

/// <summary>The example owns system speech independently of production profiles. Construction is
/// inert; explicit buttons may synthesize/play locally. Tests select the silent adapter instead.</summary>
internal sealed class LearningShowcaseSpeech(bool silent = true) : ISpeechProviding, ISpeechAudioGenerating, ISpeechAudioPlaying,
    ISpeechPCMPlaying, ISystemSpeechAudioExporting, ISpeechAudioDuration, ISpeechModelManagement, ISpeechAssetFiles, IOpenAICompatibleCredentialStore
{
    private readonly WindowsSystemSpeechProvider? _system = silent ? null : new();
    public Action<SpeechProviderEvent>? EventHandler { get; set; }
    public Action? DirectoryChanged { get; set; }
    public Func<IReadOnlySet<string>> ProtectedModelFilenames { get; set; } = () => new HashSet<string>();
    public IReadOnlyList<SpeechVoice> RefreshVoices() => [new("sample-en", "Silent sample", "en-US", SpeechVoiceQuality.Standard),
        new("sample-zh", "Silent sample", "zh-CN", SpeechVoiceQuality.Standard)];
    public void Speak(string text, SpeechVoice voice, double rate, SpeechPlaybackToken token) => EventHandler?.Invoke(new SpeechProviderEvent.Started(token));
    public void Speak(string text, SpeechVoice voice, double rate, SpeechPlaybackToken token, Func<bool>? admission)
    { SpeechAdmission.Require(admission); Speak(text, voice, rate, token); }
    public void Stop(SpeechPlaybackToken token) => EventHandler?.Invoke(new SpeechProviderEvent.Cancelled(token));
    public void Stop() { }
    public void Shutdown() { }
    public void Play(ReadOnlySpan<byte> data, double rate, SpeechPlaybackToken token) => throw new NotSupportedException();
    public void Begin(int sampleRate, double rate, SpeechPlaybackToken token) => throw new NotSupportedException();
    public Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<SpeechExportAudio> AudioAsync(string text, string voiceId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public double Duration(ReadOnlySpan<byte> audio) => throw new NotSupportedException();
    public Task GenerateAsync(SpeechAudioRequest request, Func<SpeechAudioChunk, Task> receive, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task CancelAsync(SpeechPlaybackToken token) => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public IReadOnlyList<RuntimeModelOption> InstalledModelOptions() => [];
    public SpeechAssetFile Inspect(string path) => throw new NotSupportedException();
    public string? ReadApiKey() => null;
    public void SaveApiKey(string? value) { }
    public void DeleteApiKey() { }

    internal LearningSpeechController CreateController()
    {
        var preferences = new AppSettings();
        var settings = new LearningSpeechSettings(() => preferences, () => { }, this, this);
        ISystemSpeechAudioExporting exporter = _system is null ? this : new WindowsSystemSpeechExporter();
        ISpeechAudioDuration duration = _system is null ? this : new WindowsSpeechAudioCodec();
        return new(settings, new SpeechTelemetry((_, _, _) => { }), _system ?? (ISpeechProviding)this, this, this, this, exporter, duration,
            _ => new Dictionary<string, double>(), StringComparer.CurrentCulture, "", () => this, this);
    }
    internal ValueTask RetireAsync() => _system?.DisposeAsync() ?? ValueTask.CompletedTask;
}
