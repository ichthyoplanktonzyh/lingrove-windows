using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Cotabby.Inference.OpenAICompatible;

/// <summary>Sampling values shared by the completions and chat-completions request shapes.</summary>
public sealed record OpenAICompatibleGenerationOptions(int MaxPredictionTokens, double Temperature, double TopP);

/// <summary>A failed endpoint exchange. The message is user-facing.</summary>
public sealed class OpenAICompatibleClientException : Exception
{
    private OpenAICompatibleClientException(string message) : base(message)
    {
    }

    public static OpenAICompatibleClientException Server(int statusCode) =>
        new($"The endpoint returned HTTP {statusCode}.");

    public static OpenAICompatibleClientException MalformedResponse() =>
        new("The endpoint returned an invalid OpenAI-compatible response.");

    public static OpenAICompatibleClientException StreamError(string message) =>
        new($"The endpoint reported an error: {message}");
}

/// <summary>Stateless HTTP boundary for OpenAI-compatible servers.</summary>
public sealed class OpenAICompatibleApiClient : IDisposable
{
    public static readonly TimeSpan ModelListTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Longest silence tolerated from a generation or preload request.</summary>
    public static readonly TimeSpan GenerationTimeout = TimeSpan.FromSeconds(120);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public OpenAICompatibleApiClient(HttpMessageHandler? handler = null)
    {
        // Each request carries its own timeout, so the client-wide 100 second default must not win.
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = true;
    }

