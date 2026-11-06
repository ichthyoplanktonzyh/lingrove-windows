using System.Net;
using System.Net.Http;
using System.Text;
using Cotabby.App.Dictionaries;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

public sealed class OnlineDictionaryClientTests
{
    [Fact]
    public async Task FixedProviderSendsOnlyEscapedQueryAndRejectsRedirectAndOversize()
    {
        Uri? called = null; var handler = new Handler((request, _) =>
        {
            called = request.RequestUri; Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Content);
            Assert.Equal("application/json", Assert.Single(request.Headers.Accept).MediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        using var client = new OnlineDictionaryClient(handler);
        Assert.Same(LearningDictionaryResult.NotFound, await client.LookupAsync("cat & dog", CancellationToken.None));
        Assert.Equal("https://api.dictionaryapi.dev/api/v2/entries/en/cat%20%26%20dog", called!.AbsoluteUri);
        handler.Next = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LookupAsync("cat", CancellationToken.None));
        handler.Next = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1_000_001]) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LookupAsync("cat", CancellationToken.None));
    }
    [Fact]
    public async Task CancellationAndBoundedResponseAreHonored()
    {
        var handler = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); });
        using var client = new OnlineDictionaryClient(handler); using var stop = new CancellationTokenSource();
        var work = client.LookupAsync("cat", stop.Token); stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        handler.Next = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("[{\"word\":\"cat\",\"meanings\":[]}]", Encoding.UTF8, "application/json") });
        var result = Assert.IsType<LearningDictionaryResult.Entries>(await client.LookupAsync("cat", CancellationToken.None));
        Assert.Equal("cat", Assert.Single(result.Values).Headword);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> next) : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Next = next;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Next(request, cancellationToken);
    }
}
