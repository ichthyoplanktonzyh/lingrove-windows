using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Cotabby.App.Learning;
using Cotabby.App.Settings;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Context;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.LanguageLearning.Local;
using Cotabby.Inference.OpenAICompatible;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class WritingPreviewTransactionTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task CompletedGenerationStillAllowsExplicitTranslationCopy() => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(); var window = await fixture.ReadyAsync();
        CopyButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => fixture.Clipboard.Values.Count == 1);
        Assert.Equal("bonjour", Assert.Single(fixture.Clipboard.Values));
        Assert.Equal(0, fixture.Inserter.Calls);
    });

    [Fact]
    public Task VerifiedReplacementWithdrawsSuggestionAndClosesOnlyAfterAcknowledgement() => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(); var window = await fixture.ReadyAsync();
        int withdrawals = 0; fixture.Controller.BeforeReplacement = () => withdrawals++;
        fixture.Inserter.BeforeDispatch = () => Assert.Equal(1, withdrawals);
        ((Button)window.FindName("ReplaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !window.IsVisible);
        Assert.Equal("bonjour", fixture.Focus.Current!.Text); Assert.Equal(1, fixture.Inserter.Calls);
        Assert.False(fixture.Controller.IsReplacing); Assert.Empty(fixture.Clipboard.Values);
    });

    [Fact]
    public Task UnverifiedEditBlocksRetryAndKeepsOnlyRecoveryCopy() => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(uncertain: true); var window = await fixture.ReadyAsync();
        var replace = (Button)window.FindName("ReplaceButton"); replace.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => ((TextBlock)window.FindName("ReplacementReason")).Text.Contains("Could not verify", StringComparison.Ordinal));
        Assert.True(window.IsVisible); Assert.False(replace.IsEnabled); Assert.Equal("bon", fixture.Focus.Current!.Text);
        replace.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, fixture.Inserter.Calls);
        var copy = CopyButton(window); copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => fixture.Clipboard.Values.Count == 1);
        Assert.Equal("bonjour", Assert.Single(fixture.Clipboard.Values));
        fixture.Focus.Publish(fixture.Focus.Current! with { Text = "bon!", CaretOffset = 4 });
        await Until(() => !window.IsVisible);
        copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(fixture.Clipboard.Values); Assert.Equal(1, fixture.Inserter.Calls);
    });

    [Fact]
    public Task TypingRevokesHeldReplacementBeforeDispatch() => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(holdInsert: true); var window = await fixture.ReadyAsync();
        ((Button)window.FindName("ReplaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await fixture.Inserter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(fixture.Controller.IsReplacing);
        fixture.Hook.Type(); fixture.Inserter.Release.TrySetResult();
        await Until(() => !fixture.Controller.IsReplacing);
        Assert.Equal("hello", fixture.Focus.Current!.Text); Assert.Equal(0, fixture.Inserter.Writes);
        Assert.False(window.IsVisible);
    });

    [Fact]
    public Task QueuedCopyCannotCommitAfterTheDocumentRetires() => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(holdCopy: true); var window = await fixture.ReadyAsync();
        CopyButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await fixture.Clipboard.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Hook.Type(); fixture.Clipboard.Release.Set();
        Assert.False(await fixture.Clipboard.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(fixture.Clipboard.Values); Assert.Equal(0, fixture.Inserter.Calls);
    });

    private static Button CopyButton(LearningPreviewWindow window) => ((Panel)window.FindName("CopyActions")).Children
        .OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Copy translation");

    private static async Task Until(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition()) { Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(7), "The writing transaction did not reach its expected state."); await Task.Delay(10); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Focus Focus { get; } = new();
        internal Hook Hook { get; } = new();
        internal Clipboard Clipboard { get; }
        internal Inserter Inserter { get; }
        internal WritingSentenceReplacement Owner { get; }
        internal LearningPreviewController Controller { get; }
        private readonly HttpClient _http = new(new Handler());

        internal Fixture(bool uncertain = false, bool holdInsert = false, bool holdCopy = false)
        {
            Clipboard = new(holdCopy); Inserter = new(Focus, uncertain, holdInsert);
            Owner = new(Inserter, _ => Focus.Refresh(), (_, _) => Task.CompletedTask);
            Controller = new(Hook, Focus, Dispatcher.CurrentDispatcher, new Tokenizer(), new Runtime(), _http,
                new Keys(), NullLogger<LearningPreviewController>.Instance, clipboardWriter: Clipboard);
            Controller.AttachReplacement(Owner);
            Controller.ApplySettings(new AppSettings { LearningTranslationEnabled = true, LearningSentencePreviewEnabled = false,
                LearningBackend = LearningTranslationBackend.Endpoint, LearningEndpointEnabled = true, LearningEndpointModel = "synthetic-model",
                LearningSourceLanguages = [LearningLanguage.English], LearningTargetLanguage = LearningLanguage.French }, 0);
        }
        internal async Task<LearningPreviewWindow> ReadyAsync()
        {
            Controller.RequestFromCurrentField(); LearningPreviewWindow? result = null;
            await Until(() => (result = Application.Current.Windows.OfType<LearningPreviewWindow>().FirstOrDefault(window => window.IsVisible
                && new TextRange(((TextBlock)window.FindName("ResultText")).ContentStart,
                    ((TextBlock)window.FindName("ResultText")).ContentEnd).Text == "bonjour")) is not null
                && ((Button)result.FindName("ReplaceButton")).IsEnabled);
            return result!;
        }
        public async ValueTask DisposeAsync()
        {
            Inserter.Release.TrySetResult(); Clipboard.Release.Set();
            try { await Controller.DisposeAsync(); }
            finally { try { await Owner.DisposeAsync(); } finally { Clipboard.Release.Dispose(); _http.Dispose(); } }
        }
    }
    private sealed class Focus : IFocusTracker
    {
        private FocusedField _current = new() { ProcessId = Environment.ProcessId + 1, ProcessName = "synthetic-editor", FieldId = "fixture",
            Text = "hello", CaretOffset = 5, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty, IsSingleLine = false,
            IsSecure = false, HasReliableSelection = true, IsTextWindowTruncated = false, FocusChangeSequence = 1,
            Composition = InputCompositionState.Clear, CompositionSequence = 1 };
        public FocusedField? Current => Volatile.Read(ref _current);
        public event EventHandler<FocusedField?>? FocusChanged;
        internal void Publish(FocusedField field) { Volatile.Write(ref _current, field); FocusChanged?.Invoke(this, field); }
        public FocusedField? Refresh() => Current;
        public void Start() { } public void Stop() { } public void Dispose() { }
    }
    private sealed class Hook : IKeyboardHook
    {
        public event EventHandler<KeyEventArgs>? KeyEvent;
        internal void Type() => KeyEvent?.Invoke(this, new(new KeyboardEvent { Kind = KeyKind.Character, Character = '!', IsKeyDown = true }));
        public void Start() { } public void Stop() { } public void Dispose() { }
    }
    private sealed class Inserter(Focus focus, bool uncertain, bool held) : IConditionalTextInserter
    {
        internal int Calls, Writes;
        internal Action? BeforeDispatch;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<bool> ReplaceWithPermitAsync(FocusedField target, int deleteCount, string text, Func<bool> permit, CancellationToken ct)
        {
            Calls++; Entered.TrySetResult(); if (held) await Release.Task;
            if (!permit()) return false;
            BeforeDispatch?.Invoke(); Writes++;
            string inserted = uncertain ? "bon" : text;
            focus.Publish(target with { Text = target.Text[..(target.CaretOffset - deleteCount)] + inserted + target.Text[target.CaretOffset..],
                CaretOffset = target.CaretOffset - deleteCount + inserted.Length });
            return true;
        }
    }
    private sealed class Clipboard(bool held) : IClipboardTextWriter
    {
        internal ConcurrentQueue<string> Values { get; } = new();
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);
        internal TaskCompletionSource<bool> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TryWrite(string text, Func<bool> tryCommit)
        {
            Entered.TrySetResult(); if (held) Assert.True(Release.Wait(TimeSpan.FromSeconds(10)));
            bool accepted = tryCommit(); if (accepted) Values.Enqueue(text); Completed.TrySetResult(accepted); return accepted;
        }
    }
    private sealed class Tokenizer : ILearningSentenceTokenizer
    { public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language) => [new(0, value.Length)]; }
    private sealed class Runtime : ILearningLocalRuntime
    {
        public Task UnloadAsync() => Task.CompletedTask;
        public Task<ILearningLocalOperation> BeginOperationAsync(LearningLocalModelConfiguration configuration, CancellationToken ct, CancellationToken ownerCancellation = default) => throw new InvalidOperationException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Keys : IOpenAICompatibleCredentialStore
    { public string? ReadApiKey() => null; public void SaveApiKey(string? value) => throw new InvalidOperationException(); public void DeleteApiKey() => throw new InvalidOperationException(); }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"bonjour\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n") });
    }
}
