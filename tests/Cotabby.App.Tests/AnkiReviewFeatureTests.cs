using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Review;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Actual feature and interaction run with synthetic credentials/transport and the
/// existing lazy archive. No WPF view, operating-system credential, endpoint or model is used.</summary>
public sealed class AnkiReviewFeatureTests
{
    [Fact]
    public async Task ConnectionOnlyOwnerUsesRealSettingsAndClientButRefusesNotebookReview()
    {
        var current = new AppSettings(); int saves = 0; var key = new AnkiTestCredentials(); var client = new AnkiTestClient();
        var feature = AnkiReviewFeature.CreateConnectionSettings(() => current, () => saves++, client, key);
        Assert.False(feature.Controller.NotebookAvailable); Assert.Null(feature.Controller.StoreOrNull);
        Assert.Equal(0, key.Reads); Assert.Empty(client.Calls);
        var interaction = new AnkiReviewSettingsInteraction(feature); interaction.ReadKeyOnAppearance();
        interaction.Key = "explicit key"; await interaction.TestConnectionAsync();
        Assert.Equal("Connected", feature.Controller.ConnectionMessage); Assert.NotNull(feature.Controller.Catalog);
        Assert.All(client.Calls, call => Assert.Equal("explicit key", call.Key));
        feature.Settings.Deck = "My deck"; Assert.Equal("My deck", current.AnkiReviewDeckValue?.GetString()); Assert.True(saves > 0);
        client.Calls.Clear();
        Assert.Equal(AnkiReviewController.NotebookUnavailableMessage, (await Assert.ThrowsAsync<AnkiReviewException>(() => feature.Controller.OperationScopeAsync())).Message);
        Assert.Equal(AnkiReviewController.NotebookUnavailableMessage, await feature.Controller.ReviewDeckAsync());
        Assert.Equal(AnkiReviewController.NotebookUnavailableMessage, (await Assert.ThrowsAsync<AnkiReviewException>(() => feature.Controller.UpdatePresetTemplateAsync())).Message);
        Assert.Equal(AnkiReviewController.NotebookUnavailableMessage, Assert.Throws<AnkiReviewException>(() => feature.Controller.Begin(Guid.NewGuid())).Message);
        Assert.Equal(AnkiReviewController.NotebookUnavailableMessage, Assert.Throws<AnkiReviewException>(() => _ = feature.Controller.Store).Message);
        Assert.Empty(client.Calls); await feature.DisposeAsync(); Assert.Equal(1, client.Disposals);
    }

