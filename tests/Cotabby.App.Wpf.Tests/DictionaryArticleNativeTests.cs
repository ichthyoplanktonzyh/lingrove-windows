using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.Learning;
using Cotabby.Core.LanguageLearning;
using Microsoft.Web.WebView2.Wpf;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class DictionaryArticleNativeTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task HungInitializationsLimitNewProfilesUntilLateCleanupAndRetry() => dispatcher.RunAsync(async () =>
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var views = new[] { new DictionaryArticleView(() => first.Task), new DictionaryArticleView(() => second.Task), new DictionaryArticleView(() => third.Task) };
        var article = new LearningDictionaryResult.Article("<p>private synthetic</p>", "private synthetic", "Fixture", Guid.NewGuid());
        Task Show(DictionaryArticleView view) => view.ShowAsync(article, (_, _) => Task.FromResult<DictionaryResource?>(null), () => true, CancellationToken.None);
        try
        {
            Task one = Show(views[0]), two = Show(views[1]);
            await Show(views[2]);
            var retry = Assert.Single(views[2].Children.OfType<Button>());
            Assert.Equal(Visibility.Visible, retry.Visibility); Assert.False(System.IO.Directory.Exists(views[2].ProfilePath));
            Assert.DoesNotContain("private synthetic", Assert.Single(views[2].Children.OfType<TextBlock>()).Text);
            await views[0].DisposeAsync(); await one; Assert.False(views[0].ProfileCleanup.IsCompleted);
            Assert.Null(DictionaryArticleAdmission.TryAcquire());
            first.SetResult(); await views[0].ProfileCleanup.WaitAsync(TimeSpan.FromSeconds(15));
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(System.IO.Directory.Exists(views[2].ProfilePath));
            await views[1].DisposeAsync(); await two; await views[2].DisposeAsync();
            second.SetResult(); third.SetResult();
            await Task.WhenAll(views[1].ProfileCleanup, views[2].ProfileCleanup).WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            first.TrySetResult(); second.TrySetResult(); third.TrySetResult();
            foreach (var view in views) { await view.DisposeAsync(); await view.ProfileCleanup.WaitAsync(TimeSpan.FromSeconds(15)); }
        }
    });

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
        finally { release.TrySetResult(); if (window.IsVisible) window.Close(); await view.DisposeAsync(); await view.ProfileCleanup.WaitAsync(TimeSpan.FromSeconds(15)); }
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
        finally { window.Close(); await view.DisposeAsync(); await view.ProfileCleanup.WaitAsync(TimeSpan.FromSeconds(15)); }
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
        finally { window.Close(); await view.DisposeAsync(); await view.ProfileCleanup.WaitAsync(TimeSpan.FromSeconds(15)); }
    });
}