    private OpenAICompatibleApiClient(HttpClient http, bool ownsHttp) { _http = http; _ownsHttp = ownsHttp; }
    /// <summary>Creates a separate stateless facade over a task-owned pool. Its owner must disable
    /// redirects/cookies and drain all requests before retiring that pool.</summary>
    public static OpenAICompatibleApiClient Borrowing(HttpClient http)
    { ArgumentNullException.ThrowIfNull(http); return new(http, false); }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }

    /// <summary>Lists the endpoint's models sorted by identifier.</summary>
    public Task<IReadOnlyList<OpenAICompatibleModelOption>> FetchModelsAsync(
        OpenAICompatibleEndpointConfiguration configuration,
        string? apiKey,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, configuration.ApiUrl("models"));
        return SendAsync<IReadOnlyList<OpenAICompatibleModelOption>>(request, apiKey, ModelListTimeout, async (response, timeout) =>
        {
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                const int maximum = 1024 * 1024;
                if (response.Content.Headers.ContentLength is > maximum) throw OpenAICompatibleClientException.MalformedResponse();
                using var bytes = new MemoryStream(); var buffer = new byte[8192];
                while (true)
                {
                    int count = await body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximum + 1 - (int)bytes.Length)), timeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    bytes.Write(buffer, 0, count);
                    if (bytes.Length > maximum) throw OpenAICompatibleClientException.MalformedResponse();
                }
                var payload = JsonSerializer.Deserialize<ModelListResponse>(bytes.ToArray(), OpenAICompatibleJson.Options);
                if (payload?.Data is null || payload.Data.Count > 512
                    || payload.Data.Any(model => model is null || string.IsNullOrWhiteSpace(model.Id)
                        || model.Id.Length > 1024 || model.Id.Any(char.IsControl))) throw OpenAICompatibleClientException.MalformedResponse();
                timeout.Token.ThrowIfCancellationRequested();
                return payload.Data
                    .OrderBy(model => model.Id, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (JsonException)
            {
                throw OpenAICompatibleClientException.MalformedResponse();
            }
        }, ct);
    }

    /// <summary>
    /// Loads the selected model through Ollama's native API. Returns false without sending
    /// anything when the endpoint is not the default local Ollama server.
    /// </summary>
    public async Task<bool> PreloadDefaultOllamaModelAsync(
        OpenAICompatibleEndpointConfiguration configuration,
        string? apiKey,
        CancellationToken ct = default)
    {
        RequireModelName(configuration);
        if (configuration.DefaultOllamaGenerateUrl is not { } url) return false;

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonBody(new OllamaPreloadRequest(configuration.ModelName, Prompt: "", Stream: false, KeepAlive: -1)),
        };
        return await SendAsync(request, apiKey, GenerationTimeout, (_, _) => Task.FromResult(true), ct)
            .ConfigureAwait(false);
    }

    /// <summary>Streams one generation and returns the accumulated raw text.</summary>
    public async Task<string> GenerateAsync(
        OpenAICompatibleEndpointConfiguration configuration,
        string? apiKey,
        string prompt,
        OpenAICompatibleGenerationOptions options,
        CancellationToken ct = default, Action<string>? onPartialRawText = null)
    {
        RequireModelName(configuration);

        var mode = configuration.ApiMode;
        var request = new HttpRequestMessage(HttpMethod.Post, configuration.ApiUrl(mode.Route()))
        {
            Content = JsonBody(mode == OpenAICompatibleApiMode.Completions
                ? new CompletionRequest(
                    configuration.ModelName, prompt, Stream: true,
                    options.MaxPredictionTokens, options.Temperature, options.TopP)
                : new ChatCompletionRequest(
                    configuration.ModelName,
                    [new ChatMessage("user", ChatInstruction + prompt)],
                    Stream: true,
                    options.MaxPredictionTokens, options.Temperature, options.TopP,
                    ReasoningEffort: "none")),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return await SendAsync(request, apiKey, GenerationTimeout, async (response, timeout) =>
        {
            var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var accumulated = new StringBuilder();
            var sawEvent = false;
            await foreach (var line in ReadBoundedLinesAsync(reader, timeout.Token).ConfigureAwait(false))
            {
                timeout.CancelAfter(GenerationTimeout);
                var sseEvent = OpenAICompatibleSseDecoder.Decode(line, mode);
                switch (sseEvent.Kind)
                {
                    case OpenAICompatibleSseEventKind.Done:
                        return accumulated.ToString();
                    case OpenAICompatibleSseEventKind.Text:
                        sawEvent = true;
                        if (sseEvent.Value!.Length > 65_536 - accumulated.Length) throw OpenAICompatibleClientException.MalformedResponse();
                        accumulated.Append(sseEvent.Value);
                        if (onPartialRawText is not null)
                            try { onPartialRawText(accumulated.ToString()); } catch { /* Observers cannot break transport. */ }
                        break;
                    case OpenAICompatibleSseEventKind.Error:
                        throw OpenAICompatibleClientException.StreamError(sseEvent.Value!);
                }
            }

            // A stream that closes without [DONE] still produced a usable answer.
            return sawEvent ? accumulated.ToString() : throw OpenAICompatibleClientException.MalformedResponse();
        }, ct).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<string> ReadBoundedLinesAsync(StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        const int maxLine = 262_144;
        var buffer = new char[4096]; var line = new StringBuilder(); bool previousCr = false;
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0) break;
            for (int i = 0; i < read; i++)
            {
                char next = buffer[i];
                if (next == '\n' && previousCr) { previousCr = false; continue; }
                previousCr = false;
                if (next is '\r' or '\n')
                {
                    previousCr = next == '\r';
                    yield return line.ToString(); line.Clear();
                }
                else
                {
                    if (line.Length == maxLine) throw OpenAICompatibleClientException.MalformedResponse();
                    line.Append(next);
                }
            }
        }
        if (line.Length > 0) yield return line.ToString();
    }

    // The shared prompt is a base-model continuation that ends at the caret. Chat models tend to
    // answer by repeating that final line, so the single user message states the expected output.
    private const string ChatInstruction =
        "Continue the text at the end of the context. Reply with only new continuation " +
        "text; do not repeat or quote existing text.\n\n";

    private static void RequireModelName(OpenAICompatibleEndpointConfiguration configuration)
    {
        if (configuration.ModelName.Length == 0)
        {
            throw new OpenAICompatibleEndpointException(OpenAICompatibleEndpointError.EmptyModelName);
        }
    }

    private static ByteArrayContent JsonBody(object payload)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload, OpenAICompatibleJson.Options));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    /// <summary>
    /// Sends with bearer authorization and an idle timeout that <paramref name="read"/> may
    /// restart. A timeout surfaces as <see cref="TimeoutException"/>, caller cancellation as
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        string? apiKey,
        TimeSpan idleTimeout,
        Func<HttpResponseMessage, CancellationTokenSource, Task<T>> read,
        CancellationToken ct)
    {
        using var _ = request;
        if (apiKey?.Trim() is { Length: > 0 } key)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(idleTimeout);
        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw OpenAICompatibleClientException.Server((int)response.StatusCode);
            }
            return await read(response, timeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The request timed out.");
        }
    }
}

