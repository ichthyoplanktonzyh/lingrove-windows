using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cotabby.Core.Speech;

namespace Cotabby.Inference.Speech;

/// <summary>A speech-only HTTP boundary borrowed by the independent audio generator. An explicit
/// action supplies the already selected destination and only its clicked phrase/model/voice.
/// Each call owns a fresh cookie-free, nonredirecting transport and bounded response; cancellation
/// and disposal cannot affect a newer utterance. No body, credential or audio is logged or cached.</summary>
public sealed class SpeechEndpointClient(Func<HttpMessageHandler>? handlerFactory = null, TimeProvider? clock = null)
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan ResourceTimeout = TimeSpan.FromSeconds(120);
    private readonly Func<HttpMessageHandler> _handlerFactory = handlerFactory ?? CreateHandler;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    };

    public async Task<byte[]> AudioAsync(string text, SpeechEndpointConfiguration configuration, string? apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var resource = new CancellationTokenSource(ResourceTimeout, _clock);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, resource.Token);
        // The native source uses an ephemeral URLSession per request. Sharing HttpClient's
        // cancellation state or a cookie container would violate that ownership boundary.
        using var http = new HttpClient(_handlerFactory(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Post, configuration.Endpoint.ApiUrl("audio/speech"));
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        { ["model"] = configuration.Endpoint.ModelName, ["input"] = text, ["voice"] = configuration.Voice, ["response_format"] = "wav" }));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Accept.ParseAdd("audio/wav, audio/mpeg");
        if (!string.IsNullOrEmpty(apiKey))
        {
            try { request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey); }
            catch (FormatException) { throw new SpeechException(SpeechFailure.GenerationFailed); }
        }
        using var response = await WithIdleAsync(token => http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new SpeechException(SpeechFailure.EndpointStatus, (int)response.StatusCode);
        if (response.Content.Headers.ContentLength > MaximumBytes) throw new SpeechException(SpeechFailure.ResponseTooLarge);
        // Match CFNetwork's transparent gzip/deflate decoding. For compressed responses the
        // handler removes the encoded Content-Length; the streamed bound below applies to decoded
        // bytes. An incomplete compressed response remains a transport failure, as on URLSession.
        await using var stream = await WithIdleAsync(token => response.Content.ReadAsStreamAsync(token)).ConfigureAwait(false);
        using var bytes = new MemoryStream(); byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int count = await WithIdleAsync(token => stream.ReadAsync(buffer.AsMemory(), token).AsTask()).ConfigureAwait(false);
            if (count == 0) break;
            if (count > MaximumBytes - bytes.Length) throw new SpeechException(SpeechFailure.ResponseTooLarge);
            bytes.Write(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length == 0) throw new SpeechException(SpeechFailure.InvalidAudio);
        return bytes.ToArray();

        async Task<T> WithIdleAsync<T>(Func<CancellationToken, Task<T>> action)
        {
            // URLRequest's inactivity deadline resets with progress; the URLSession resource
            // deadline bounds the whole exchange. Do not replace both with a 90-second total.
            using var idle = new CancellationTokenSource(IdleTimeout, _clock);
            using var step = CancellationTokenSource.CreateLinkedTokenSource(operation.Token, idle.Token);
            try { return await action(step.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && (idle.IsCancellationRequested || resource.IsCancellationRequested))
            { throw new TimeoutException("The speech request timed out."); }
        }
    }
}
