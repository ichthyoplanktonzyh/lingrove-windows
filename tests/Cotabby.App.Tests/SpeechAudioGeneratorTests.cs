using System.Net;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;
using Cotabby.Tests.Speech;
namespace Cotabby.App.Tests;

public sealed class SpeechAudioGeneratorTests
{
    [Fact]
    public async Task LocalRoutePublishesPcmAndNeverConstructsAnEndpointTransport()
    {
        await using var fixture = new SpeechRuntimeFixture(); int endpointCalls = 0;
        var generator = new SpeechAudioGenerator(fixture.Runtime, new SpeechEndpointClient(() => { endpointCalls++; throw new InvalidOperationException(); }));
        var chunks = new List<SpeechAudioChunk>();
        await generator.GenerateAsync(fixture.Request("first"), value => { chunks.Add(value); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(9600, Assert.IsType<SpeechAudioChunk.Pcm>(Assert.Single(chunks)).Value.ByteCount); Assert.Equal(0, endpointCalls);
        await generator.ShutdownAsync(); Assert.All(fixture.Factory.Processes, value => Assert.True(value.HasExited));
    }

    [Fact]
    public async Task EndpointRoutePublishesOneEncodedResponseWithoutStartingLocalAssets()
    {
        await using var fixture = new SpeechRuntimeFixture(); var handler = new EndpointHandler();
        var generator = new SpeechAudioGenerator(fixture.Runtime, new SpeechEndpointClient(() => handler)); var chunks = new List<SpeechAudioChunk>();
        await generator.GenerateAsync(EndpointRequest(), value => { chunks.Add(value); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<SpeechAudioChunk.Encoded>(Assert.Single(chunks)).CopyBytes());
        Assert.Empty(fixture.Factory.Processes); Assert.Equal(0, fixture.Pressure.Subscriptions); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CancelByTokenDoesNotCancelAnEndpointButItsCallerCancellationDoes()
    {
        await using var fixture = new SpeechRuntimeFixture(); var handler = new EndpointHandler(wait: true);
        var generator = new SpeechAudioGenerator(fixture.Runtime, new SpeechEndpointClient(() => handler));
        using var cancellation = new CancellationTokenSource(); var request = EndpointRequest(); int received = 0;
        var call = generator.GenerateAsync(request, _ => { received++; return Task.CompletedTask; }, cancellation.Token);
        try
        {
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await generator.CancelAsync(request.Token); await generator.ShutdownAsync();
            Assert.False(call.IsCompleted); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            Assert.Equal(0, received); Assert.Empty(fixture.Factory.Processes);
        }
        finally { cancellation.Cancel(); try { await call; } catch (OperationCanceledException) { } }
    }

    [Fact]
    public async Task EndpointFailureCannotFallBackToLocalGeneration()
    {
        await using var fixture = new SpeechRuntimeFixture(); var handler = new EndpointHandler(status: HttpStatusCode.BadRequest);
        var generator = new SpeechAudioGenerator(fixture.Runtime, new SpeechEndpointClient(() => handler));
        var error = await Assert.ThrowsAsync<SpeechException>(() => generator.GenerateAsync(EndpointRequest(), _ => throw new InvalidOperationException("No audio expected"), CancellationToken.None));
        Assert.Equal(SpeechFailure.EndpointStatus, error.Failure); Assert.Empty(fixture.Factory.Processes);
    }
    private static SpeechAudioRequest EndpointRequest() => new(new(), "explicit fixture phrase", LearningLanguage.English,
        new SpeechAudioSource.Endpoint(new(new OpenAICompatibleEndpointConfiguration("https://fixture.test", "fixture-model", OpenAICompatibleApiMode.ChatCompletions), "fixture-voice"), "fixture-key"));
    private sealed class EndpointHandler(bool wait = false, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        internal int Calls; internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; Entered.TrySetResult(); if (wait) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return new(status) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) }; }
    }
}