public enum OpenAICompatibleSseEventKind
{
    Ignore,
    Done,
    Text,
    Error,
}

public readonly record struct OpenAICompatibleSseEvent(OpenAICompatibleSseEventKind Kind, string? Value = null)
{
    public static readonly OpenAICompatibleSseEvent Ignore = new(OpenAICompatibleSseEventKind.Ignore);
    public static readonly OpenAICompatibleSseEvent Done = new(OpenAICompatibleSseEventKind.Done);
    public static OpenAICompatibleSseEvent Text(string text) => new(OpenAICompatibleSseEventKind.Text, text);
    public static OpenAICompatibleSseEvent Error(string message) => new(OpenAICompatibleSseEventKind.Error, message);
}

/// <summary>Pure line decoder for OpenAI's data-only SSE streams.</summary>
public static class OpenAICompatibleSseDecoder
{
    private static readonly string[] MetadataPrefixes = [":", "event:", "id:", "retry:"];

    /// <exception cref="OpenAICompatibleClientException">The payload is not a stream chunk.</exception>
    public static OpenAICompatibleSseEvent Decode(string line, OpenAICompatibleApiMode mode)
    {
        var payload = line.Trim();
        if (payload.Length == 0 || MetadataPrefixes.Any(prefix => payload.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return OpenAICompatibleSseEvent.Ignore;
        }
        if (payload.StartsWith("data:", StringComparison.Ordinal))
        {
            payload = payload["data:".Length..].TrimStart();
        }
        if (payload == "[DONE]") return OpenAICompatibleSseEvent.Done;

        StreamChunk? chunk;
        try
        {
            chunk = JsonSerializer.Deserialize<StreamChunk>(payload, OpenAICompatibleJson.Options);
        }
        catch (JsonException)
        {
            chunk = null;
        }
        if (chunk is null) throw OpenAICompatibleClientException.MalformedResponse();

        if (chunk.Error is { Message.Length: > 0 } error) return OpenAICompatibleSseEvent.Error(error.Message);
        if (chunk.Choices is not [{ } choice, ..]) return OpenAICompatibleSseEvent.Ignore;
        var text = mode == OpenAICompatibleApiMode.Completions
            ? choice.Text
            : choice.Delta?.Content ?? choice.Message?.Content;
        return string.IsNullOrEmpty(text) ? OpenAICompatibleSseEvent.Ignore : OpenAICompatibleSseEvent.Text(text);
    }
}

internal static class OpenAICompatibleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
}

internal sealed record ModelListResponse(IReadOnlyList<OpenAICompatibleModelOption> Data);

// Ollama treats an empty, non-streaming generate request as a model load. A keep_alive of -1
// keeps the weights resident until Ollama stops or is asked to unload them.
internal sealed record OllamaPreloadRequest(string Model, string Prompt, bool Stream, int KeepAlive);

internal sealed record CompletionRequest(
    string Model,
    string Prompt,
    bool Stream,
    int MaxTokens,
    double Temperature,
    double TopP);

internal sealed record ChatMessage(string Role, string Content);

// A reasoning_effort of none keeps reasoning-capable models from spending the short token budget
// on an invisible thinking channel. Models without reasoning ignore the field.
internal sealed record ChatCompletionRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    bool Stream,
    int MaxTokens,
    double Temperature,
    double TopP,
    string ReasoningEffort);

internal sealed record StreamChunk(IReadOnlyList<StreamChoice?>? Choices = null, StreamErrorPayload? Error = null);

internal sealed record StreamChoice(string? Text = null, StreamContent? Delta = null, StreamContent? Message = null);

internal sealed record StreamContent(string? Content = null);

internal sealed record StreamErrorPayload(string Message);
