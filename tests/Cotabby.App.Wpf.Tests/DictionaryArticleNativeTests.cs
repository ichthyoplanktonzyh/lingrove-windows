using System.IO;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.Learning;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Vocabulary;
using Microsoft.Web.WebView2.Wpf;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class DictionaryArticleNativeTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task MissingRuntimeBeforeProfileOrBrowserReturnsAdmission() => dispatcher.RunAsync(async () =>
    {
        var slots = new DictionaryArticleAdmission(); int loads = 0;
        var view = new DictionaryArticleView(slots: slots, runtimeVersion: () => throw new FileNotFoundException());
        try
        {
            await view.ShowAsync(new("<p>fixture</p>", "", "Fixture", Guid.NewGuid()),
                (_, _) => { loads++; return Task.FromResult<DictionaryResource?>(null); }, () => true, CancellationToken.None);
            Assert.False(Directory.Exists(view.ProfilePath)); Assert.Equal(0, loads);
            await view.DisposeAsync(); await view.ProfileCleanup;
            using var first = slots.TryAcquire(); using var second = slots.TryAcquire(); Assert.NotNull(first); Assert.NotNull(second);
        }
        finally { await view.DisposeAsync(); }
    });

    [Fact]
    public Task NativeOwnedProfileWaitsForRetiredControllerInitialization() => dispatcher.RunAsync(async () =>
    {
        string parent = Path.Combine(Path.GetTempPath(), "lingrove-native-profile-test-" + Guid.NewGuid().ToString("N"));
        VocabularyWindowsFile.CreateParentDirectory(parent);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new DictionaryArticleView(controllerBarrier: () => { entered.TrySetResult(); return release.Task; },
            slots: new DictionaryArticleAdmission(), profileParent: parent);
        var window = new Window { Width = 360, Height = 240, Content = view, ShowInTaskbar = false };
        int loads = 0;
        try
        {
            window.Show(); Task show = view.ShowAsync(new("<img src=\"late.png\">", "", "Fixture", Guid.NewGuid()),
                (_, _) => { loads++; return Task.FromResult<DictionaryResource?>(null); }, () => true, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            window.Close(); await view.DisposeAsync(); await show;
            Assert.False(view.ProfileCleanup.IsCompleted); release.SetResult();
            await view.ProfileCleanup.WaitAsync(TimeSpan.FromSeconds(15)); Assert.False(Directory.Exists(view.ProfilePath)); Assert.Equal(0, loads);
        }
        finally
        {
            release.TrySetResult(); if (window.IsVisible) window.Close(); await view.DisposeAsync();
            try { Directory.Delete(parent, false); } catch (IOException) { }
        }
    });

    [Fact]
    public Task NativeOwnedProfileSupportsRepeatedWebViewUseAndCleanup() => dispatcher.RunAsync(async () =>
    {
        string parent = Path.Combine(Path.GetTempPath(), "lingrove-native-profile-test-" + Guid.NewGuid().ToString("N"));
        VocabularyWindowsFile.CreateParentDirectory(parent);
        var slots = new DictionaryArticleAdmission();
        try
        {
            for (int index = 0; index < 3; index++)
            {
                var view = new DictionaryArticleView(slots: slots, profileParent: parent);
                var window = new Window { Width = 360, Height = 240, Content = view, ShowInTaskbar = false };
                var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    window.Show();
                    await view.ShowAsync(new("<p>synthetic</p><img src=\"tiny.png\">", "synthetic", "Fixture", Guid.NewGuid()),
                        (_, _) => { requested.TrySetResult(); return Task.FromResult<DictionaryResource?>(new([137,80,78,71], "image/png")); },
                        () => true, CancellationToken.None);
                    var browser = Assert.Single(view.Children.OfType<WebView2CompositionControl>());
                    Assert.True(browser.CoreWebView2 is not null, view.InitializationFailure);
                    await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    string before = Path.Combine(view.ProfilePath, "synthetic-before"), after = Path.Combine(view.ProfilePath, "synthetic-after");
                    File.WriteAllText(before, "test"); File.Move(before, after); Assert.True(File.Exists(after));
                    window.Close(); await view.DisposeAsync(); await view.ProfileCleanup.WaitAsync(TimeSpan.FromSeconds(15));
                    Assert.False(Directory.Exists(view.ProfilePath));
                }
                finally { if (window.IsVisible) window.Close(); await view.DisposeAsync(); }
            }
        }
        finally { try { Directory.Delete(parent, false); } catch (IOException) { } }
    });

    // The test alone owns these synthetic folders. Production has no pathname-based cleanup.
    private static DictionaryProfileEffects TestEffects => new(path => Directory.Delete(path, true), File.GetAttributes, () => Task.Delay(250));

    [Fact]
    public Task ReplacedProfileDirectoryIsNeverDeleted() => dispatcher.RunAsync(async () =>
    {
        var slots = new DictionaryArticleAdmission();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new DictionaryArticleView(environmentBarrier: () => release.Task, slots: slots, profileEffects: DictionaryProfileEffects.FailClosed);
        var article = new LearningDictionaryResult.Article("fixture", "", "Fixture", Guid.NewGuid());
        string original = view.ProfilePath + "-original";
        try
        {
            Task show = view.ShowAsync(article, (_, _) => Task.FromResult<DictionaryResource?>(null), () => true, CancellationToken.None);
            Directory.Move(view.ProfilePath, original); Directory.CreateDirectory(view.ProfilePath);
            string marker = Path.Combine(view.ProfilePath, "replacement.txt"); File.WriteAllText(marker, "replacement");
            await view.DisposeAsync(); await show; release.SetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => view.ProfileCleanup);
            Assert.True(File.Exists(marker)); using var one = slots.TryAcquire(); using var two = slots.TryAcquire();
            Assert.NotNull(one); Assert.Null(two);
        }
        finally
        {
            release.TrySetResult(); await view.DisposeAsync();
            // Both absolute paths were generated by this test; neither contains user files.
            if (Directory.Exists(view.ProfilePath)) Directory.Delete(view.ProfilePath, true);
            if (Directory.Exists(original)) Directory.Delete(original, true);
        }
    });
    [Fact]
    public Task SanitizerFailureAndRetryDoNotLeakAdmission() => dispatcher.RunAsync(async () =>
    {
        var slots = new DictionaryArticleAdmission(); int attempts = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new DictionaryArticleView(environmentBarrier: () => release.Task,
            sanitize: value => { if (++attempts == 1) throw new InvalidDataException(); return value; }, slots: slots, profileEffects: TestEffects);
        var article = new LearningDictionaryResult.Article("fixture", "", "Fixture", Guid.NewGuid());
        try
        {
            await view.ShowAsync(article, (_, _) => Task.FromResult<DictionaryResource?>(null), () => true, CancellationToken.None);
            Task retry = view.ShowAsync(article, (_, _) => Task.FromResult<DictionaryResource?>(null), () => true, CancellationToken.None);
            Assert.Equal(2, attempts); await view.DisposeAsync(); await retry; release.SetResult(); await view.ProfileCleanup;
            using var one = slots.TryAcquire(); using var two = slots.TryAcquire(); Assert.NotNull(one); Assert.NotNull(two);
        }
        finally { release.TrySetResult(); await view.DisposeAsync(); }
    });

    [Theory]
    [InlineData("denied")]
    [InlineData("io")]
    [InlineData("replacement")]
    [InlineData("missing")]
    public Task CleanupRequiresConfirmedAbsence(string mode) => dispatcher.RunAsync(async () =>
    {
        var slots = new DictionaryArticleAdmission();
        var effects = new DictionaryProfileEffects(
            _ => { if (mode == "denied") throw new UnauthorizedAccessException(); if (mode == "io") throw new IOException(); throw new DirectoryNotFoundException(); },
            _ => mode == "replacement" ? FileAttributes.Normal : throw new FileNotFoundException(), () => Task.CompletedTask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new DictionaryArticleView(environmentBarrier: () => release.Task, slots: slots, profileEffects: effects);
        try
        {
            Task show = view.ShowAsync(new("fixture", "", "Fixture", Guid.NewGuid()), (_, _) => Task.FromResult<DictionaryResource?>(null), () => true, CancellationToken.None);
            await view.DisposeAsync(); await show; release.SetResult();
            if (mode == "missing") await view.ProfileCleanup;
            else await Assert.ThrowsAnyAsync<Exception>(() => view.ProfileCleanup);
            using var one = slots.TryAcquire(); using var two = slots.TryAcquire();
            Assert.NotNull(one); if (mode == "missing") Assert.NotNull(two); else Assert.Null(two);
        }
        finally { release.TrySetResult(); await view.DisposeAsync(); if (Directory.Exists(view.ProfilePath)) Directory.Delete(view.ProfilePath, true); }
    });

    [Fact]
    public Task HungInitializationsLimitNewProfilesUntilLateCleanupAndRetry() => dispatcher.RunAsync(async () =>
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var views = new[] { new DictionaryArticleView(() => first.Task, profileEffects: TestEffects), new DictionaryArticleView(() => second.Task, profileEffects: TestEffects), new DictionaryArticleView(() => third.Task, profileEffects: TestEffects) };
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
            Assert.Null(DictionaryArticleAdmission.Shared.TryAcquire());
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
        var view = new DictionaryArticleView(controller ? null : Hold, controller ? Hold : null, profileEffects: TestEffects);
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
        var view = new DictionaryArticleView(profileEffects: TestEffects);
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
        var view = new DictionaryArticleView(profileEffects: TestEffects);
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
