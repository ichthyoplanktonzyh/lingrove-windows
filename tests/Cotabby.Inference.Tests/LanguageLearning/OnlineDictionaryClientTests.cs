using System.Net;
using System.Net.Http;
using System.Text;
using Cotabby.Inference.LanguageLearning;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Inference.Tests.LanguageLearning;

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
        await using var client = new OnlineDictionaryClient(handler);
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
        await using var client = new OnlineDictionaryClient(handler); using var stop = new CancellationTokenSource();
        var work = client.LookupAsync("cat", stop.Token); stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        handler.Next = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("[{\"word\":\"cat\",\"meanings\":[]}]", Encoding.UTF8, "application/json") });
        var result = Assert.IsType<LearningDictionaryResult.Entries>(await client.LookupAsync("cat", CancellationToken.None));
        Assert.Equal("cat", Assert.Single(result.Values).Headword);
    }

    [Fact]
    public async Task ShutdownCancelsAndJoinsAcceptedTransportBeforeDisposingPool()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        await using var client = new OnlineDictionaryClient(handler);
        var pending = client.LookupAsync("cat");
        Task close;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            close = client.DisposeAsync().AsTask();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(close.IsCompleted);
            Assert.False(handler.Disposed);
            Assert.Throws<ObjectDisposedException>(() => { _ = client.LookupAsync("dog"); });
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handler.Disposed);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task NoncooperativeHeadersCannotPublishAfterCallerCancellation()
    {
        using var stop = new CancellationTokenSource();
        var handler = new Handler((_, _) =>
        {
            stop.Cancel();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        await using var client = new OnlineDictionaryClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LookupAsync("cat", stop.Token));
    }

    [Fact]
    public async Task BodyWithoutContentLengthStillCannotExceedBound()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(new UnknownLengthStream(new byte[OnlineDictionaryPolicy.MaximumBytes + 1])) }));
        await using var client = new OnlineDictionaryClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LookupAsync("cat"));
    }

    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> next) : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Next = next;
        internal bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Next(request, cancellationToken);
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
