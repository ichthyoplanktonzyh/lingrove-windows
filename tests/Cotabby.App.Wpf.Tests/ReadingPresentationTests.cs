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

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingPresentationTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task FailedBasicCanRetryAgainstTheSameReceipt() => dispatcher.RunAsync(async () =>
    {
        int calls = 0;
        var presentation = new ReadingPresentation(Dispatcher.CurrentDispatcher, new Dictionary(),
            (_, _) => ++calls == 1 ? Task.FromException<ReadingLookupResult>(new InvalidOperationException())
                : Task.FromResult(new ReadingLookupResult("meaning", "")),
            (_, _, _) => throw new InvalidOperationException(), () => { });
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
            (_, _) => pending.Task, (_, _, _) => throw new InvalidOperationException(), () => { });
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
            (input, basic, _) => { Assert.Equal("river bank", basic.Meaning); detailsCalls++; return Task.FromResult("A noun used for a river edge."); }, () => { });
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
    private sealed class Dictionary : ILearningDictionaryProviding, ILearningUnknownDictionaryProviding
    {
        public int UnknownCalls;
        public Task<LearningDictionaryResult> LookupUnknownAsync(string query, bool expression, CancellationToken token = default)
        { Assert.Equal("bank", query); UnknownCalls++; return Task.FromResult(LearningDictionaryResult.NotFound); }
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken token = default)
            => throw new InvalidOperationException();
    }
}
