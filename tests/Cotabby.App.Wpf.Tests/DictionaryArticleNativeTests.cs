using System.Windows;
using Cotabby.App.UI.Learning;
using Cotabby.Core.LanguageLearning;
using Microsoft.Web.WebView2.Wpf;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class DictionaryArticleNativeTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HeldInitializationRetiresBeforeLateCompletionAndCleansPrivateProfile(bool controller) => dispatcher.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Hold() { entered.TrySetResult(); return release.Task; }
        var view = new DictionaryArticleView(controller ? null : Hold, controller ? Hold : null);
        var window = new Window { Width = 360, Height = 240, Content = view, ShowInTaskbar = false };
        int loads = 0;
        try
        {
            window.Show();
            var show = view.ShowAsync(new("<img src=\"late.png\">", "", "Fixture", Guid.NewGuid()),
                (_, _) => { loads++; return Task.FromResult<DictionaryResource?>(null); }, () => true, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            window.Close(); await view.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await show.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(System.IO.Directory.Exists(view.ProfilePath)); Assert.False(view.ProfileCleanup.IsCompleted);
            release.SetResult(); await view.ProfileCleanup.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(System.IO.Directory.Exists(view.ProfilePath)); Assert.Equal(0, loads);
        }
        finally { release.TrySetResult(); if (window.IsVisible) window.Close(); await view.DisposeAsync(); }
    });

    [Fact]
    public Task RetirementDrainsAnAlreadyAcceptedResource() => dispatcher.RunAsync(async () =>
    {
        var view = new DictionaryArticleView();
        var window = new Window { Width = 360, Height = 240, Content = view, ShowInTaskbar = false };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DictionaryResource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            window.Show();
            await view.ShowAsync(new("<img src=\"held.png\">", "", "Fixture", Guid.NewGuid()),
                (_, _) => { entered.TrySetResult(); return release.Task; }, () => true, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            view.Retire(); var dispose = view.DisposeAsync().AsTask(); Assert.False(dispose.IsCompleted);
            release.SetResult(new([1, 2, 3], "image/png"));
            await dispose.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { window.Close(); await view.DisposeAsync(); }
    });

    [Fact]
    public Task ClosingDuringInitializationRetiresAndDrains() => dispatcher.RunAsync(async () =>
    {
        var view = new DictionaryArticleView();
        var window = new Window { Width = 360, Height = 240, Content = view, ShowInTaskbar = false };
        window.Show();
        var article = new LearningDictionaryResult.Article("<p>temporary</p>", "temporary", "Fixture", Guid.NewGuid());
        Task show = view.ShowAsync(article, (_, _) => Task.FromResult<DictionaryResource?>(null), () => true, CancellationToken.None);
        window.Close();
        await view.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        await show.WaitAsync(TimeSpan.FromSeconds(15));
    });

    [Fact]
    public Task SyntheticArticleUsesPrivateRuntimeAndAuthorizedLocalResource() => dispatcher.RunAsync(async () =>
    {
        var view = new DictionaryArticleView();
        var window = new Window { Width = 360, Height = 240, Content = view, ShowInTaskbar = false };
        var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid();
        try
        {
            window.Show();
            var article = new LearningDictionaryResult.Article("<p>Synthetic article</p><img src=\"tiny.png\">", "Synthetic article", "Fixture", id);
            await view.ShowAsync(article, (path, _) =>
            {
                requested.TrySetResult(path);
                byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aXcAAAAASUVORK5CYII=");
                return Task.FromResult<DictionaryResource?>(new(png, "image/png"));
            }, () => true, CancellationToken.None);
            var browser = Assert.Single(view.Children.OfType<WebView2CompositionControl>());
            Assert.True(browser.CoreWebView2 is not null, view.InitializationFailure); Assert.False(browser.CoreWebView2.Settings.IsScriptEnabled);
            Assert.False(browser.CoreWebView2.Settings.IsWebMessageEnabled); Assert.Equal(Visibility.Visible, browser.Visibility);
            Assert.Equal("tiny.png", await requested.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        finally { window.Close(); await view.DisposeAsync(); }
    });
}
