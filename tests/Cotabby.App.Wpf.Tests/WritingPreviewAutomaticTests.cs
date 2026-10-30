using System.Net;
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
public sealed class WritingPreviewAutomaticTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task CommittedMutationWaitsForPauseThenUsesSelectedTaskEndpoint() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(Field("hello", 5)); var hook = new Hook(); using var handler = new Handler(); using var http = new HttpClient(handler);
        await using var controller = New(hook, focus, http); controller.ApplySettings(Settings(), 0);
        hook.Type(); focus.Publish(Field("hello!", 6));
        await Task.Delay(300); Assert.Equal(0, handler.Calls);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(1, handler.Calls);
        Assert.Contains("hello!", handler.Body);
    });

    [Fact]
    public Task NavigationAndUnknownCompositionDoNotStartPassiveRequests() => dispatcher.RunAsync(async () =>
    {
        var focus = new Focus(Field("hello", 5)); var hook = new Hook(); using var handler = new Handler(); using var http = new HttpClient(handler);
        await using var controller = New(hook, focus, http); controller.ApplySettings(Settings(), 0);
        hook.Type(); focus.Publish(Field("hello", 2));
        focus.Publish(Field("hello!", 6) with { Composition = InputCompositionState.Unknown, CompositionSequence = 2 });
        await Task.Delay(800); Assert.Equal(0, handler.Calls); Assert.Equal(0, focus.Refreshes);
    });

    [Fact]
    public Task ImeCommitUsesTheLastCommittedBaselineAndNeverSendsPreedit() => dispatcher.RunAsync(async () =>
    {
        var baseline = Field("", 0); var focus = new Focus(baseline); var hook = new Hook();
        using var handler = new Handler(); using var http = new HttpClient(handler);
        await using var controller = New(hook, focus, http);
        var settings = Settings(); settings.LearningSourceLanguage = LearningLanguage.Chinese; settings.LearningTargetLanguage = LearningLanguage.English;
        controller.ApplySettings(settings, 0); hook.Type();
        focus.Publish(baseline with { Text = "你", CaretOffset = 1, Composition = InputCompositionState.Composing, CompositionSequence = 2 });
        await Task.Delay(700); Assert.Equal(0, handler.Calls);
        focus.Publish(baseline with { Text = "你好", CaretOffset = 2, CompositionSequence = 3 });
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(1, handler.Calls);
    });

    [Fact]
    public Task CancelledPreeditDoesNotBecomeACommittedMutation() => dispatcher.RunAsync(async () =>
    {
        var baseline = Field("", 0); var focus = new Focus(baseline); var hook = new Hook();
        using var handler = new Handler(); using var http = new HttpClient(handler);
        await using var controller = New(hook, focus, http); controller.ApplySettings(Settings(), 0); hook.Type();
        focus.Publish(baseline with { Text = "preedit", CaretOffset = 7, Composition = InputCompositionState.Composing, CompositionSequence = 2 });
        focus.Publish(baseline with { CompositionSequence = 3 });
        await Task.Delay(800); Assert.Equal(0, handler.Calls);
    });

    private static AppSettings Settings() => new() { LearningTranslationEnabled = true, LearningBackend = LearningTranslationBackend.Endpoint,
        LearningEndpointEnabled = true, LearningEndpointModel = "synthetic-model", LearningSourceLanguage = LearningLanguage.English,
        LearningTargetLanguage = LearningLanguage.Chinese, LearningSentencePreviewEnabled = true };
    private static LearningPreviewController New(Hook hook, Focus focus, HttpClient http) => new(hook, focus, Dispatcher.CurrentDispatcher,
        new Tokenizer(), new Runtime(), http, new Credentials(), NullLogger<LearningPreviewController>.Instance);
    private static FocusedField Field(string value, int caret) => new() { ProcessId = Environment.ProcessId + 1, ProcessName = "synthetic-editor",
        FieldId = "fixture", Text = value, CaretOffset = caret, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty,
        IsSingleLine = false, IsSecure = false, HasReliableSelection = true, IsTextWindowTruncated = false,
        FocusChangeSequence = 1, Composition = InputCompositionState.Clear, CompositionSequence = 1 };
    private sealed class Focus(FocusedField initial) : IFocusTracker
    {
        private FocusedField _field = initial; public int Refreshes;
        public FocusedField? Current => _field;
        public event EventHandler<FocusedField?>? FocusChanged;
        public void Publish(FocusedField field) { _field = field; FocusChanged?.Invoke(this, field); }
        public FocusedField? Refresh() { Interlocked.Increment(ref Refreshes); return _field; }
        public void Start() { } public void Stop() { } public void Dispose() { }
    }
    private sealed class Hook : IKeyboardHook
    {
        public event EventHandler<KeyEventArgs>? KeyEvent;
        public void Type() => KeyEvent?.Invoke(this, new(new KeyboardEvent { Kind = KeyKind.Character, Character = '!', IsKeyDown = true }));
        public void Start() { } public void Stop() { } public void Dispose() { }
    }
    private sealed class Tokenizer : ILearningSentenceTokenizer
    { public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language) => [new(0, value.Length)]; }
    private sealed class Runtime : ILearningLocalRuntime
    {
        public Task UnloadAsync() => Task.CompletedTask;
        public Task<ILearningLocalOperation> BeginOperationAsync(LearningLocalModelConfiguration configuration, CancellationToken ct, CancellationToken ownerCancellation = default) => throw new InvalidOperationException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Credentials : IOpenAICompatibleCredentialStore
    { public string? ReadApiKey() => null; public void SaveApiKey(string? value) => throw new InvalidOperationException(); public void DeleteApiKey() => throw new InvalidOperationException(); }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public string Body = ""; public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); Body = await request.Content!.ReadAsStringAsync(token); Started.TrySetResult();
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"你好\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n") };
        }
    }
}
