using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Cotabby.Core.Review;

namespace Cotabby.Win32.Review;

/// <summary>App-owned local AnkiConnect HTTP service. Unlike inference transports this accepts
/// only the original loopback allowlist. One instance owns its cookie-free, redirect-free session;
/// controllers borrow it and retain transaction/recovery state outside this transport.</summary>
public sealed class AnkiConnectClient : IAnkiConnecting, IDisposable
{
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _clock;
    public AnkiConnectClient() : this(CreateHandler()) { }
    internal AnkiConnectClient(HttpMessageHandler handler) : this(handler, TimeSpan.FromSeconds(10)) { }
    internal TimeSpan RequestTimeout => _timeout;
    // Handler/deadline injection is a component-test seam, never an endpoint/user setting.
    internal AnkiConnectClient(HttpMessageHandler handler, TimeSpan timeout)
        : this(handler, timeout, TimeProvider.System) { }
    internal AnkiConnectClient(HttpMessageHandler handler, TimeSpan timeout, TimeProvider clock)
    { _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan }; _timeout = timeout; _clock = clock; }
    internal static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        UseDefaultCredentials = false, AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli,
    };
    public async Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters,
        AnkiConnection connection, CancellationToken cancellationToken = default)
    {
        string? endpoint = AnkiArchiveEndpointPolicy.Normalize(connection.Endpoint);
        if (endpoint is null) throw new AnkiReviewException(AnkiWirePolicy.InvalidResponse);
        // Encoding is deliberately outside transport error mapping, matching the original.
        byte[] body = AnkiWirePolicy.Request(action, parameters, connection.ApiKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, UriKind.Absolute));
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var idleTimeout = new CancellationTokenSource(_timeout, _clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idleTimeout.Token);
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.OK) throw new AnkiReviewException(AnkiWirePolicy.InvalidResponse);
            idleTimeout.CancelAfter(_timeout);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                // URLRequest uses an idle-data timeout, not a total-operation deadline. Reset
                // before each asynchronous read so sustained progress may exceed ten seconds.
                idleTimeout.CancelAfter(_timeout);
                int count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, AnkiWirePolicy.MaximumResponseBytes - bytes.Length + 1)), deadline.Token).ConfigureAwait(false);
                if (count == 0) break;
                if (bytes.Length + count > AnkiWirePolicy.MaximumResponseBytes) throw new AnkiReviewException(AnkiWirePolicy.InvalidResponse);
                bytes.Write(buffer, 0, count);
            }
            idleTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            deadline.Token.ThrowIfCancellationRequested();
            return AnkiWirePolicy.Response(bytes.ToArray());
        }
        catch (AnkiReviewException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        { throw new AnkiReviewException(AnkiWirePolicy.CouldNotConnect); }
    }
    public void Dispose() => _client.Dispose();
}
