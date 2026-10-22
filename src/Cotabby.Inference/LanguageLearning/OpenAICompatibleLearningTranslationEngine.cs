using System.Net.Http;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.Inference.LanguageLearning;

/// <summary>
/// A translation-only adapter for one explicitly configured OpenAI-compatible endpoint. The
/// validated endpoint and credential store are fixed for this engine's lifetime. Its planner applies
/// endpoint-model capabilities to the captured settings, then the transport sends only that frozen
/// request; autocomplete settings, prompts, and clients stay independent.
/// </summary>
/// <remarks>
/// The engine sends the frozen learning prompt and full output reservation, then returns only a
/// response the server marked complete. It never reads editor state, rebuilds prompt policy, or
/// borrows the suggestion client, credentials, normalization, or mutable llama runtime.
/// </remarks>
public sealed class OpenAICompatibleLearningTranslationEngine :
    ILearningTranslationEngine,
    ILearningTranslationRequestPlanner,
    IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly OpenAICompatibleEndpointConfiguration _configuration;
    private readonly IOpenAICompatibleCredentialStore _credentials;
    private readonly OpenAICompatibleLearningTranslationClient _client;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private int _disposed;

    /// <summary>
    /// Creates a translation engine from already-validated learning endpoint settings. If a client
    /// is injected, its owner controls its lifetime and must configure it not to follow redirects.
    /// This engine still rejects any 3xx response or final URI change the client exposes.
    /// </summary>
    public OpenAICompatibleLearningTranslationEngine(
        OpenAICompatibleEndpointConfiguration configuration,
        IOpenAICompatibleCredentialStore credentialStore,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(credentialStore);
        if (configuration.ApiMode != OpenAICompatibleApiMode.ChatCompletions)
        {
            // The macOS learning client always uses the instruction-task chat route. A completion
            // mode is an autocomplete transport choice and must not silently create a new learning
            // backend protocol.
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.InvalidRequest);
        }

        _configuration = configuration;
        _credentials = credentialStore;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? CreateHttpClient();
        _client = new OpenAICompatibleLearningTranslationClient(_httpClient);
    }

    /// <summary>
    /// Creates a reusable client for a composition root that owns several immutable learning
    /// endpoint engines over time. The handler has automatic redirects disabled and the client has
    /// no shorter global timeout; each generation owns its own cancellable idle timeout.
    /// </summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <inheritdoc />
    public Task<LearningTranslationRequest> PlanAsync(
        string requestId,
        string source,
        LearningTranslationOptions options,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ct.ThrowIfCancellationRequested();
        if (options is null || options.ModelName is null || options.Style is null)
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        if (_configuration.ModelName.Length == 0)
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.InvalidRequest);
        if (!string.Equals(options.ModelName, _configuration.ModelName, StringComparison.Ordinal))
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.ModelMismatch);

        // TranslateGemma has a fixed translation contract and cannot consume saved style text.
        // Use a natural style only to render this request; keep the captured settings untouched so
        // coordinator state, cache keys, and stale-request comparisons retain the user's snapshot.
        var family = LearningTranslationPrompt.ForEndpointModel(options.ModelName);
        var planningOptions = family == LearningTranslationPrompt.Family.TranslateGemma
            ? options with { Style = new LearningTranslationStyle() }
            : options;
        var request = LearningTranslationRequestFactory.Create(requestId, source, planningOptions);
        return Task.FromResult(request with { Options = options });
    }

    /// <inheritdoc />
    public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct) =>
        TranslateAsync(request, onDelta: null, ct);

    /// <inheritdoc />
    public async Task<string> TranslateAsync(
        LearningTranslationRequest request,
        Action<string>? onDelta,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        ValidateRequest(request);

        string? apiKey;
        try
        {
            apiKey = _credentials.ReadApiKey();
        }
        catch (Exception)
        {
            // Credential storage can include a local file path in its error. The preview should
            // expose only a fixed failure and never send a partially read or fallback key.
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.CredentialUnavailable);
        }

        return await _client.TranslateAsync(_configuration, request, apiKey, onDelta, ct).ConfigureAwait(false);
    }

    /// <summary>Disposes only a client created by this engine; injected clients remain caller-owned.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    private void ValidateRequest(LearningTranslationRequest request)
    {
        var options = request.Options;
        if (options is null || request.Prompt is null || request.Source is null
            || request.Prompt.Prompt is null || request.Prompt.Sampling is null)
        {
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.InvalidRequest);
        }

        if (_configuration.ModelName.Length == 0)
        {
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.InvalidRequest);
        }

        if (!string.Equals(options.ModelName, _configuration.ModelName, StringComparison.Ordinal))
        {
            // The endpoint object is an immutable settings snapshot. Refuse stale/mismatched work
            // instead of silently sending it to whichever model happens to be in this engine.
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.ModelMismatch);
        }

        int sourceBytes;
        int promptBytes;
        try
        {
            // Swift strings cannot contain unpaired UTF-16 surrogates. Reject an impossible
            // Windows string instead of letting JSON encoding silently replace part of the
            // confirmed source with U+FFFD on the way to an endpoint.
            sourceBytes = StrictUtf8.GetByteCount(request.Source);
            promptBytes = StrictUtf8.GetByteCount(request.Prompt.Prompt);
        }
        catch (EncoderFallbackException)
        {
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.InvalidRequest);
        }

        if (request.Source.Length > ModelContextBudget.MaximumTextBytes
            || sourceBytes > ModelContextBudget.MaximumTextBytes
            || promptBytes > OpenAICompatibleLearningTranslationClient.MaximumRequestBytes
            || request.MaxOutputTokens is < 1 or > ModelContextBudget.MaximumTranslationOutputTokens
            || options.ContextTokens is < 1 or > ModelContextBudget.MaximumEndpointTokens
            || request.MaxOutputTokens >= options.ContextTokens
            || string.IsNullOrWhiteSpace(request.Prompt.Prompt)
            || request.Prompt.Prompt.Length > OpenAICompatibleLearningTranslationClient.MaximumPromptCodeUnits
            || _configuration.ModelName.Length > 1_024
            || (long)request.Prompt.Prompt.Length * 6 + (long)_configuration.ModelName.Length * 6 + 256
                > OpenAICompatibleLearningTranslationClient.MaximumRequestBytes
            || !double.IsFinite(request.Prompt.Sampling.Temperature)
            || request.Prompt.Sampling.Temperature < 0
            || !double.IsFinite(request.Prompt.Sampling.TopP)
            || request.Prompt.Sampling.TopP is < 0 or > 1)
        {
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.InvalidRequest);
        }
    }
}

/// <summary>
/// Safe default when the user has not explicitly enabled a learning endpoint. It performs no
/// network work and reports the missing provider instead of fabricating translation text.
/// </summary>
public sealed class UnavailableLearningTranslationEngine : ILearningTranslationEngine
{
    public static UnavailableLearningTranslationEngine Instance { get; } = new();

    private UnavailableLearningTranslationEngine()
    {
    }

    /// <inheritdoc />
    public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromException<string>(
            new LearningTranslationEndpointException(LearningTranslationEndpointFailure.NotConfigured));
    }
}
