using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Threading;
using Cotabby.App.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Speech;
using Cotabby.App.Settings;
using Cotabby.Core.Speech;
using Cotabby.Core.Models;
using Cotabby.Inference.Speech;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Win32.Speech;
using System.Net;
using System.Net.Http;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingPresentationTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ReadingRevocationAfterUiAdmissionBlocksActualEndpointSend() => dispatcher.RunAsync(async () =>
    {
        int live = 1, sent = 0; var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new LearningSpeechSettings(() => new AppSettings(), () => { }, new SpeechCredentials()); settings.SetEndpointBaseUrl("https://fixture.example/v1"); settings.SetEndpointModel("fixture"); settings.SetEndpointVoice("voice"); settings.SelectProvider(LearningSpeechProviderKind.Endpoint);
        var effects = new SpeechEffects(); await using var actual = new SpeechAudioGenerator(new LocalSpeechRuntime(new WindowsSpeechProcessFactory(), new WindowsSpeechMemoryPressure()), new SpeechEndpointClient(() => new CountSpeechHandler(() => Interlocked.Increment(ref sent))));
        var delayed = new DelayedSpeechGenerator(actual, entered, release);
        await using var speech = new LearningSpeechController(settings, new SpeechTelemetry((_, _, _) => { }), effects, delayed, effects, effects, effects, effects,
            _ => new System.Collections.Generic.Dictionary<string, double>(), StringComparer.Ordinal, Path.GetTempPath(), () => effects);
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(), (_, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException(), () => { }, new Clipboard(), new ReadingVocabularyStoreFake(), (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "Fixture sentence.", 1, "field", 1, 0, 17, false, true, InputCompositionState.Clear, 1, ScreenRect.Empty) { Admission = () => Volatile.Read(ref live) == 1 };
        try
        {
            presentation.AttachSpeech(speech); presentation.BindAuthority(selection, _ => Task.FromResult(true), default);
            await presentation.ShowAsync(new(selection, LearningLanguage.English, LearningLanguage.English, new(true, ReadingRouteReason.TerminalText, null, false), ReadingStatus.Ready, "Local model", "Result."));
            FindButton(presentation.ReadingCard, "Read source").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Volatile.Write(ref live, 0); release.SetResult(); await speech.DrainAsync(); Assert.Equal(0, sent);
        }
        finally { release.TrySetResult(); await presentation.DisposeAsync(); speech.Shutdown(); await speech.DrainAsync(); }
    });
    private sealed class CountSpeechHandler(Action send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { send(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }); } }
    private sealed class DelayedSpeechGenerator(ISpeechAudioGenerating inner, TaskCompletionSource entered, TaskCompletionSource release) : ISpeechAudioGenerating
    {
        public Task GenerateAsync(SpeechAudioRequest request, Func<SpeechAudioChunk, Task> receive, CancellationToken token) => Task.Run(async () => { entered.SetResult(); await release.Task; await inner.GenerateAsync(request, receive, token); });
        public Task CancelAsync(SpeechPlaybackToken token) => inner.CancelAsync(token); public Task ShutdownAsync() => inner.ShutdownAsync();
    }
    [Fact]
    public Task ReadingSpeechRetiresHeldAdmissionBeforeAnyProviderCall() => dispatcher.RunAsync(async () =>
    {
        var state = new AppSettings(); var settings = new LearningSpeechSettings(() => state, () => { }, new SpeechCredentials()); var effects = new SpeechEffects();
        await using var speech = new LearningSpeechController(settings, new SpeechTelemetry((_, _, _) => { }), effects, effects, effects, effects, effects, effects,
            _ => new System.Collections.Generic.Dictionary<string, double>(), StringComparer.Ordinal, Path.GetTempPath(), () => effects);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); bool hold = false; int checks = 0;
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(), (_, _) => throw new InvalidOperationException(),
            (_, _, _) => throw new InvalidOperationException(), () => { }, new Clipboard(), new ReadingVocabularyStoreFake(), (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "This is a fixture.", 1, "field", 1, 0, 18, false, true, InputCompositionState.Clear, 1, ScreenRect.Empty);
        Task<bool> Current(CancellationToken _) { checks++; return hold ? gate.Task : Task.FromResult(true); }
        try
        {
            presentation.AttachSpeech(speech); presentation.BindAuthority(selection, Current, default);
            var card = new ReadingState(selection, LearningLanguage.English, LearningLanguage.English, new(true, ReadingRouteReason.TerminalText, null, false), ReadingStatus.Ready, "Local model", "A translation.");
            await presentation.ShowAsync(card);
            VocabularyThemeRender.Capture(presentation.ReadingCard, "reading-speech", 400, 520);
            FindButton(presentation.ReadingCard, "Read source").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && effects.Starts == 0; i++) await Task.Delay(10); Assert.Equal(1, effects.Starts); Assert.Equal(selection.Text, effects.LastText);
            presentation.Hide(); presentation.BindAuthority(selection, Current, default); await presentation.ShowAsync(card);
            int previousChecks = checks; hold = true; FindButton(presentation.ReadingCard, "Read source").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && checks == previousChecks; i++) await Task.Delay(10); Assert.True(checks > previousChecks);
            presentation.Hide(); gate.SetResult(true); await Task.Delay(50); Assert.Equal(1, effects.Starts);
        }
        finally { gate.TrySetResult(false); await presentation.DisposeAsync(); speech.Shutdown(); await speech.DrainAsync(); }
    });
    private sealed class SpeechCredentials : IOpenAICompatibleCredentialStore
    { public string? ReadApiKey() => null; public void SaveApiKey(string? value) { } public void DeleteApiKey() { } }
    private sealed class SpeechEffects : ISpeechProviding, ISpeechAudioGenerating, ISpeechAudioPlaying, ISpeechPCMPlaying, ISystemSpeechAudioExporting, ISpeechAudioDuration, ISpeechModelManagement
    {
        public Action<SpeechProviderEvent>? EventHandler { get; set; }
        public Action? DirectoryChanged { get; set; }
        public Func<IReadOnlySet<string>> ProtectedModelFilenames { get; set; } = () => new HashSet<string>();
        internal int Starts; internal string? LastText;
        public IReadOnlyList<SpeechVoice> RefreshVoices() => [new("test", "Test", "en-US", SpeechVoiceQuality.Standard)];
        public void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token) { Starts++; LastText = text; }
        public void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token, Func<bool>? admission) { SpeechAdmission.Require(admission); Speak(text, voice, rateMultiplier, token); }
        public void Stop(SpeechPlaybackToken token) { } public void Stop() { } public void Shutdown() { }
        public void Play(ReadOnlySpan<byte> data, double rate, SpeechPlaybackToken token) => throw new NotSupportedException();
        public void Begin(int sampleRate, double rate, SpeechPlaybackToken token) => throw new NotSupportedException();
        public Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SpeechExportAudio> AudioAsync(string text, string voiceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public double Duration(ReadOnlySpan<byte> audio) => throw new NotSupportedException();
        public Task GenerateAsync(SpeechAudioRequest request, Func<SpeechAudioChunk, Task> receive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelAsync(SpeechPlaybackToken token) => Task.CompletedTask; public Task ShutdownAsync() => Task.CompletedTask;
        public IReadOnlyList<RuntimeModelOption> InstalledModelOptions() => [];
    }
    [Fact]
    public Task TranslationFavoriteAppearsOnlyForCompleteResultAndKeepsOriginalAndTranslation() => dispatcher.RunAsync(async () =>
    {
        var store = new ReadingVocabularyStoreFake();
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException(), () => { },
            new Clipboard(), store, (_, _) => [new(0, 12)], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "Hello world!", 1, "field", 1, 0, 12, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        var route = new ReadingRoute(true, ReadingRouteReason.TerminalText, null, false);
        var state = new ReadingState(selection, null, LearningLanguage.Chinese, route, ReadingStatus.Translating, "Local model", "partial", WordCount: 2);
        try
        {
            presentation.BindAuthority(selection, _ => Task.FromResult(true), default); await presentation.ShowAsync(state);
            var favorite = Descendants(presentation.ReadingCard).OfType<VocabularyFavoriteButton>().Single();
            Assert.Equal(Visibility.Collapsed, favorite.Visibility);
            presentation.Update(state with { Status = ReadingStatus.Ready, Text = "你好世界！" });
            for (int i = 0; i < 100 && favorite.Visibility != Visibility.Visible; i++) await Task.Delay(10);
            Assert.Equal(Visibility.Visible, favorite.Visibility); await favorite.LastOperation;
            favorite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await favorite.LastOperation;
            Assert.NotNull(store.Saved); Assert.Null(store.Saved.Language); Assert.Equal("Hello world!", store.Saved.Expression);
            Assert.Equal("你好世界！", store.Saved.Example!.Translation);
        }
        finally { await presentation.DisposeAsync(); }
    });
    [Fact]
    public Task AcceptedFavoriteWriteFinishesDuringPresentationShutdown() => dispatcher.RunAsync(async () =>
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ReadingVocabularyStoreFake { PendingSave = pending };
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => Task.FromResult(new ReadingLookupResult("河岸", "")), (_, _, _) => throw new InvalidOperationException(),
            () => { }, new Clipboard(), store, (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "bank", 1, "field", 1, 0, 4, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        var route = new ReadingRoute(false, ReadingRouteReason.StandaloneUnit, "bank", true);
        try
        {
            presentation.BindAuthority(selection, _ => Task.FromResult(true), default);
            await presentation.ShowAsync(new(selection, null, LearningLanguage.Chinese, route, ReadingStatus.Lookup, "Local model", ""));
            await presentation.LookupAsync(selection, route, null, LearningLanguage.Chinese, _ => Task.FromResult(true), default, []);
            var favorite = Descendants(presentation.ReadingCard).OfType<VocabularyFavoriteButton>().Single();
            await favorite.LastOperation; favorite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(store.Saved); var shutdown = presentation.DisposeAsync().AsTask(); Assert.False(shutdown.IsCompleted);
            pending.SetResult(true); await shutdown; Assert.Equal("河岸", store.Saved.Meaning);
        }
        finally { pending.TrySetResult(true); await presentation.DisposeAsync(); }
    });
    [Fact]
    public Task SourceAndCompletedResultCopyOnlyTheirOwnText() => dispatcher.RunAsync(async () =>
    {
        var clipboard = new RecordingClipboard();
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException(), () => { },
            clipboard, new ReadingVocabularyStoreFake(), (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "source", 1, "field", 1, 0, 6, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        presentation.BindAuthority(selection, _ => Task.FromResult(true), default);
        try
        {
            await presentation.ShowAsync(new(selection, null, LearningLanguage.Chinese,
                new(true, ReadingRouteReason.TerminalText, null, false), ReadingStatus.Ready, "Local model", "result"));
            FindButton(presentation.ReadingCard, "Copy source").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && clipboard.Values.Count == 0; i++) await Task.Delay(10);
            Assert.Equal("source", Assert.Single(clipboard.Values));
            FindButton(presentation.ReadingCard, "Copy result").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && clipboard.Values.Count < 2; i++) await Task.Delay(10);
            Assert.Equal(new[] { "source", "result" }, clipboard.Values);
        }
        finally { await presentation.DisposeAsync(); }
    });
    [Fact]
    public Task LookupFavoritePreservesAcceptedUnknownLanguageMeaning() => dispatcher.RunAsync(async () =>
    {
        var store = new ReadingVocabularyStoreFake();
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => Task.FromResult(new ReadingLookupResult("河岸", "")), (_, _, _) => throw new InvalidOperationException(),
            () => { }, new Clipboard(), store, (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "bank", 1, "field", 1, 0, 4, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        var route = new ReadingRoute(false, ReadingRouteReason.StandaloneUnit, "bank", true);
        try
        {
            presentation.BindAuthority(selection, _ => Task.FromResult(true), default);
            await presentation.ShowAsync(new(selection, null, LearningLanguage.Chinese, route, ReadingStatus.Lookup, "Local model", ""));
            await presentation.LookupAsync(selection, route, null, LearningLanguage.Chinese, _ => Task.FromResult(true), default, []);
            var favorite = Descendants(presentation.ReadingCard).OfType<VocabularyFavoriteButton>().Single();
            await favorite.LastOperation; favorite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await favorite.LastOperation;
            Assert.NotNull(store.Saved); Assert.Null(store.Saved.Language); Assert.Equal("河岸", store.Saved.Meaning);
            Assert.Equal(LearningLanguage.Chinese, store.Saved.MeaningLanguage);
        }
        finally { await presentation.DisposeAsync(); }
    });
    [Fact]
    public Task LookupFavoriteSavesAcceptedMeaningOnlyAfterCurrentReceiptPreflight() => dispatcher.RunAsync(async () =>
    {
        var store = new ReadingVocabularyStoreFake(); var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool hold = false; int checks = 0;
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => Task.FromResult(new ReadingLookupResult("河岸", "")), (_, _, _) => throw new InvalidOperationException(),
            () => { }, new Clipboard(), store, (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "bank", 1, "field", 1, 0, 4, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        var route = new ReadingRoute(false, ReadingRouteReason.StandaloneUnit, "bank", true);
        Task<bool> Current(CancellationToken _) { checks++; return hold ? gate.Task : Task.FromResult(true); }
        try
        {
            presentation.BindAuthority(selection, Current, default);
            await presentation.ShowAsync(new(selection, null, LearningLanguage.Chinese, route, ReadingStatus.Lookup, "Local model", ""));
            await presentation.LookupAsync(selection, route, null, LearningLanguage.Chinese, Current, default, []);
            var favorite = Descendants(presentation.ReadingCard).OfType<VocabularyFavoriteButton>().Single();
            await favorite.LastOperation; Assert.True(favorite.IsEnabled);
            hold = true; favorite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && checks < 3; i++) await Task.Delay(10);
            presentation.Hide(); gate.SetResult(true); await favorite.LastOperation;
            Assert.Null(store.Saved);
        }
        finally { gate.TrySetResult(false); await presentation.DisposeAsync(); }
    });
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task RetiredWithoutNotificationRejectsFavoriteAfterPreflight(bool changeProcess) => dispatcher.RunAsync(async () =>
    {
        var store = new ReadingVocabularyStoreFake(); var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool hold = false; int foreground = 10, process = 42; int checks = 0;
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => Task.FromResult(new ReadingLookupResult("河岸", "")), (_, _, _) => throw new InvalidOperationException(),
            () => { }, new Clipboard(), store, (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "bank", 1, "field", 1, 0, 4, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty) { Admission = () => foreground == 10 && process == 42 };
        var route = new ReadingRoute(false, ReadingRouteReason.StandaloneUnit, "bank", true);
        Task<bool> Current(CancellationToken _) { checks++; return hold ? gate.Task : Task.FromResult(true); }
        try
        {
            presentation.BindAuthority(selection, Current, default);
            await presentation.ShowAsync(new(selection, null, LearningLanguage.Chinese, route, ReadingStatus.Lookup, "Local model", ""));
            await presentation.LookupAsync(selection, route, null, LearningLanguage.Chinese, Current, default, []);
            var favorite = Descendants(presentation.ReadingCard).OfType<VocabularyFavoriteButton>().Single();
            await favorite.LastOperation; Assert.True(favorite.IsEnabled);
            hold = true; favorite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && checks < 3; i++) await Task.Delay(10);
            if (changeProcess) process = 43; else foreground = 11; gate.SetResult(true); await favorite.LastOperation;
            Assert.Null(store.Saved);
        }
        finally { gate.TrySetResult(false); await presentation.DisposeAsync(); }
    });
    [Fact]
    public Task CopyUsesCompleteResultAndRetiredReceiptCannotCommitClipboard() => dispatcher.RunAsync(async () =>
    {
        var clipboard = new GatedClipboard();
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException(), () => { }, clipboard, new ReadingVocabularyStoreFake(), (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "source", 1, "field", 1, 0, 6, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        using var cancellation = new CancellationTokenSource();
        presentation.BindAuthority(selection, _ => Task.FromResult(true), cancellation.Token);
        var state = new ReadingState(selection, null, LearningLanguage.Chinese,
            new(true, ReadingRouteReason.TerminalText, null, false), ReadingStatus.Ready, "Local model", "result");
        try
        {
            await presentation.ShowAsync(state);
            FindButton(presentation.ReadingCard, "Copy result").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await clipboard.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel(); presentation.Hide(); var disposing = presentation.DisposeAsync().AsTask();
            Assert.False(disposing.IsCompleted); clipboard.Release.Set(); await disposing;
            Assert.Equal("result", clipboard.Payload); Assert.False(clipboard.Committed);
        }
        finally { clipboard.Release.Set(); }
    });
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task RetiredWithoutNotificationRejectsFinalClipboardGrant(bool changeProcess) => dispatcher.RunAsync(async () =>
    {
        int foreground = 10, process = 42; var clipboard = new GatedClipboard();
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException(), () => { }, clipboard, new ReadingVocabularyStoreFake(), (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "source", 1, "field", 1, 0, 6, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty) { Admission = () => foreground == 10 && process == 42 };
        using var cancellation = new CancellationTokenSource();
        presentation.BindAuthority(selection, _ => Task.FromResult(true), cancellation.Token);
        var state = new ReadingState(selection, null, LearningLanguage.Chinese,
            new(true, ReadingRouteReason.TerminalText, null, false), ReadingStatus.Ready, "Local model", "result");
        try
        {
            await presentation.ShowAsync(state);
            FindButton(presentation.ReadingCard, "Copy result").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await clipboard.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (changeProcess) process = 43; else foreground = 11; clipboard.Release.Set();
            await clipboard.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await presentation.DisposeAsync();
            Assert.Equal("result", clipboard.Payload); Assert.False(clipboard.Committed);
        }
        finally { clipboard.Release.Set(); }
    });
    [Fact]
    public Task FailedBasicCanRetryAgainstTheSameReceipt() => dispatcher.RunAsync(async () =>
    {
        int calls = 0;
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => ++calls == 1 ? Task.FromException<ReadingLookupResult>(new InvalidOperationException())
                : Task.FromResult(new ReadingLookupResult("meaning", "")),
            (_, _, _) => throw new InvalidOperationException(), () => { }, new Clipboard(), new ReadingVocabularyStoreFake(), (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "bank", 1, "field", 1, 0, 4, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        var route = new ReadingRoute(false, ReadingRouteReason.StandaloneUnit, "bank", true);
        try
        {
            await presentation.ShowAsync(new(selection, null, LearningLanguage.Chinese, route, ReadingStatus.Lookup, "Local model", ""));
            await presentation.LookupAsync(selection, route, null, LearningLanguage.Chinese, _ => Task.FromResult(true), default, []);
            FindButton(presentation.ReadingCard, "Retry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && calls < 2; i++) await Task.Delay(10);
            Assert.Equal(2, calls); Assert.Contains("meaning", Text(presentation.ReadingCard));
        }
        finally { await presentation.DisposeAsync(); }
    });
    [Fact]
    public Task RevocationBeforeBasicCompletesCannotPublish() => dispatcher.RunAsync(async () =>
    {
        var pending = new TaskCompletionSource<ReadingLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => pending.Task, (_, _, _) => throw new InvalidOperationException(), () => { }, new Clipboard(), new ReadingVocabularyStoreFake(), (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "bank", 1, "field", 1, 0, 4, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        var route = new ReadingRoute(false, ReadingRouteReason.StandaloneUnit, "bank", true);
        await presentation.ShowAsync(new(selection, null, LearningLanguage.Chinese, route, ReadingStatus.Lookup, "Local model", ""));
        using var cancellation = new CancellationTokenSource();
        var work = presentation.LookupAsync(selection, route, null, LearningLanguage.Chinese, _ => Task.FromResult(true), cancellation.Token, []);
        cancellation.Cancel(); presentation.Hide(); pending.SetResult(new("late", "")); await work;
        Assert.False(presentation.ReadingCard.IsVisible); Assert.DoesNotContain("late", Text(presentation.ReadingCard));
        await presentation.DisposeAsync();
    });
    [Fact]
    public Task UnknownLookupShowsAcceptedMeaningThenDetailsAndRealDictionary() => dispatcher.RunAsync(async () =>
    {
        var dictionary = new Dictionary(); int basicCalls = 0, detailsCalls = 0;
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, dictionary,
            (input, _) => { Assert.Null(input.Source); basicCalls++; return Task.FromResult(new ReadingLookupResult("river bank", "")); },
            (input, basic, _) => { Assert.Equal("river bank", basic.Meaning); detailsCalls++; return Task.FromResult("A noun used for a river edge."); }, () => { }, new Clipboard(), new ReadingVocabularyStoreFake(), (_, _) => [], LearningLanguage.Chinese);
        var selection = new ReadingSelection(Guid.NewGuid(), "bank", 1, "field", 1, 0, 4, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        var route = new ReadingRoute(false, ReadingRouteReason.StandaloneUnit, "bank", true);
        var state = new ReadingState(selection, null, LearningLanguage.Chinese, route, ReadingStatus.Lookup, "Local explanation model", "");
        try
        {
            await presentation.ShowAsync(state);
            await presentation.LookupAsync(selection, route, null, LearningLanguage.Chinese, _ => Task.FromResult(true), default, [new("bank", new(0, 4))]);
            Assert.Equal(1, basicCalls); Assert.Contains("river bank", Text(presentation.ReadingCard));
            FindButton(presentation.ReadingCard, "Explain usage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && !Text(presentation.ReadingCard).Any(x => x.Contains("A noun used for a river edge.")); i++) await Task.Delay(10);
            Assert.Equal(1, detailsCalls); Assert.Contains(Text(presentation.ReadingCard), x => x.Contains("A noun used for a river edge."));
            if (Environment.GetEnvironmentVariable("LINGROVE_READING_LOOKUP_RENDER") is { Length: > 0 } path)
            {
                var window = presentation.ReadingCard; window.UpdateLayout();
                var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(path); encoder.Save(output);
            }
            FindButton(presentation.ReadingCard, "Hide details").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain(Text(presentation.ReadingCard), x => x.Contains("A noun used for a river edge."));
            FindButton(presentation.ReadingCard, "Explain usage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, detailsCalls); Assert.Contains(Text(presentation.ReadingCard), x => x.Contains("A noun used for a river edge."));
            FindButton(presentation.ReadingCard, "Dictionary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && !presentation.DictionaryCard.IsVisible; i++) await Task.Delay(10);
            Assert.True(presentation.DictionaryCard.IsVisible); Assert.Equal(1, dictionary.UnknownCalls);
            FindButton(presentation.DictionaryCard, "Back to meaning").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && !presentation.ReadingCard.IsVisible; i++) await Task.Delay(10);
            Assert.True(presentation.ReadingCard.IsVisible); Assert.Equal(1, basicCalls);
        }
        finally { await presentation.DisposeAsync(); }
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static string[] Text(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(x => x.Text).ToArray();
    private static Button FindButton(DependencyObject root, string title) => Descendants(root).OfType<Button>().Single(x => Equals(x.Content, title));
    private sealed class Clipboard : Cotabby.Core.Context.IClipboardTextWriter
    { public bool TryWrite(string text, Func<bool> tryCommit) => throw new InvalidOperationException(); }
    private sealed class GatedClipboard : Cotabby.Core.Context.IClipboardTextWriter
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release = new(); public string? Payload; public bool Committed;
        public bool TryWrite(string text, Func<bool> tryCommit)
        { Payload = text; Entered.TrySetResult(); Release.Wait(TimeSpan.FromSeconds(5)); Committed = tryCommit(); Finished.TrySetResult(); return Committed; }
    }
    private sealed class RecordingClipboard : Cotabby.Core.Context.IClipboardTextWriter
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Values = new();
        public bool TryWrite(string text, Func<bool> tryCommit) { if (!tryCommit()) return false; Values.Enqueue(text); return true; }
    }
    private sealed class Dictionary : ILearningDictionaryProviding, ILearningUnknownDictionaryProviding
    {
        public int UnknownCalls;
        public Task<LearningDictionaryResult> LookupUnknownAsync(string query, bool expression, CancellationToken token = default)
        { Assert.Equal("bank", query); UnknownCalls++; return Task.FromResult(LearningDictionaryResult.NotFound); }
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken token = default)
            => throw new InvalidOperationException();
    }
}
