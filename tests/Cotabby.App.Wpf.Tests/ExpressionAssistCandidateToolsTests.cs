using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.App.UI;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Core.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ExpressionAssistCandidateToolsTests(WpfDispatcher dispatcher)
{
    private static readonly ExpressionAssistCandidate Candidate = new("kept me posted", "让我随时了解最新情况", "keep someone posted");
    private static readonly ExpressionAssistRequest Request = new("随时了解情况", "Thanks, she ", " throughout the week.", "", "en", "zh-Hans");
    private static readonly ExpressionAssistResult Result = new([Candidate, new("updated me", "另一种表达", "update")], null);
    private static VocabularyCaptureFactory Factory(Facts? facts = null) { facts ??= new(); return new(facts, facts); }

    [Fact]
    public Task CandidateSaveAndExactRemoveUseTheRealCanonicalStoreWithoutInserting() => dispatcher.RunAsync(async () =>
    {
        await using var store = new LearningShowcaseStore(Dispatcher.CurrentDispatcher);
        var speechOwner = new LearningShowcaseSpeech(); await using var speech = speechOwner.CreateController();
        var view = new ExpressionAssistView(); var window = Host(view); int insertions = 0; view.Accept += _ => insertions++;
        await using var tools = new ExpressionAssistCandidateTools(Dispatcher.CurrentDispatcher, store.Writing, Factory(), speech,
            () => window.IsVisible && !view.IsComposing, _ => Task.FromResult(true));
        try
        {
            window.Show(); view.AttachTools(tools); tools.Update(Request, Result); view.SetQuery(Request.Query); view.Update(Result, null, false);
            await Until(() => Descendants<VocabularyFavoriteButton>(view).FirstOrDefault()?.IsEnabled == true);
            var favorite = Descendants<VocabularyFavoriteButton>(view).First(); var capture = tools.Capture(0, Candidate)!;
            var originalId = capture.Example!.Id;
            Click(favorite); await favorite.LastOperation;
            Assert.True(store.Writing.Contains(capture)); Assert.Equal("Saved", AutomationProperties.GetItemStatus(favorite));
            Assert.Equal("Remove this saved usage", AutomationProperties.GetName(favorite));
            var snapshot = await store.Backend.ReadAsync();
            var example = Assert.Single(Assert.Single(snapshot.Document.CollectedEntries).Examples);
            Assert.Equal(originalId, example.Id); Assert.Equal(Cotabby.Core.Vocabulary.Resources.VocabularySourceKind.Generated, example.SourceKind);
            Assert.Equal(VocabularyMeaningOrigin.Ai, example.MeaningOrigin); Assert.Equal(Candidate.Hint, example.Meaning);
            Assert.Equal("kept me posted", example.Text.Substring(example.Ranges[0].Location, example.Ranges[0].Length));
            Assert.Equal(0, insertions); Assert.Equal(Request.Query, view.QueryBox.Text);
            Assert.Null(speech.ActiveTarget); Render(view, window);

            var down = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view.QueryBox), 0, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            view.QueryBox.RaiseEvent(down); Assert.True(down.Handled);
            Assert.Equal(originalId, tools.Capture(0, Candidate)!.Example!.Id);
            var other = Factory().FromExpressionAssist(Candidate, Request with { LeftContext = "Yesterday she " })!;
            Assert.True(await store.Writing.SaveAsync(other));
            favorite = Descendants<VocabularyFavoriteButton>(view).First(); await Until(() => favorite.IsEnabled);
            Click(favorite); await favorite.LastOperation;
            Assert.Null(favorite.ContextMenu); Assert.False(store.Writing.Contains(capture)); Assert.True(store.Writing.Contains(other));
            Assert.Equal(0, insertions); Assert.Equal(Request.Query, view.QueryBox.Text);
        }
        finally { view.DetachTools(); window.Close(); }
    });

    [Fact]
    public Task ExplicitSpeechUsesItsOwnRowTargetAndNeverAcceptsOrStartsOnRender() => dispatcher.RunAsync(async () =>
    {
        var effects = new LearningShowcaseSpeech(); var provider = new SpeechSpy(); var settings = new AppSettings();
        var preferences = new LearningSpeechSettings(() => settings, () => { }, effects, effects);
        await using var speech = new LearningSpeechController(preferences, new SpeechTelemetry((_, _, _) => { }), provider,
            effects, effects, effects, effects, effects, _ => new Dictionary<string, double>(), StringComparer.Ordinal, "", () => effects, effects);
        var view = new ExpressionAssistView(); var window = Host(view); int insertions = 0; view.Accept += _ => insertions++;
        await using var tools = new ExpressionAssistCandidateTools(Dispatcher.CurrentDispatcher, null, null, speech,
            () => window.IsVisible && !view.IsComposing, _ => Task.FromResult(true));
        try
        {
            window.Show(); view.AttachTools(tools); tools.Update(Request, Result); view.SetQuery(Request.Query); view.Update(Result, null, false); window.UpdateLayout();
            Assert.Empty(provider.Spoken);
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text.Contains("System voice", StringComparison.Ordinal));
            var play = Descendants<Button>(view).First(button => AutomationProperties.GetName(button) == "Play expression aloud");
            Assert.False(play.Focusable); Click(play);
            Assert.Equal("kept me posted", Assert.Single(provider.Spoken)); Assert.Equal(tools.Target(0), speech.ActiveTarget);
            Assert.Equal("Stop speaking expression", AutomationProperties.GetName(play));
            Click(play); Assert.Null(speech.ActiveTarget); Assert.Equal(1, provider.Stops);
            Click(play); await tools.DisposeAsync();
            Assert.Equal(tools.Target(0), speech.ActiveTarget); // Accepted playback, like Mac, can outlive its panel.
            Assert.Equal(0, insertions); Assert.Equal(Request.Query, view.QueryBox.Text);
        }
        finally { view.DetachTools(); window.Close(); }
    });

    [Fact]
    public Task StaleFavoritePreflightCannotStartAStoreWrite() => dispatcher.RunAsync(async () =>
    {
        var store = new HeldStore(); var view = new ExpressionAssistView(); var window = Host(view);
        var preflight = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var tools = new ExpressionAssistCandidateTools(Dispatcher.CurrentDispatcher, store, Factory(), null,
            () => window.IsVisible, _ => preflight.Task);
        try
        {
            window.Show(); view.AttachTools(tools); tools.Update(Request, Result); view.Update(Result, null, false);
            await Until(() => Descendants<VocabularyFavoriteButton>(view).FirstOrDefault()?.IsEnabled == true);
            var favorite = Descendants<VocabularyFavoriteButton>(view).First(); Click(favorite);
            tools.Update(Request, new([new("other", "changed", null)], null)); preflight.SetResult(true); await favorite.LastOperation;
            Assert.Equal(0, store.Saves);
        }
        finally { preflight.TrySetResult(false); view.DetachTools(); window.Close(); }
    });

    [Fact]
    public Task ClosingJoinsAnAcceptedSaveWithoutDisposingTheBorrowedStore() => dispatcher.RunAsync(async () =>
    {
        var store = new HeldStore(); var view = new ExpressionAssistView(); var window = Host(view);
        var tools = new ExpressionAssistCandidateTools(Dispatcher.CurrentDispatcher, store, Factory(), null,
            () => window.IsVisible, _ => Task.FromResult(true));
        try
        {
            window.Show(); view.AttachTools(tools); tools.Update(Request, Result); view.Update(Result, null, false);
            await Until(() => Descendants<VocabularyFavoriteButton>(view).FirstOrDefault()?.IsEnabled == true);
            var favorite = Descendants<VocabularyFavoriteButton>(view).First(); Click(favorite); await store.Entered.Task;
            view.DetachTools(); var close = tools.DisposeAsync().AsTask(); Assert.False(close.IsCompleted);
            store.Complete.SetResult(true); await close;
            Assert.Equal(1, store.Saves); Assert.Equal(1, store.Accepted); Assert.True(store.IsLoaded);
        }
        finally { store.Complete.TrySetResult(false); view.DetachTools(); window.Close(); await tools.DisposeAsync(); }
    });

    [Fact]
    public Task RetiredPreparationCannotAttachOldCapturesToANewerResult() => dispatcher.RunAsync(async () =>
    {
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var facts = new Facts { BeforeWords = text => { if (text == "first") { entered.TrySetResult(); release.Wait(); } } };
        var tools = new ExpressionAssistCandidateTools(Dispatcher.CurrentDispatcher, new HeldStore(), Factory(facts), null, () => true, _ => Task.FromResult(true));
        try
        {
            tools.Update(Request, new([new("first", "old", null)], null)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var next = new ExpressionAssistCandidate("second", "new", null); tools.Update(Request, new([next], null));
            await Until(() => tools.Capture(0, next) is not null); var id = tools.Capture(0, next)!.Example!.Id;
            release.Set(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("second", tools.Capture(0, next)!.Expression); Assert.Equal(id, tools.Capture(0, next)!.Example!.Id);
        }
        finally { release.Set(); await tools.DisposeAsync(); }
    });

    private static Window Host(FrameworkElement view)
    {
        var window = new Window { Content = view, Width = 430, Height = 560, ShowActivated = false, ShowInTaskbar = false };
        view.HorizontalAlignment = HorizontalAlignment.Left; view.VerticalAlignment = VerticalAlignment.Top;
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        window.FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); window.FontSize = 13;
        WindowTheme.ApplyPalette(window.Resources, false); return window;
    }
    private static void Render(FrameworkElement view, Window window)
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_EXPRESSION_TOOLS_RENDER") is not { Length: > 0 } directory) return;
        foreach (var (name, dark, height, scale) in new[] { ("light", false, 560, 1d), ("dark", true, 560, 1d), ("short", false, 340, 1d), ("large-text", false, 560, 1.5d) })
        {
            WindowTheme.ApplyPalette(window.Resources, dark); window.Height = height; window.Width = scale > 1 ? 650 : 430;
            view.LayoutTransform = new ScaleTransform(scale, scale);
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * scale), (int)Math.Ceiling(view.ActualHeight * scale), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = System.IO.File.Create(System.IO.Path.Combine(directory, "expression-tools-" + name + ".png")); encoder.Save(output);
        }
        view.LayoutTransform = Transform.Identity; window.Height = 560; window.Width = 430; window.UpdateLayout();
    }
    private static async Task Until(Func<bool> condition)
    { var elapsed = Stopwatch.StartNew(); while (!condition()) { Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(6)); await Task.Delay(10); } }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class Facts : ILearningLexicalText, ILearningEvidenceSentenceTokenizer
    {
        internal Action<string>? BeforeWords;
        public IReadOnlyList<LearningTextRange> Tokenize(string text, LearningLanguage language) => [new(0, text.Length)];
        public IReadOnlyList<LearningDictionaryWord> Words(string text, LearningLanguage language)
        { BeforeWords?.Invoke(text); return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => new LearningDictionaryWord(word, new(text.IndexOf(word, StringComparison.Ordinal), word.Length))).ToArray(); }
        public string Key(string text) => text.ToLowerInvariant(); public bool IsPatternText(string text) => false; public bool FirstIsUppercase(string text) => false;
    }
    private sealed class SpeechSpy : ISpeechProviding
    {
        public Action<SpeechProviderEvent>? EventHandler { get; set; }
        internal List<string> Spoken = []; internal int Stops;
        public IReadOnlyList<SpeechVoice> RefreshVoices() => [new("test-en", "Owned test voice", "en-US", SpeechVoiceQuality.Standard)];
        public void Speak(string text, SpeechVoice voice, double rate, SpeechPlaybackToken token)
        { Spoken.Add(text); EventHandler?.Invoke(new SpeechProviderEvent.Started(token)); }
        public void Stop(SpeechPlaybackToken token) { Stops++; EventHandler?.Invoke(new SpeechProviderEvent.Cancelled(token)); }
    }
    private sealed class HeldStore : IWritingVocabularyStore
    {
        public event Action? Changed { add { } remove { } }
        public Action<VocabularySelection?>? OpenSelection => null; public string? ErrorMessage => null; public bool IsLoaded => true;
        internal int Saves, Accepted;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task LoadAsync() => Task.CompletedTask; public bool Contains(VocabularyCapture capture) => Accepted > 0;
        public bool IsPending(VocabularyCapture capture) => Saves > Accepted;
        public async Task<bool> SaveAsync(VocabularyCapture capture)
        { Saves++; Entered.TrySetResult(); bool result = await Complete.Task; if (result) Accepted++; return result; }
        public Task<bool> RemoveAsync(VocabularyCapture capture) => throw new NotSupportedException();
        public void Show(VocabularyCapture capture) => throw new NotSupportedException();
    }
}
