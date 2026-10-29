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

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingPresentationTests(WpfDispatcher dispatcher)
{
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
        public ManualResetEventSlim Release = new(); public string? Payload; public bool Committed;
        public bool TryWrite(string text, Func<bool> tryCommit)
        { Payload = text; Entered.TrySetResult(); Release.Wait(TimeSpan.FromSeconds(5)); return Committed = tryCommit(); }
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
