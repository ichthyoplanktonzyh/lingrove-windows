using System.IO;
using System.Windows.Threading;
using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.App.Localization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Core.Speech;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Wpf.Tests;

/// <summary>The actual compiled controller runs on the WPF STA while a fake generator produces
/// bytes on a worker. This checks callback marshaling, stale cancellation and joined teardown;
/// no synthesis, codec, recognizer, network or audio hardware participates.</summary>
[Collection("WPF views")]
public sealed class LearningSpeechControllerNativeTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task FrozenInterfaceFormatterDisclosesSpeechWithoutActivatingProviders() => dispatcher.RunAsync(async () =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans"); var saved = new AppSettings();
            var settings = new LearningSpeechSettings(() => saved, () => { }, new NoCredentials());
            var effects = new Effects(Dispatcher.CurrentDispatcher);
            await using var controller = new LearningSpeechController(settings, new SpeechTelemetry((_, _, _) => { }),
                effects, new WorkerGenerator(), effects, effects, effects, effects, _ => new Dictionary<string, double>(),
                StringComparer.Ordinal, Path.GetTempPath(), () => effects, format: InterfaceText.Format);
            Assert.Contains("系统语音", controller.DestinationDescription);
            settings.SetEndpointBaseUrl("https://owned.invalid/private/path"); settings.SetEndpointModel("General {0} Cotabby");
            settings.SetEndpointVoice("voice {1}"); settings.SelectProvider(LearningSpeechProviderKind.Endpoint);
            string text = controller.DestinationDescription;
            Assert.Contains("语音端点", text); Assert.Contains("https://owned.invalid", text); Assert.DoesNotContain("private/path", text);
            Assert.Contains("General {0} Cotabby", text); Assert.Contains("voice {1}", text);
            settings.SetEndpointBaseUrl("https://name:owned-secret@owned.invalid/?key=owned-secret");
            Assert.DoesNotContain("owned-secret", controller.DestinationDescription); Assert.Contains("无法发送文本", controller.DestinationDescription);
            Assert.Equal(0, effects.Played);
        }
        finally { InterfaceText.Initialize("en"); }
    });

    [Fact]
    public Task WorkerAudioReturnsToDispatcherAndSettingsRetireTheCapturedRequest() => dispatcher.RunAsync(async () =>
    {
        var owner = Dispatcher.CurrentDispatcher; var saved = new AppSettings();
        var settings = new LearningSpeechSettings(() => saved, () => { }, new NoCredentials());
        settings.SetEndpointBaseUrl("http://127.0.0.1:8888/v1"); settings.SetEndpointModel("fixture-speech"); settings.SetEndpointVoice("fixture-voice"); settings.SelectProvider(LearningSpeechProviderKind.Endpoint);
        var effects = new Effects(owner); var generator = new WorkerGenerator();
        var telemetry = new SpeechTelemetry((_, _, _) => Assert.True(owner.CheckAccess()));
        await using var controller = new LearningSpeechController(settings, telemetry, effects, generator, effects, effects, effects, effects,
            _ => new Dictionary<string, double>(), StringComparer.Ordinal, Path.GetTempPath(), () => effects);
        int changes = 0; controller.PropertyChanged += (_, _) => { Assert.True(owner.CheckAccess()); changes++; };
        var target = new SpeechTarget(Guid.NewGuid(), "word");
        controller.Toggle(new(target, "Hello.", LearningLanguage.English, LanguageMode: SpeechLanguageMode.Fixed));
        try
        {
            await generator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await generator.EmitFromWorkerAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(SpeechPlaybackPhase.Playing, controller.Phase); Assert.Equal(target, controller.ActiveTarget); Assert.Equal(1, effects.Played);
            settings.SetRateMultiplier(1.2);
            await controller.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(controller.ActiveTarget); Assert.True(generator.Cancelled); Assert.True(effects.Stops > 0);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.EmitFromWorkerAsync());
            Assert.Equal(1, effects.Played); Assert.True(changes > 0);
        }
        finally { controller.Shutdown(); await controller.DrainAsync(); }
    });

    private sealed class WorkerGenerator : ISpeechAudioGenerating
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<SpeechAudioChunk, Task>? _receive;
        internal bool Cancelled { get; private set; }
        public async Task GenerateAsync(SpeechAudioRequest request, Func<SpeechAudioChunk, Task> receive, CancellationToken cancellationToken)
        {
            _receive = receive; Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
        internal Task EmitFromWorkerAsync() => Task.Run(() => _receive!(new SpeechAudioChunk.Encoded(new byte[] { 1, 2, 3 })));
        public Task CancelAsync(SpeechPlaybackToken token) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
    }
    private sealed class Effects(Dispatcher owner) : ISpeechProviding, ISpeechAudioPlaying, ISpeechPCMPlaying, ISystemSpeechAudioExporting, ISpeechAudioDuration, ISpeechModelManagement
    {
        public Action<SpeechProviderEvent>? EventHandler { get; set; }
        public Action? DirectoryChanged { get; set; }
        public Func<IReadOnlySet<string>> ProtectedModelFilenames { get; set; } = () => new HashSet<string>();
        internal int Played, Stops;
        public void Play(ReadOnlySpan<byte> data, double rate, SpeechPlaybackToken token) { Assert.True(owner.CheckAccess()); Played++; EventHandler?.Invoke(new SpeechProviderEvent.Started(token)); }
        public void Stop() { Assert.True(owner.CheckAccess()); Stops++; }
        public void Stop(SpeechPlaybackToken token) => Stop();
        public void Shutdown() => Stop();
        public IReadOnlyList<SpeechVoice> RefreshVoices() => throw new InvalidOperationException("Unexpected voice enumeration");
        public void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token) => throw new InvalidOperationException("Unexpected system speech");
        public void Begin(int sampleRate, double rate, SpeechPlaybackToken token) => throw new InvalidOperationException("Unexpected PCM preparation");
        public Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected PCM");
        public Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected PCM EOF");
        public Task<SpeechExportAudio> AudioAsync(string text, string voiceId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected recording");
        public double Duration(ReadOnlySpan<byte> audio) => throw new InvalidOperationException("Unexpected duration inspection");
        public IReadOnlyList<RuntimeModelOption> InstalledModelOptions() => throw new InvalidOperationException("Unexpected model scan");
    }
    private sealed class NoCredentials : IOpenAICompatibleCredentialStore
    {
        public string? ReadApiKey() => null;
        public void SaveApiKey(string? value) => throw new InvalidOperationException("Unexpected credential write");
        public void DeleteApiKey() => throw new InvalidOperationException("Unexpected credential removal");
    }
}
