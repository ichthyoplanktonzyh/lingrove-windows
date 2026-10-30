using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI.Learning;
using Cotabby.App.Dictionaries;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.LanguageLearning;
using Cotabby.Win32.Dictionary;
using System.Net;
using System.Net.Http;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class DictionaryOnlinePresentationTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task RealFloatingCardAndRouterRequireCurrentVisibleOfferBeforeFakeHttp() => dispatcher.RunAsync(async () =>
    {
        var appSettings = new AppSettings(); var settings = new DictionarySettings(() => appSettings, () => { });
        Assert.True(settings.SetEnabled(DictionarySource.Online, true));
        var handler = new Handler(); await using var online = new OnlineDictionaryClient(handler);
        await using var books = new MDictLibraryService(s => s.ToLowerInvariant());
        var router = new DictionaryRouter(settings, new Local(), books, (_, _) => [], online);
        await using var session = new LearningDictionarySession(router);
        var unit = new LearningUnit(0, "cat", "cat", LearningUnitKind.Word, [new(0, 3)], [new("cat", new(0, 3))], "fixture");
        await session.OpenAsync(unit, "synthetic", LearningLanguage.English);
        await session.SelectSourceAsync(DictionarySource.Online);
        var offer = session.State; Assert.Equal(LearningDictionaryContentStatus.AwaitingOnlineConfirmation, offer.Status);
        var window = new LearningDictionaryWindow();
        bool permitted = true;
        window.AttachSession(session, () => permitted);
        try
        {
            window.ShowState(offer, LearningViewProbe.Anchor);
            var body = LearningViewProbe.Find<StackPanel>(window, "DefinitionContent");
            Assert.Equal("Online English dictionary", LearningViewProbe.Find<TextBlock>(window, "SubtitleText").Text);
            Assert.Contains(body.Children.OfType<TextBlock>(), x => x.Text.Contains("https://api.dictionaryapi.dev"));
            Assert.Equal(0, handler.Calls);
            var button = Assert.Single(body.Children.OfType<Button>());
            await session.SelectSourceAsync(DictionarySource.Bundled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, handler.Calls);
            await session.SelectSourceAsync(DictionarySource.Online);
            window.ShowState(session.State, LearningViewProbe.Anchor);
            button = Assert.Single(body.Children.OfType<Button>());
            permitted = false;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, handler.Calls);
            permitted = true;
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(LearningDictionaryState value)
            { if (value.SelectedSource == DictionarySource.Online && value.Status == LearningDictionaryContentStatus.Result) finished.TrySetResult(); }
            session.StateChanged += Changed;
            try
            {
                Render(window, "online-floating-offer.png");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, handler.Calls);
            }
            finally { session.StateChanged -= Changed; }
            await session.SelectSourceAsync(DictionarySource.Online);
            window.ShowState(session.State, LearningViewProbe.Anchor);
            button = Assert.Single(body.Children.OfType<Button>());
            window.HideCard();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, handler.Calls);
        }
        finally { window.Close(); await window.ArticleShutdown; }
    });

    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
    }
    private sealed class Local : ILearningDictionaryProviding
    {
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken = default)
            => Task.FromResult(LearningDictionaryResult.NotFound);
    }
    private static void Render(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew); encoder.Save(stream);
    }
    [Fact]
    public Task ExplicitOfferShowsDestinationBeforeActionAndRejectsStaleClick() => dispatcher.Run(() =>
    {
        var body = new StackPanel { Margin = new Thickness(16), Background = Brushes.White }; var attribution = new TextBlock();
        var state = new LearningDictionaryState { Status = LearningDictionaryContentStatus.AwaitingOnlineConfirmation, SelectedSource = DictionarySource.Online };
        bool current = true; int calls = 0;
        var window = new Window { Width = 440, Height = 260, Content = body, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
            Cotabby.App.UI.WindowTheme.ApplyPalette(window.Resources, false);
            LearningDictionaryDefinitions.Populate(body, attribution, state, () => calls++, () => current);
            window.Show(); window.UpdateLayout();
            Assert.Contains(body.Children.OfType<TextBlock>(), t => t.Text.Contains("https://api.dictionaryapi.dev"));
            Assert.Contains(body.Children.OfType<TextBlock>(), t => t.Text.Contains("Only this lookup term"));
            var button = Assert.Single(body.Children.OfType<Button>()); Assert.Equal(0, calls);
            if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir); var bitmap = new RenderTargetBitmap(440, 260, 96, 96, PixelFormats.Pbgra32); bitmap.Render(body);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(dir, "online-offer.png")); encoder.Save(stream);
            }
            current = false; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(0, calls);
            current = true; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, calls);
            var website = state with { Status = LearningDictionaryContentStatus.Result, Result = new LearningDictionaryResult.Website(new Uri("https://example.invalid/search?q=cat")) };
            LearningDictionaryDefinitions.Populate(body, attribution, website, null, () => current);
            Assert.Contains(body.Children.OfType<TextBlock>(), t => t.Text.Contains("https://example.invalid/search?q=cat"));
            Assert.Contains(body.Children.OfType<TextBlock>(), t => t.Inlines.OfType<Hyperlink>().Any());
        }
        finally { window.Close(); }
    });
}
