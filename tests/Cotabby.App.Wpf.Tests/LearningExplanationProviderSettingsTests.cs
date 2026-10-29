using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.Settings;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningExplanationProviderSettingsTests(WpfDispatcher dispatcher)
{
    [Fact]
    public void LocalBudgetCommitRequiresCurrentRoleAndActiveModel()
    {
        var settings = new AppSettings { LearningTranslationPowerSwitching = true, LearningPowerPluggedIn = true,
            LearningTranslationPluggedInProfile = LearningPowerProfile.Local("active.gguf"), LearningLocalModel = "manual.gguf" };
        Assert.True(LearningLocalModels.CanCommitLocal(settings, LearningModelRole.Translation, "ACTIVE.gguf"));
        Assert.False(LearningLocalModels.CanCommitLocal(settings, LearningModelRole.Translation, "manual.gguf"));
        settings.LearningTranslationPluggedInProfile = LearningPowerProfile.Endpoint("remote");
        Assert.False(LearningLocalModels.CanCommitLocal(settings, LearningModelRole.Translation, "active.gguf"));
        Assert.False(LearningLocalModels.CanCommitLocal(settings, LearningModelRole.Explanation, "active.gguf"));
    }

    [Fact]
    public Task PowerSelectionShowsActiveEndpointAndEditsItsOwnBudget() => dispatcher.Run(() =>
    {
        var settings = new AppSettings { LearningExplanationBackend = LearningModelBackend.Local, LearningExplanationLocalModel = "manual.gguf",
            LearningExplanationPowerSwitching = true, LearningPowerPluggedIn = true,
            LearningExplanationPluggedInProfile = LearningPowerProfile.Endpoint("active-model") };
        var view = new LearningExplanationProviderSettings(() => settings, edit => edit(settings), operation => operation(CancellationToken.None),
            (_, _) => Task.FromResult<IReadOnlyList<OpenAICompatibleModelOption>>([]), _ => false, () => { }, new TextBlock());
        var window = new Window { Width = 450, Height = 480, Content = new ScrollViewer { Content = view }, ShowInTaskbar = false };
        try
        {
            window.Show(); window.Activate(); Assert.False(view.Backend.IsEnabled); Assert.False(view.Model.IsEnabled);
            Assert.Equal("active-model", view.Model.Text); Assert.Contains("active-model", view.Destination.Text);
            Assert.True(view.Url.IsEnabled); Assert.True(view.Key.IsEnabled);
            Keyboard.Focus(view.ContextTokens); view.ContextTokens.Text = "4096"; Keyboard.Focus(view.Url);
            Assert.Equal(4096, settings.LearningExplanationEndpointContextTokensByDestination[settings.LearningExplanationEndpointIdentity]);
            Keyboard.Focus(view.ContextTokens); view.ContextTokens.Text = "7000";
            settings.LearningExplanationPluggedInProfile = LearningPowerProfile.Endpoint("different-model"); view.Refresh(); Keyboard.Focus(view.Url);
            Assert.False(settings.LearningExplanationEndpointContextTokensByDestination.ContainsKey(settings.LearningExplanationEndpointIdentity));
            Assert.Equal("manual.gguf", settings.LearningExplanationLocalModel); Assert.Equal(LearningModelBackend.Local, settings.LearningExplanationBackend);
        }
        finally { view.Close(); window.Close(); }
    });

    [Fact]
    public Task FocusedParameterEditCannotWriteToAnotherDestination() => dispatcher.Run(() =>
    {
        var settings = new AppSettings { LearningExplanationBackend = LearningModelBackend.Endpoint, LearningExplanationEndpointModel = "first" };
        var view = new LearningExplanationProviderSettings(() => settings, edit => edit(settings), operation => operation(CancellationToken.None),
            (_, _) => Task.FromResult<IReadOnlyList<OpenAICompatibleModelOption>>([]), _ => false, () => { }, new TextBlock());
        var window = new Window { Width = 450, Height = 480, Content = new ScrollViewer { Content = view, Background = Brushes.White }, ShowInTaskbar = false };
        try
        {
            window.Show(); window.Activate(); Keyboard.Focus(view.ContextTokens); view.ContextTokens.Text = "3000";
            settings.LearningExplanationEndpointModel = "second"; view.Refresh(); Keyboard.Focus(view.Model);
            Assert.Empty(settings.LearningExplanationEndpointContextTokensByDestination);
            LearningViewProbe.AssertPaints(window);
            if (Environment.GetEnvironmentVariable("LINGROVE_EXPLANATION_RENDER") is { Length: > 0 } output)
            {
                var root = (FrameworkElement)window.Content; var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new System.IO.FileStream(output, System.IO.FileMode.CreateNew); encoder.Save(stream);
            }
        }
        finally { view.Close(); window.Close(); }
    });

    [Fact]
    public Task LocalAndExplicitCredentialsDoNotReadOrSendImplicitly() => dispatcher.Run(() =>
    {
        var settings = new AppSettings(); int saves = 0, calls = 0, revisions = 0;
        var view = new LearningExplanationProviderSettings(() => settings, edit => edit(settings), operation => operation(CancellationToken.None),
            (_, _) => { calls++; return Task.FromResult<IReadOnlyList<OpenAICompatibleModelOption>>([]); },
            _ => { saves++; return true; }, () => revisions++, new TextBlock());
        try
        {
            view.Refresh(); view.BeginRefresh(); view.StoreKey("private"); Assert.Equal(0, saves); Assert.Equal(0, calls);
            view.Backend.SelectedIndex = 1; Assert.Equal(LearningModelBackend.Endpoint, settings.LearningExplanationBackend);
            view.StoreKey("private"); Assert.Equal(1, saves); Assert.Equal(1, revisions); Assert.Equal("", view.Key.Password);
            view.StoreKey(null); Assert.Equal(2, saves); Assert.Equal(2, revisions);
            Assert.Contains("this PC", view.Destination.Text);
        }
        finally { view.Close(); }
    });

    [Fact]
    public Task StaleMetadataCannotPublishAfterDestinationChangeOrClose() => dispatcher.RunAsync(async () =>
    {
        var settings = new AppSettings { LearningExplanationBackend = LearningModelBackend.Endpoint, LearningExplanationEndpointModel = "first" };
        var pending = new TaskCompletionSource<IReadOnlyList<OpenAICompatibleModelOption>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? owned = null; int calls = 0;
        var view = new LearningExplanationProviderSettings(() => settings, edit => edit(settings), operation => owned = operation(CancellationToken.None),
            (_, _) => { calls++; return pending.Task; }, _ => false, () => { }, new TextBlock());
        view.BeginRefresh(); Assert.Equal(1, calls); Assert.NotNull(owned);
        settings.LearningExplanationEndpointModel = "new"; view.Refresh();
        pending.SetResult([new("obsolete")]); await owned!;
        Assert.Null(view.Models.ItemsSource); Assert.Equal("new", settings.LearningExplanationEndpointModel);
        Assert.DoesNotContain("obsolete", view.Status.Text); view.Close();
    });

    [Fact]
    public Task DiscoveredModelSelectionChangesOnlyExplanationDestination() => dispatcher.RunAsync(async () =>
    {
        var settings = new AppSettings { LearningExplanationBackend = LearningModelBackend.Endpoint,
            LearningExplanationEndpointBaseUrl = "https://service.example/v1", LearningExplanationEndpointModel = "first" };
        Task? owned = null;
        var view = new LearningExplanationProviderSettings(() => settings, edit => edit(settings), operation => owned = operation(CancellationToken.None),
            (_, _) => Task.FromResult<IReadOnlyList<OpenAICompatibleModelOption>>([new("second")]), _ => false, () => { }, new TextBlock());
        try
        {
            Assert.Contains("remote service", view.Destination.Text); view.BeginRefresh(); await owned!;
            view.Models.SelectedItem = "second";
            Assert.Equal("second", settings.LearningExplanationEndpointModel);
            Assert.Contains("second", view.Destination.Text); Assert.Equal("Not connected.", view.Status.Text);
        }
        finally { view.Close(); }
    });

    [Fact]
    public Task ClosingRetiresHeldMetadataAndDrainsTheOwnedContinuation() => dispatcher.RunAsync(async () =>
    {
        var settings = new AppSettings { LearningExplanationBackend = LearningModelBackend.Endpoint };
        var pending = new TaskCompletionSource<IReadOnlyList<OpenAICompatibleModelOption>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? owned = null;
        var view = new LearningExplanationProviderSettings(() => settings, edit => edit(settings), operation => owned = operation(CancellationToken.None),
            (_, _) => pending.Task, _ => false, () => { }, new TextBlock());
        view.BeginRefresh(); view.Close(); Assert.NotNull(owned); Assert.False(owned!.IsCompleted);
        pending.SetResult([new("late")]); await owned;
        Assert.Null(view.Models.ItemsSource); Assert.DoesNotContain("late", view.Status.Text);
    });

    [Fact]
    public Task EndpointErrorsAndInvalidAddressNeverExposeBodyOrCredentials() => dispatcher.RunAsync(async () =>
    {
        var settings = new AppSettings { LearningExplanationBackend = LearningModelBackend.Endpoint, LearningExplanationEndpointBaseUrl = "https://secret:token@example.invalid" };
        Task? owned = null;
        var view = new LearningExplanationProviderSettings(() => settings, edit => edit(settings), operation => owned = operation(CancellationToken.None),
            (_, _) => Task.FromException<IReadOnlyList<OpenAICompatibleModelOption>>(new InvalidOperationException("secret request body")),
            _ => throw new InvalidOperationException("secret key"), () => { }, new TextBlock());
        try
        {
            Assert.DoesNotContain("secret", view.Destination.Text); view.StoreKey("secret"); Assert.DoesNotContain("secret", view.Status.Text);
            view.BeginRefresh(); await owned!; Assert.DoesNotContain("secret", view.Status.Text);
        }
        finally { view.Close(); }
    });
}
