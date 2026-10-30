using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    public Task UnsupportedNativeRangeShowsCopyRecoveryInsteadOfSilentlyReenablingReplace() => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(draft: "hello", range: new(0, 5), rangeEdits: true, rejectRange: true);
        var window = await fixture.ReadyAsync(); var replace = (Button)window.FindName("ReplaceButton");
        replace.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => ((TextBlock)window.FindName("ReplacementReason")).Text.Contains("No text was replaced", StringComparison.Ordinal));
        Capture(window, "writing-range-unavailable");
        Assert.False(replace.IsEnabled); Assert.Equal("hello", fixture.Focus.Current!.Text);
        CopyButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => fixture.Clipboard.Values.Count == 1);
        Assert.Equal("bonjour", Assert.Single(fixture.Clipboard.Values)); Assert.Equal(0, fixture.Inserter.Calls);
    });
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task UnicodeSentenceAndExplicitSelectionUseTheRealCardRangeTransaction(bool selected) => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(draft: "前面。你好😀世界。后面。", range: new(3, 7), rangeEdits: true,
            caret: selected ? 3 : 5, selectedLength: selected ? 7 : 0);
        var window = await fixture.ReadyAsync("你好😀世界。");
        Capture(window, selected ? "writing-range-selected" : "writing-range-middle-caret");
        using (var body = System.Text.Json.JsonDocument.Parse(fixture.Transport.Body))
            Assert.Contains(body.RootElement.GetProperty("messages").EnumerateArray(),
                message => message.GetProperty("content").GetString()!.Contains("你好", StringComparison.Ordinal));
        ((Button)window.FindName("ReplaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !window.IsVisible);
        Assert.Equal("前面。bonjour后面。", fixture.Focus.Current!.Text);
        Assert.Equal(0, fixture.Inserter.Calls); Assert.Empty(fixture.Clipboard.Values);
    });
    [Fact]
    public Task CompletedGenerationStillAllowsExplicitTranslationCopy() => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(); var window = await fixture.ReadyAsync();
        CopyButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => fixture.Clipboard.Values.Count == 1);
        Assert.Equal("bonjour", Assert.Single(fixture.Clipboard.Values));
        Assert.Equal(0, fixture.Inserter.Calls);
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task OnlyExplicitDraftModeSendsNearestReferenceWithoutChangingTheTarget(bool enabled) => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(draft: "earlier. hello later.", range: new(9, 5),
            referenceMode: enabled ? LearningReferenceMode.Draft : LearningReferenceMode.CurrentSentence);
        var window = await fixture.ReadyAsync();
        Capture(window, enabled ? "writing-draft-expanded" : "writing-current-expanded");
        Assert.Equal(enabled, fixture.Transport.Body.Contains("REFERENCE EXCERPTS (JSON)", StringComparison.Ordinal));
        Assert.Equal(enabled, fixture.Transport.Body.Contains("earlier.", StringComparison.Ordinal));
        Assert.Equal(enabled, fixture.Transport.Body.Contains("later.", StringComparison.Ordinal));
        Assert.Contains("hello", fixture.Transport.Body); Assert.Equal(0, fixture.Inserter.Calls);
    });

    [Fact]
    public Task PreviousTranslationRequiresItsUnchangedPrecedingSource() => dispatcher.RunAsync(async () =>
    {
        await using var fixture = new Fixture(); await fixture.ReadyAsync();
        fixture.Hook.Type(); fixture.Focus.Publish(fixture.Focus.Current! with { Text = "hello. second", CaretOffset = 13 });
        var window = await fixture.ReadyAsync("second", "deuxième");
        ((Button)window.FindName("ExpandButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => ((TextBlock)window.FindName("PreviousTranslation")).Visibility == Visibility.Visible);
        Assert.Equal("bonjour", ((TextBlock)window.FindName("PreviousTranslation")).Text);
        Capture(window, "writing-compact-previous");
        fixture.Hook.Type(); fixture.Focus.Publish(fixture.Focus.Current! with { Text = "other. second", CaretOffset = 13 });
        window = await fixture.ReadyAsync("second", "deuxième");
        ((Button)window.FindName("ExpandButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => ((FrameworkElement)window.FindName("SourceSection")).Visibility == Visibility.Collapsed);
        Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("PreviousTranslation")).Visibility);
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

    private static void Capture(LearningPreviewWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_WRITING_RENDER_DIRECTORY") is not { Length: > 0 } directory) return;
        var view = (FrameworkElement)window.Content; window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new System.IO.FileStream(System.IO.Path.Combine(directory, name + ".png"), System.IO.FileMode.CreateNew);
        png.Save(output);
    }

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
        internal Handler Transport { get; } = new();
        private readonly HttpClient _http;

        internal Fixture(bool uncertain = false, bool holdInsert = false, bool holdCopy = false, string? draft = null,
            LearningTextRange? range = null, LearningReferenceMode referenceMode = LearningReferenceMode.CurrentSentence,
            bool rangeEdits = false, int? caret = null, int selectedLength = 0, bool rejectRange = false)
        {
            _http = new(Transport);
            if (draft is not null) Focus.Publish(Focus.Current! with { Text = draft, CaretOffset = caret ?? range!.Value.Location + range.Value.Length,
                HasSelection = selectedLength > 0, SelectionLength = selectedLength, SupportsRangeReplacement = rangeEdits });
            Clipboard = new(holdCopy); Inserter = new(Focus, uncertain, holdInsert);
            Owner = new(Inserter, _ => Focus.Refresh(), (_, _) => Task.CompletedTask, ranges: rangeEdits ? new RangeInserter(Focus, rejectRange) : null);
            Controller = new(Hook, Focus, Dispatcher.CurrentDispatcher, new Tokenizer(range), new Runtime(), _http,
                new Keys(), NullLogger<LearningPreviewController>.Instance, clipboardWriter: Clipboard);
            Controller.AttachReplacement(Owner);
            Controller.ApplySettings(new AppSettings { LearningTranslationEnabled = true, LearningSentencePreviewEnabled = false,
                LearningBackend = LearningTranslationBackend.Endpoint, LearningEndpointEnabled = true, LearningEndpointModel = "synthetic-model",
                LearningSourceLanguages = [LearningLanguage.English], LearningTargetLanguage = LearningLanguage.French,
                LearningReferenceMode = referenceMode }, 0);
        }
        internal async Task<LearningPreviewWindow> ReadyAsync(string expectedSource = "hello", string expectedTranslation = "bonjour")
        {
            Controller.RequestFromCurrentField(); LearningPreviewWindow? result = null;
            await Until(() => (result = Application.Current.Windows.OfType<LearningPreviewWindow>().FirstOrDefault(window => window.IsVisible
                && new TextRange(((TextBlock)window.FindName("ResultText")).ContentStart,
                    ((TextBlock)window.FindName("ResultText")).ContentEnd).Text == expectedTranslation
                && ((TextBlock)window.FindName("SourceText")).Text == expectedSource)) is not null
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
    private sealed class RangeInserter(Focus focus, bool reject) : IRangeTextInserter
    {
        public Task<RangeEditDispatch> ReplaceRangeAsync(FocusedField target, LearningTextRange range, string source, string replacement,
            Func<bool> permit, CancellationToken ct)
        {
            if (reject || !permit() || ct.IsCancellationRequested || !WritingRangeReplacementPolicy.IsCurrent(target, focus.Current)) return Task.FromResult(RangeEditDispatch.Rejected);
            focus.Publish(target with { Text = target.Text[..range.Location] + replacement + target.Text[(range.Location + range.Length)..],
                CaretOffset = range.Location + replacement.Length, HasSelection = false, SelectionLength = 0 });
            return Task.FromResult(RangeEditDispatch.Dispatched);
        }
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
    private sealed class Tokenizer(LearningTextRange? range = null) : ILearningSentenceTokenizer
    {
        public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language)
        {
            if (range is { } exact) return [exact];
            int split = value.IndexOf(". ", StringComparison.Ordinal);
            return split < 0 ? [new(0, value.Length)] : [new(0, split + 1), new(split + 2, value.Length - split - 2)];
        }
    }
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
        internal string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await request.Content!.ReadAsStringAsync(token);
            string translation = Body.Contains("second", StringComparison.Ordinal) ? "deuxième" : "bonjour";
            const string response = "data: {\"choices\":[{\"delta\":{\"content\":\"bonjour\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response.Replace("\"bonjour\"", System.Text.Json.JsonSerializer.Serialize(translation), StringComparison.Ordinal)) };
        }
    }
}
