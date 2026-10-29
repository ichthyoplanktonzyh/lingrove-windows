using System.Net;
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
