using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.App.UI.Settings;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Core.Speech;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
[SupportedOSPlatform("windows10.0")]
public sealed class SpeechConfigurationWindowTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task FeatureConstructionAndRetirementStayLazy() => dispatcher.RunAsync(async () =>
    {
        var app = new AppSettings(); var credentials = new Credentials(); int saves = 0;
        string directory = Path.Combine(Path.GetTempPath(), "speech-lazy-" + Guid.NewGuid().ToString("N"));
        var feature = new SpeechFeature(() => app, () => saves++, credentials, directory, new Effects());
        Assert.False(feature.Controller.CatalogIsLoaded); Assert.False(Directory.Exists(directory)); Assert.Equal(0, saves);
        await feature.DisposeAsync(); Assert.False(Directory.Exists(directory));
    });
    [Fact]
    public Task SettingsEnumerateOnOpenAndAuditionOnlyOnClick() => dispatcher.RunAsync(async () =>
    {
        var app = new AppSettings(); int writes = 0; var settings = new LearningSpeechSettings(() => app, () => writes++, new Credentials()); var effects = new Effects();
        await using var controller = new LearningSpeechController(settings, new SpeechTelemetry((_, _, _) => { }), effects, effects, effects, effects, effects, effects,
            _ => new Dictionary<string, double>(), StringComparer.Ordinal, Path.GetTempPath(), () => effects);
        var window = new SpeechConfigurationWindow(controller); Assert.Equal(0, effects.Catalogs); Assert.Equal(0, effects.Speaks);
        window.Show(); Assert.Equal(1, effects.Catalogs); Assert.Equal(0, effects.Speaks);
        Assert.Contains(Descendants(window).OfType<ComboBox>(), combo => combo.SelectedItem?.ToString() == "Test voice");
        VocabularyThemeRender.Capture(window, "speech-configuration", 700, 680);
        var button = Descendants(window).OfType<Button>().Single(b => b.Name == "speechTestVoice"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, effects.Speaks); Assert.Equal("Hello. This is a voice preview.", effects.Text); Assert.Equal(0, writes);
        settings.SetEndpointBaseUrl("https://example.org/v1"); settings.SetEndpointModel("test/model"); settings.SetEndpointVoice("voice"); settings.SelectProvider(LearningSpeechProviderKind.Endpoint);
        Assert.Contains(Descendants(window).OfType<TextBlock>(), text => text.Text.Contains("https://example.org", StringComparison.Ordinal) && text.Text.Contains("test/model", StringComparison.Ordinal));
        window.Close(); Assert.True(effects.Stopped); controller.Shutdown(); await controller.DrainAsync();
    });
    [Fact]
    public Task PreviewOnlySpeaksCompleteTranslationAndStopsOnReplacement() => dispatcher.RunAsync(async () =>
    {
        var app = new AppSettings(); var settings = new LearningSpeechSettings(() => app, () => { }, new Credentials()); var effects = new Effects();
        await using var controller = new LearningSpeechController(settings, new SpeechTelemetry((_, _, _) => { }), effects, effects, effects, effects, effects, effects,
            _ => new Dictionary<string, double>(), StringComparer.Ordinal, Path.GetTempPath(), () => effects);
        var window = new LearningPreviewWindow(); window.AttachSpeech(controller); var anchor = new ScreenRect(50, 50, 10, 10);
        var state = new LearningPreviewState { RequestId = "first", Source = "Original sentence", PartialResult = "provisional", Status = LearningPreviewStatus.Loading,
            Options = new LearningTranslationOptions { SourceLanguage = LearningLanguage.English, TargetLanguage = LearningLanguage.English } };
        window.ShowState(state, anchor, false);
        Assert.Equal(Visibility.Collapsed, window.ResultSpeechButton.Visibility); Assert.Equal(0, effects.Speaks);
        window.ShowState(state with { Status = LearningPreviewStatus.Ready, Result = "Finished sentence" }, anchor, false);
        window.ResultSpeechButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("Finished sentence", effects.Text);
        window.ShowState(state with { RequestId = "second", Status = LearningPreviewStatus.Ready, Result = "Another sentence" }, anchor, false);
        Assert.True(effects.Stopped);
        window.ShowState(state with { RequestId = "third", Status = LearningPreviewStatus.Ready, Result = "Another sentence", Options = state.Options! with { SourceLanguage = null } }, anchor, false);
        int before = effects.Speaks; window.SourceSpeechButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(before, effects.Speaks); Assert.Equal(Visibility.Visible, window.SpeechStatus.Visibility);
        window.HidePreview(); window.Close(); controller.Shutdown(); await controller.DrainAsync();
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class Credentials : IOpenAICompatibleCredentialStore
    { public string? ReadApiKey() => null; public void SaveApiKey(string? value) { } public void DeleteApiKey() { } }
    private sealed class Effects : ISpeechProviding, ISpeechAudioPlaying, ISpeechPCMPlaying, ISystemSpeechAudioExporting, ISpeechAudioDuration, ISpeechAudioGenerating, ISpeechModelManagement
    {
        public Action<SpeechProviderEvent>? EventHandler { get; set; }
        public Action? DirectoryChanged { get; set; }
        public Func<IReadOnlySet<string>> ProtectedModelFilenames { get; set; } = () => new HashSet<string>();
        internal int Catalogs, Speaks; internal string? Text; internal bool Stopped;
        public IReadOnlyList<SpeechVoice> RefreshVoices() { Catalogs++; return [new("test", "Test voice", "en-US", SpeechVoiceQuality.Standard)]; }
        public void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token) { Speaks++; Text = text; EventHandler?.Invoke(new SpeechProviderEvent.Started(token)); }
        public void Stop(SpeechPlaybackToken token) => Stopped = true;
        public void Stop() { }
        public void Play(ReadOnlySpan<byte> data, double rate, SpeechPlaybackToken token) => throw new NotSupportedException();
        public void Begin(int sampleRate, double rate, SpeechPlaybackToken token) => throw new NotSupportedException();
        public Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Shutdown() { }
        public Task<SpeechExportAudio> AudioAsync(string text, string voiceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public double Duration(ReadOnlySpan<byte> audio) => throw new NotSupportedException();
        public Task GenerateAsync(SpeechAudioRequest request, Func<SpeechAudioChunk, Task> receive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelAsync(SpeechPlaybackToken token) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public IReadOnlyList<RuntimeModelOption> InstalledModelOptions() => [];
    }
}