    [Fact]
    public async Task ConnectionOnlyShutdownCancelsAndDrainsAnExplicitTest()
    {
        var settings = new AppSettings(); var client = new AnkiTestClient { ProfileGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var feature = AnkiReviewFeature.CreateConnectionSettings(() => settings, () => { }, client, new AnkiTestCredentials());
        var interaction = new AnkiReviewSettingsInteraction(feature); interaction.ReadKeyOnAppearance();
        var action = interaction.TestConnectionAsync(); Assert.Single(client.Calls);
        await feature.DisposeAsync(); await action;
        Assert.False(feature.Controller.Testing); Assert.Equal(1, client.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => feature.RunAsync(_ => Task.CompletedTask));
    }
    private static VocabularyStore Store(Action? opened = null) => new(new VocabularyArchive(() =>
    { opened?.Invoke(); return VocabularySQLiteDatabase.CreateMemory(); }), new SynchronizationContext());

    [Fact]
    public async Task ConstructionAndSynchronousWorkAreInertAndFailureDoesNotPoisonDrain()
    {
        int opened = 0, saved = 0; var key = new AnkiTestCredentials(); var client = new AnkiTestClient();
        await using var store = Store(() => opened++);
        var feature = new AnkiReviewFeature(() => new AppSettings(), () => saved++, store, client, key);
        var interaction = new AnkiReviewSettingsInteraction(feature);
        Assert.Same(store, feature.Controller.Store); Assert.Same(feature.Settings, feature.Controller.Settings);
        Assert.Equal(0, opened); Assert.Equal(0, key.Reads); Assert.Equal(0, saved); Assert.Empty(client.Calls);
        await feature.RunAsync(_ => Task.CompletedTask);
        await Assert.ThrowsAsync<AnkiReviewException>(() => feature.RunAsync(_ => throw new AnkiReviewException("action failure")));
        await feature.DisposeAsync(); await feature.DisposeAsync(); Assert.Equal(1, client.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => feature.RunAsync(_ => Task.CompletedTask));
        Assert.Equal(0, opened); Assert.Equal(0, key.Reads);
    }

    [Fact]
    public async Task ShutdownClosesAdmissionAndCancelsThenDrainsBeforeClientDisposal()
    {
        await using var store = Store(); var client = new AnkiTestClient();
        var feature = new AnkiReviewFeature(() => new AppSettings(), () => { }, store, client, new AnkiTestCredentials());
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task action = feature.RunAsync(async ct => { started.SetResult(ct); await release.Task; });
        var token = await started.Task; Task first = feature.DisposeAsync().AsTask();
        try
        {
            Assert.True(token.IsCancellationRequested); Assert.False(first.IsCompleted); Assert.Equal(0, client.Disposals);
            Assert.Same(first, feature.DisposeAsync().AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => feature.RunAsync(_ => Task.CompletedTask));
        }
        finally { release.TrySetResult(); }
        await action; await first; Assert.Equal(1, client.Disposals);
    }

    [Fact]
    public async Task AThrowingCancellationCallbackStillDrainsAndDisposesTheClientOnce()
    {
        await using var store = Store(); var client = new AnkiTestClient();
        var feature = new AnkiReviewFeature(() => new AppSettings(), () => { }, store, client, new AnkiTestCredentials());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task action = feature.RunAsync(async ct =>
        {
            using var registration = ct.Register(() => { callbackStarted.TrySetResult(); throw new AnkiReviewException("callback failure"); });
            await release.Task;
        });
        Task close = feature.DisposeAsync().AsTask();
        try
        {
            // CancelAsync schedules callbacks. Releasing the action before this signal can
            // unregister a callback that never began, making a required throw depend on timing.
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(close.IsCompleted); Assert.Equal(0, client.Disposals);
        }
        finally { release.TrySetResult(); }
        await action;
        await Assert.ThrowsAsync<AggregateException>(() => close); Assert.Equal(1, client.Disposals);
        await Assert.ThrowsAsync<AggregateException>(() => feature.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task ReentrantDisposalInsideAnAdmittedCallbackSeesItsReservation()
    {
        await using var store = Store(); var client = new AnkiTestClient();
        var feature = new AnkiReviewFeature(() => new AppSettings(), () => { }, store, client, new AnkiTestCredentials());
        Task? close = null;
        await feature.RunAsync(_ => { close = feature.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); Assert.Equal(0, client.Disposals); return Task.CompletedTask; });
        await close!; Assert.Equal(1, client.Disposals);
    }

    [Fact]
    public async Task AppearanceAndCanonicalUnchangedGuardDoNotWriteOrInvalidate()
    {
        await using var store = Store(); var client = new AnkiTestClient(); var key = new AnkiTestCredentials { Key = "café" };
        await using var feature = new AnkiReviewFeature(() => new AppSettings(), () => { }, store, client, key);
        var interaction = new AnkiReviewSettingsInteraction(feature); interaction.ReadKeyOnAppearance();
        Assert.Equal("café", interaction.Key); Assert.Equal(1, key.Reads); Assert.Equal(0, key.Writes); Assert.Empty(client.Calls);
        await feature.Controller.TestConnectionAsync(ReviewLayoutPolicy.Model); var catalog = feature.Controller.Catalog; Assert.NotNull(catalog);
        interaction.Key = "cafe\u0301"; interaction.SaveKey(); Assert.Equal(0, key.Writes); Assert.Equal("café", key.Key); Assert.Same(catalog, feature.Controller.Catalog);
        interaction.Key = " \0\ufeff "; interaction.SaveKey(); Assert.Equal(" \0\ufeff ", key.Key); Assert.Equal(1, key.Writes); Assert.Null(feature.Controller.Catalog);
        interaction.SaveKey(); Assert.Equal(1, key.Writes);
        interaction.ClearEphemeralKey(); Assert.Equal("", interaction.Key); Assert.Equal(" \0\ufeff ", key.Key); Assert.Equal(1, key.Writes);
    }

    [Fact]
    public async Task FailedSaveBlocksTestUntilSuccessfulChangedKeyAndUsesFixedFlexibleModel()
    {
        await using var store = Store(); var client = new AnkiTestClient(); var key = new AnkiTestCredentials(); var current = new AppSettings();
        await using var feature = new AnkiReviewFeature(() => current, () => { }, store, client, key); feature.Settings.Model = "Legacy custom";
        var interaction = new AnkiReviewSettingsInteraction(feature); interaction.ReadKeyOnAppearance(); interaction.Key = "edited"; key.FailWrite = true;
        await interaction.TestConnectionAsync(); Assert.Equal("fixture write failure", interaction.KeyError); Assert.Empty(client.Calls); Assert.Equal("fixture-key", key.Key);
        // Returning to the saved spelling leaves the original error sticky, exactly like source.
        interaction.Key = "fixture-key"; key.FailWrite = false; await interaction.TestConnectionAsync(); Assert.Empty(client.Calls); Assert.NotNull(interaction.KeyError);
        interaction.Key = "new key"; await interaction.TestConnectionAsync(); Assert.Null(interaction.KeyError); Assert.Equal("Connected", feature.Controller.ConnectionMessage);
        Assert.All(client.Calls, call => Assert.Equal("new key", call.Key));
        var fields = Assert.Single(client.Calls, c => c.Action == "modelFieldNames"); Assert.Equal(ReviewLayoutPolicy.Model, fields.Parameters["modelName"].String);
        Assert.Equal("Legacy custom", feature.Settings.Model);
        interaction.Key = ""; interaction.SaveKey(); Assert.Equal("", key.Key); Assert.Null(feature.Controller.Catalog);
    }

    [Fact]
    public async Task SuccessfulAppearanceDoesNotClearPriorReadErrorAndChangedSaveDoes()
    {
        await using var store = Store(); var key = new AnkiTestCredentials { FailRead = true };
        await using var feature = new AnkiReviewFeature(() => new AppSettings(), () => { }, store, new AnkiTestClient(), key);
        var interaction = new AnkiReviewSettingsInteraction(feature); interaction.ReadKeyOnAppearance(); Assert.Equal("fixture read failure", interaction.KeyError);
        key.FailRead = false; interaction.ReadKeyOnAppearance(); Assert.Equal("fixture-key", interaction.Key); Assert.NotNull(interaction.KeyError);
        interaction.SaveKey(); Assert.NotNull(interaction.KeyError); Assert.Equal(0, key.Writes);
        interaction.Key = "replacement"; interaction.SaveKey(); Assert.Null(interaction.KeyError); Assert.Equal(1, key.Writes);
    }
}
