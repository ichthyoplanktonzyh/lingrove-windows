using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Cotabby.App.Learning;
using Cotabby.App.Settings;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.LanguageLearning.Local;
using Cotabby.Inference.OpenAICompatible;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningPreviewInlineExclusionTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public Task TrayRequestDuringExpressionReadsNoFieldAndShowsNoWindow(bool translationEnabled) => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var runtime = new Runtime(); using var http = new HttpClient();
        await using var controller = Create(focus, runtime, http);
        controller.ApplySettings(new AppSettings { LearningTranslationEnabled = translationEnabled }, 0);
        controller.InlineCaptureActive = () => true;
        int windows = Application.Current.Windows.Count;
        controller.RequestFromCurrentField();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(0, focus.CurrentReads); Assert.Equal(0, focus.Refreshes); Assert.Equal(0, runtime.Begins);
        Assert.Equal(windows, Application.Current.Windows.Count);
    });

    [Fact]
    public Task AlreadyStartedReadCannotPublishAfterExpressionStarts() => dispatcher.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var focus = new Focus { RefreshStarted = entered, RefreshRelease = release }; var runtime = new Runtime(); using var http = new HttpClient();
        await using var controller = Create(focus, runtime, http);
        controller.ApplySettings(new AppSettings { LearningTranslationEnabled = true }, 0);
        int active = 0; controller.InlineCaptureActive = () => Volatile.Read(ref active) != 0;
        int windows = Application.Current.Windows.Count;
        try
        {
            controller.RequestFromCurrentField(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Volatile.Write(ref active, 1); release.Set();
            await Task.Delay(80); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(1, focus.Refreshes); Assert.Equal(0, runtime.Begins);
            Assert.Equal(windows, Application.Current.Windows.Count);
        }
        finally { release.Set(); }
    });

    [Fact]
    public Task QueuedCaptureRetiresBeforeReadingWhenExpressionStarts() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(); var runtime = new Runtime(); using var http = new HttpClient();
        await using var controller = Create(focus, runtime, http);
        controller.ApplySettings(new AppSettings { LearningTranslationEnabled = true }, 0);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); int active = 0;
        var ownerDispatcher = Dispatcher.CurrentDispatcher;
        controller.InlineCaptureActive = () =>
        {
            if (!ownerDispatcher.CheckAccess()) { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10)); returned.TrySetResult(); }
            return Volatile.Read(ref active) != 0;
        };
        int windows = Application.Current.Windows.Count;
        try
        {
            controller.RequestFromCurrentField();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Volatile.Write(ref active, 1); release.Set();
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(80); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(1, focus.CurrentReads); Assert.Equal(0, focus.Refreshes); Assert.Equal(0, runtime.Begins);
            Assert.Equal(windows, Application.Current.Windows.Count);
        }
        finally { release.Set(); }
    });

    private static LearningPreviewController Create(Focus focus, Runtime runtime, HttpClient http) =>
        new(new Hook(), focus, Dispatcher.CurrentDispatcher, new Tokenizer(), runtime, http, new Credentials(), NullLogger<LearningPreviewController>.Instance);

    [Fact]
    public Task ReadingUsesFrozenLearningEndpointAndUnknownSourceWithFakeTransport() => dispatcher.RunAsync(async () =>
    {
        using var handler = new ReadingHandler(); using var http = new HttpClient(handler);
        var runtime = new Runtime(); var controller = Create(new Focus(), runtime, http);
        var settings = new Cotabby.App.Settings.AppSettings { LearningTranslationEnabled = true,
            LearningBackend = Cotabby.App.Settings.LearningTranslationBackend.Endpoint, LearningEndpointEnabled = true,
            LearningEndpointBaseUrl = "http://127.0.0.1:11434/v1", LearningEndpointModel = "translategemma-4b" };
        var translator = controller.ReadingTranslator(settings, 0);
        var selection = new Cotabby.Core.LanguageLearning.Reading.ReadingSelection(Guid.NewGuid(), "A sentence.", 1, "field", 1, 0, 11,
            false, true, InputCompositionState.Clear, 1, ScreenRect.Empty);
        try
        {
            var prefixes = new List<string>();
            Assert.Equal("translated", await translator(selection, null, LearningLanguage.Chinese, prefixes.Add, default));
            Assert.Equal(new[] { "translated" }, prefixes); Assert.Contains("Infer the source language", handler.Body);
            Assert.Equal(0, runtime.Begins);
        }
        finally { await controller.DisposeAsync(); }
    });

    private sealed class ReadingHandler : HttpMessageHandler
    {
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"translated\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n") };
        }
    }
    private sealed class Focus : IFocusTracker
    {
        private readonly FocusedField _field = new() { ProcessId = Environment.ProcessId + 1, ProcessName = "editor", FieldId = "field",
            Text = "A sentence.", CaretOffset = 3, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty,
            IsSingleLine = true, IsSecure = false, HasReliableSelection = true, IsTextWindowTruncated = false,
            FocusChangeSequence = 1, Composition = InputCompositionState.Clear, CompositionSequence = 1 };
        public int CurrentReads, Refreshes; public TaskCompletionSource? RefreshStarted; public ManualResetEventSlim? RefreshRelease;
        public FocusedField? Current { get { Interlocked.Increment(ref CurrentReads); return _field; } }
        public event EventHandler<FocusedField?>? FocusChanged { add { } remove { } }
        public FocusedField? Refresh()
        {
            Interlocked.Increment(ref Refreshes); RefreshStarted?.TrySetResult();
            RefreshRelease?.Wait(TimeSpan.FromSeconds(10)); return _field;
        }
        public void Start() { } public void Stop() { } public void Dispose() { }
    }
    private sealed class Hook : IKeyboardHook
    {
        public event EventHandler<KeyEventArgs>? KeyEvent { add { } remove { } }
        public void Start() { } public void Stop() { } public void Dispose() { }
    }
    private sealed class Runtime : ILearningLocalRuntime
    {
        public int Begins;
        public Task UnloadAsync() => Task.CompletedTask;
        public Task<ILearningLocalOperation> BeginOperationAsync(LearningLocalModelConfiguration configuration, CancellationToken ct, CancellationToken ownerCancellation = default)
        { Interlocked.Increment(ref Begins); throw new InvalidOperationException(); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Tokenizer : ILearningSentenceTokenizer
    { public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language) => throw new InvalidOperationException(); }
    private sealed class Credentials : IOpenAICompatibleCredentialStore
    {
        public string? ReadApiKey() => null;
        public void SaveApiKey(string? value) => throw new InvalidOperationException();
        public void DeleteApiKey() => throw new InvalidOperationException();
    }
}
