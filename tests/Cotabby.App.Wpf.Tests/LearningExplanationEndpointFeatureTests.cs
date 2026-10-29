using System.Net;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using Cotabby.App.Learning;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningExplanationEndpointFeatureTests(WpfDispatcher dispatcher)
{
    private sealed class Keys : IOpenAICompatibleCredentialStore
    {
        public int Reads;
        public string? ReadApiKey() { Reads++; return "owned"; }
        public void SaveApiKey(string? value) => throw new InvalidOperationException();
        public void DeleteApiKey() => throw new InvalidOperationException();
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"owned-model\"}]}") }); }
    }
    private sealed class MutableKeys : IOpenAICompatibleCredentialStore
    {
        internal int Reads, Saves, Deletes;
        internal string? Value;
        internal bool Fail;
        public string? ReadApiKey() { Reads++; if (Fail) throw new IOException("owned-secret"); return Value; }
        public void SaveApiKey(string? value) { Saves++; Value = value; }
        public void DeleteApiKey() { Deletes++; Value = null; }
    }

    [Fact]
    public Task ExplicitCredentialEditsNormalizeAndOnlyPublishRealChanges() => dispatcher.RunAsync(async () =>
    {
        using var handler = new Handler(); using var http = new HttpClient(handler, false); var keys = new MutableKeys();
        var feature = new LearningExplanationEndpointFeature(Dispatcher.CurrentDispatcher, keys, http);
        Assert.Equal(0, keys.Reads);
        Assert.True(feature.SaveCredential("  owned-key \n")); Assert.Equal("owned-key", keys.Value);
        Assert.False(feature.SaveCredential("owned-key")); Assert.Equal(1, keys.Saves);
        Assert.True(feature.SaveCredential(null)); Assert.Null(keys.Value); Assert.Equal(1, keys.Deletes);
        Assert.False(feature.SaveCredential("  ")); Assert.Equal(1, keys.Deletes);
        int reads = keys.Reads;
        Assert.Throws<Cotabby.Inference.LanguageLearning.LearningTranslationEndpointException>(() => feature.SaveCredential("x\0y"));
        Assert.Equal(reads, keys.Reads); Assert.Equal(0, handler.Calls);
        await feature.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => feature.SaveCredential("other")); Assert.Equal(reads, keys.Reads);
    });

    [Fact]
    public Task CredentialFailuresDoNotExposeProviderDetails() => dispatcher.RunAsync(async () =>
    {
        using var handler = new Handler(); using var http = new HttpClient(handler, false); var keys = new MutableKeys { Fail = true };
        await using var feature = new LearningExplanationEndpointFeature(Dispatcher.CurrentDispatcher, keys, http);
        var error = Assert.Throws<Cotabby.Inference.LanguageLearning.LearningTranslationEndpointException>(() => feature.SaveCredential("new"));
        Assert.DoesNotContain("owned-secret", error.ToString()); Assert.Equal(0, handler.Calls); Assert.Equal(0, keys.Saves);
    });

    [Fact]
    public Task MetadataRetirementWaitsForTheAcceptedUiContinuation() => dispatcher.RunAsync(async () =>
    {
        using var handler = new Handler(); using var http = new HttpClient(handler, false); var keys = new Keys();
        var feature = new LearningExplanationEndpointFeature(Dispatcher.CurrentDispatcher, keys, http);
        Assert.Equal(0, keys.Reads); Assert.Equal(0, handler.Calls);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool applied = false;
        var work = feature.RunAsync(async token =>
        {
            var models = await feature.FetchModelsAsync(new() { Backend = LearningModelBackend.Endpoint,
                BaseUrl = "https://owned.invalid/v1" }, token);
            Assert.Equal("owned-model", Assert.Single(models).Id); entered.TrySetResult();
            await release.Task; applied = !token.IsCancellationRequested;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(1, keys.Reads);
            var retirement = feature.DisposeAsync().AsTask(); Assert.False(retirement.IsCompleted);
            release.TrySetResult(); await retirement.WaitAsync(TimeSpan.FromSeconds(5)); await work;
            Assert.False(applied);
            Assert.Throws<ObjectDisposedException>(() => { _ = feature.RunAsync(_ => Task.CompletedTask); });
            using var response = await http.GetAsync("https://owned.invalid/models"); Assert.True(response.IsSuccessStatusCode);
        }
        finally { release.TrySetResult(); await feature.DisposeAsync(); }
    });

    [Fact]
    public Task LocalMetadataAdmissionNeverReadsEndpointCredentials() => dispatcher.RunAsync(async () =>
    {
        using var handler = new Handler(); using var http = new HttpClient(handler, false); var keys = new Keys();
        await using var feature = new LearningExplanationEndpointFeature(Dispatcher.CurrentDispatcher, keys, http);
        await Assert.ThrowsAsync<Cotabby.Inference.LanguageLearning.LearningTranslationEndpointException>(() => feature.FetchModelsAsync(new(), default));
        Assert.Equal(0, keys.Reads); Assert.Equal(0, handler.Calls);
    });
}
