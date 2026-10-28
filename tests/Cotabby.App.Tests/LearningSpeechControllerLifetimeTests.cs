using System.Text.Json;
using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Tests;

public sealed partial class LearningSpeechControllerTests
{
    [Fact]
    public void CanonicalTargetsAndRequestsKeepHashIdentityAndGenerationRemainsEphemeral()
    {
        var owner = Guid.NewGuid(); var first = new SpeechTarget(owner, "café"); var second = new SpeechTarget(owner, "cafe\u0301");
        Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        var a = new SpeechRequest(first, "résumé", LearningLanguage.English, "café");
        var b = new SpeechRequest(second, "re\u0301sume\u0301", LearningLanguage.English, "cafe\u0301");
        Assert.Equal(a, b); Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Target = new(Guid.NewGuid(), second.ItemId) });
        Assert.NotEqual(a, b with { LanguageMode = SpeechLanguageMode.Fixed });
        var source = new SpeechAudioSource.Local(new(new Uri("file:///fixture/model.gguf"), new Uri("file:///fixture/decoder.gguf")));
        var generation = new SpeechAudioRequest(new(), "private clicked text", LearningLanguage.English, source, new(Temperature: 0.75));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(generation));
        Assert.DoesNotContain(generation.Text, generation.ToString()!, StringComparison.Ordinal);
        Assert.Same(source, generation.Source); Assert.Equal(0.75, generation.Sampling.Temperature);
    }

    [Fact]
    public async Task ThrowingCancellationCallbackCannotSkipStopOrTheAcceptedWorkDrain()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            await using var rig = new ControllerRig(); rig.Endpoint();
            rig.Generator.CancellationCallback = () => throw new InvalidOperationException("fixture cancellation callback");
            rig.Controller.Toggle(new(new(Guid.NewGuid(), "clicked"), "Hello.", LearningLanguage.English, LanguageMode: SpeechLanguageMode.Fixed));
            await WaitFlow(() => rig.Generator.Requests.Count == 1, deadline);
            rig.Controller.Stop();
            Assert.Null(rig.Controller.ActiveTarget); Assert.Null(rig.Controller.Phase);
            Assert.Single(rig.Provider.Stopped); Assert.True(rig.Pcm.Stops > 0);
            var error = await Assert.ThrowsAsync<AggregateException>(() => rig.Controller.DrainAsync());
            Assert.Contains(error.Flatten().InnerExceptions, e => e.Message == "fixture cancellation callback");
            Assert.Equal(1, rig.Generator.CancelledCount);
            await rig.Controller.DrainAsync(); // The reported failure is consumed only after all work joined.
        });
    }

    [Fact]
    public async Task CallerOwnedExportMustJoinBeforeDisposalAndPlaybackStopCannotCancelIt()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            await using var rig = new ControllerRig(); rig.Endpoint(); using var cancellation = new CancellationTokenSource();
            var request = rig.Controller.ExportRequest("Hello.", LearningLanguage.English);
            var export = rig.Controller.ExportAudioAsync(request, cancellation.Token);
            try
            {
                await WaitFlow(() => rig.Generator.Requests.Count == 1, deadline);
                await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.DisposeAsync().AsTask());
                rig.Controller.Stop(); await rig.Controller.DrainAsync(); Assert.False(export.IsCompleted); Assert.Equal(0, rig.Generator.CancelledCount);
                cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
                int changes = 0; rig.Controller.PropertyChanged += (_, _) => changes++;
                _ = rig.Controller.ModelDownloads;
                await rig.DisposeAsync();
                Assert.Null(rig.Provider.EventHandler); Assert.Null(rig.Pcm.EventHandler); Assert.Null(rig.Device.EventHandler);
                Assert.Null(rig.Models.DirectoryChanged); Assert.Empty(rig.Models.ProtectedModelFilenames());
                int afterDispose = changes; rig.Settings.SetRateMultiplier(1.2); Assert.Equal(afterDispose, changes);
            }
            finally { cancellation.Cancel(); try { await export; } catch (OperationCanceledException) { } }
        });
    }

    [Fact]
    public async Task CancelledExportKeepsExclusivityUntilAcceptedNativeDecodeJoins()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var duration = new HeldDuration(); await using var rig = new ControllerRig(duration); rig.Endpoint();
            using var cancellation = new CancellationTokenSource();
            var request = rig.Controller.ExportRequest("Hello.", LearningLanguage.English);
            Task<SpeechExportAudio> export = rig.Controller.ExportAudioAsync(request, cancellation.Token);
            try
            {
                await WaitFlow(() => rig.Generator.Requests.Count == 1, deadline);
                await rig.Generator.CompleteWaveAsync(request.Token); await duration.Entered.Task.WaitAsync(deadline);
                cancellation.Cancel(); Assert.False(export.IsCompleted);
                var error = await Assert.ThrowsAsync<SpeechException>(() => rig.Controller.ExportAudioAsync(request));
                Assert.Equal(SpeechFailure.OperationInProgress, error.Failure);
                await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.DisposeAsync().AsTask());
                duration.Release.TrySetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
                rig.Controller.Toggle(new(new(Guid.NewGuid(), "new"), "Hello.", LearningLanguage.English, LanguageMode: SpeechLanguageMode.Fixed));
                Assert.NotNull(rig.Controller.ActiveTarget); rig.Controller.Stop(); await rig.Controller.DrainAsync();
            }
            finally { duration.Release.TrySetResult(); cancellation.Cancel(); try { await export; } catch (OperationCanceledException) { } }
        });
    }

    [Fact]
    public async Task PublishedVoiceAndModelInventoriesAreDetachedUntilAnExplicitRefresh()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async _ =>
        {
            await using var rig = new ControllerRig();
            var voices = new List<SpeechVoice> { new("en", "English", "en-US", SpeechVoiceQuality.Standard) }; rig.Provider.Voices = voices;
            rig.Models.Models.Add(new("custom-a.gguf", new Uri("file:///fixture/custom-a.gguf")));
            rig.Controller.RefreshVoices(); rig.Controller.RefreshLocalModels();
            var oldVoices = rig.Controller.VoiceCatalog; var oldModels = rig.Controller.AvailableLocalModels;
            voices.Clear(); rig.Models.Models.Clear();
            Assert.Single(oldVoices); Assert.Single(rig.Controller.VoiceCatalog); Assert.Single(oldModels); Assert.Single(rig.Controller.AvailableLocalModels);
            rig.Controller.RefreshVoices(); rig.Models.DirectoryChanged!();
            Assert.Empty(rig.Controller.VoiceCatalog); Assert.Empty(rig.Controller.AvailableLocalModels); Assert.Single(oldVoices); Assert.Single(oldModels);
        });
    }

    /// <summary>One test owns all borrowed effects and joins the real controller before the UI
    /// queue closes. The inventory and generation gates are explicit fakes, never product defaults.</summary>
    private sealed class ControllerRig : IAsyncDisposable
    {
        internal LearningSpeechSettings Settings { get; }
        internal FlowProvider Provider { get; } = new();
        internal FlowGenerator Generator { get; } = new();
        internal FlowPCM Pcm { get; } = new();
        internal FlowDevice Device { get; } = new();
        internal MutableModels Models { get; } = new();
        internal LearningSpeechController Controller { get; }
        private bool _disposed;
        internal ControllerRig(ISpeechAudioDuration? duration = null)
        {
            var saved = new AppSettings(); Settings = new(() => saved, () => { }, new Credentials());
            Controller = new(Settings, new SpeechTelemetry((_, _, _) => { }), Provider, Generator, Device, Pcm, new FlowSystemExporter(), duration ?? Device,
                _ => new Dictionary<string, double>(), StringComparer.Ordinal, Path.GetTempPath(), () => Models);
        }
        internal void Endpoint() { Settings.SetEndpointBaseUrl("http://127.0.0.1:8888/v1"); Settings.SetEndpointModel("fixture-speech"); Settings.SetEndpointVoice("fixture-voice"); Settings.SelectProvider(LearningSpeechProviderKind.Endpoint); }
        public async ValueTask DisposeAsync() { if (!_disposed) { await Controller.DisposeAsync(); _disposed = true; } }
    }
    private sealed class HeldDuration : ISpeechAudioDuration
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public double Duration(ReadOnlySpan<byte> audio) => throw new InvalidOperationException("Native decoding must use its async boundary.");
        public async Task<double> DurationAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
        { Entered.TrySetResult(); await Release.Task; return .1; }
    }
    private sealed class MutableModels : ISpeechModelManagement
    {
        internal List<RuntimeModelOption> Models { get; } = [];
        public Action? DirectoryChanged { get; set; }
        public Func<IReadOnlySet<string>> ProtectedModelFilenames { get; set; } = () => new HashSet<string>();
        public IReadOnlyList<RuntimeModelOption> InstalledModelOptions() => Models;
    }
}
