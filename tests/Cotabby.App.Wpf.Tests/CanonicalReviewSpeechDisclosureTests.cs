using System.ComponentModel;
using System.IO;
using System.Windows;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.UI.Review;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Speech;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Inference.Speech;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalReviewSpeechDisclosureTests(WpfDispatcher dispatcher)
{
    private sealed class EndpointSpeech : ISpeechExporting, ISpeechDestinationDisclosure
    {
        internal Action? BeforeRequest, BeforeExport, OnStop; internal bool Fail; internal int Exports; internal string Configuration = "one";
        public string ExportConfigurationId => Configuration;
        public string DestinationDescription { get; private set; } = "Speech endpoint: https://speech.example · model: one. The selected text is sent to this server.";
        public bool IsDestinationAvailable { get; private set; } = true;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Change(string configuration, string text, bool available = true) { Configuration = configuration; DestinationDescription = text; IsDestinationAvailable = available; PropertyChanged?.Invoke(this, new(null)); }
        public SpeechExportRequest ExportRequest(string text, LearningLanguage? language, string? context = null) { BeforeRequest?.Invoke(); return new(text, language ?? throw new InvalidOperationException(), SpeechExportPolicy.Digest(System.Text.Encoding.UTF8.GetBytes(Configuration + text)),
            new SpeechExportRequest.Source.Generated(new SpeechAudioSource.Endpoint(new SpeechEndpointConfiguration(
                new OpenAICompatibleEndpointConfiguration(Configuration == "one" ? "https://speech.example/v1" : "https://new.example/v1", Configuration, OpenAICompatibleApiMode.ChatCompletions), "fake-voice"), null), new())); }
        public Task<SpeechExportAudio> ExportAudioAsync(SpeechExportRequest request, CancellationToken token = default) { BeforeExport?.Invoke(); Exports++; if (Fail) return Task.FromException<SpeechExportAudio>(new InvalidOperationException("temporary failure")); return Task.FromResult(SpeechExportPolicy.Wav(new byte[2400 * sizeof(float)], 24000)); }
        public void Audition(SpeechExportAudio audio, Guid ownerId, LearningLanguage language) { }
        public void Stop(Guid ownerId) { var callback = OnStop; OnStop = null; callback?.Invoke(); }
    }
    private sealed class OpaqueSpeech : ISpeechExporting
    {
        internal int Requests;
        public string ExportConfigurationId => "opaque";
        public SpeechExportRequest ExportRequest(string text, LearningLanguage? language, string? context = null) { Requests++; throw new InvalidOperationException(); }
        public Task<SpeechExportAudio> ExportAudioAsync(SpeechExportRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public void Audition(SpeechExportAudio audio, Guid ownerId, LearningLanguage language) { }
        public void Stop(Guid ownerId) { }
    }
    private static async Task Ready(CanonicalAnkiReviewWindow window) { if (window.LastOperation is { } operation) await operation; await window.PreviewOperation; }
    [Fact]
    public Task EndpointIsVisibleBeforeGenerationAndChangingSettingsRefreshesRetry() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-speech-disclosure-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var speech = new EndpointSpeech { Fail = true }; var server = new SessionTestServer();
            var controller = new AnkiReviewController(new(() => settings, () => { }, new AnkiTestCredentials()), null, server, speech: speech);
            var session = await CanonicalAnkiReviewSession.OpenAsync(controller, store, entry.Id, entry.Senses[0].Id);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            void Check() { Assert.True(window.SpeechDestination.IsVisible); Assert.Contains(speech.DestinationDescription, window.SpeechDestination.Text); }
            speech.BeforeRequest = Check; speech.BeforeExport = Check; Check();
            VocabularyThemeRender.Capture(window, "canonical-review-disclosure", 700, 710);
            window.ExpressionAudioToggle.IsChecked = true; window.ExpressionAudioToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await window.PreviewOperation;
            window.PreviewExpressionAudio.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Ready(window); Assert.Contains("temporary failure", session.Message);
            speech.Fail = false; speech.Change("two", "Speech endpoint: https://new.example · model: two. The selected text is sent to this server.");
            await window.PreviewOperation; Check(); window.PreviewExpressionAudio.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Ready(window);
            Assert.Null(session.Message); Assert.Equal(2, speech.Exports);
            speech.Change("bad", "Speech endpoint is not configured.", false); await window.PreviewOperation;
            Assert.Contains("unavailable", window.SpeechDestination.Text); int before = speech.Exports;
            window.PreviewExpressionAudio.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Ready(window); Assert.Equal(before, speech.Exports);
            await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task SynchronousStopChangingDestinationCannotStartOldExport() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-speech-stop-change-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var speech = new EndpointSpeech(); var server = new SessionTestServer();
            var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(new(() => settings, () => { }, new AnkiTestCredentials()), null, server, speech: speech), store, entry.Id, entry.Senses[0].Id);
            var window = new CanonicalAnkiReviewWindow(session, action => action(CancellationToken.None), () => { }); window.Show(); await Ready(window);
            window.ExpressionAudioToggle.IsChecked = true; window.ExpressionAudioToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await window.PreviewOperation;
            speech.OnStop = () => speech.Change("two", "Speech endpoint: https://new.example · model: two. The selected text is sent to this server.");
            window.PreviewExpressionAudio.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Ready(window);
            Assert.Equal(0, speech.Exports); Assert.Contains("new.example", window.SpeechDestination.Text); Assert.NotNull(session.Message);
            await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task BatchDisclosesEndpointBeforeEveryAudioRequest() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-disclosure-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var speech = new EndpointSpeech(); var server = new SessionTestServer();
            await using var feature = new AnkiReviewFeature(() => settings, () => { }, null, server, new AnkiTestCredentials(), speech: speech);
            Guid card = Guid.NewGuid(); using (var session = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, entry.Id, entry.Senses[0].Id, card))
            {
                var layout = session.Plan.Layout!; session.Plan = session.Plan with { Layout = layout with { Front = layout.Front.Append(ReviewCardPart.Create(ReviewContentSource.ExpressionAudio)).ToArray() } };
                Assert.True(await session.SaveAsync());
            }
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [new VocabularySelection(entry.Id, entry.Senses[0].Id)]);
            var window = new CanonicalAnkiReviewBatchWindow(batch, feature, () => { }); window.Show(); if (window.LastOperation is { } loading) await loading;
            void Check() { Assert.True(window.SpeechDestination.IsVisible); Assert.Contains(speech.DestinationDescription, window.SpeechDestination.Text); }
            speech.BeforeRequest = Check; speech.BeforeExport = Check; Check();
            VocabularyThemeRender.Capture(window, "canonical-review-batch-disclosure", 760, 740);
            window.SendButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); if (window.LastOperation is { } operation) await operation; if (batch.Work is { } work) await work;
            Assert.True(speech.Exports == 1, $"Exports={speech.Exports}; row={batch.Items.Single().Status} / {batch.Items.Single().Message}; window={window.MessageText.Text}"); Assert.True(server.AddCount == 1, $"Adds={server.AddCount}; row={batch.Items.Single().Status} / {batch.Items.Single().Message}; window={window.MessageText.Text}"); Assert.Equal(ReviewBatchStatus.Synced, batch.Items.Single().Status); await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task UndisclosedOwnerCannotGenerateOrSendAudio() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-speech-opaque-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var speech = new OpaqueSpeech(); var server = new SessionTestServer();
            using var session = await CanonicalAnkiReviewSession.OpenAsync(new AnkiReviewController(new(() => settings, () => { }, new AnkiTestCredentials()), null, server, speech: speech), store, entry.Id, entry.Senses[0].Id);
            session.Plan = session.Plan with { AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression } };
            Assert.False(await session.SendAsync()); await session.PrepareAudioAsync(ReviewAudioKind.Expression, true); Assert.Equal(0, speech.Requests); Assert.Empty(server.Calls);
            Assert.Contains("destination", session.Message!, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
