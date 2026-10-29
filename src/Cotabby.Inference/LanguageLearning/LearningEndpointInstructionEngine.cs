using System.Net.Http;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.Inference.LanguageLearning;

/// <summary>One explicit explanation-role endpoint request. The role owner injects its own
/// immutable profile, credential store and HTTP pool. No autocomplete/translation state or
/// local model is borrowed, and credentials are read only after a valid request is admitted.</summary>
public sealed class LearningEndpointInstructionEngine : ILearningInstructionEngine
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly LearningModelSnapshot _profile;
    private readonly IOpenAICompatibleCredentialStore _credentials;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task<string>? _work;
    private Task? _retirement;

    public LearningEndpointInstructionEngine(LearningModelSnapshot profile,
        IOpenAICompatibleCredentialStore credentials, HttpClient? httpClient = null)
    {
        _profile = (profile ?? throw new ArgumentNullException(nameof(profile))).ForExplanation();
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _ownsHttp = httpClient is null;
        _http = httpClient ?? CreateHttpClient();
    }

    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };

    public Task<string> GenerateAsync(string prompt, int output, int maximumBytes, CancellationToken token, Func<bool>? admission = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_retirement is not null) throw new OperationCanceledException();
            if (_work is not null) throw new InvalidOperationException("An instruction facade owns one request.");
            return _work = Task.Run(() => GenerateCoreAsync(prompt, output, maximumBytes, token, admission));
        }
    }

    private async Task<string> GenerateCoreAsync(string prompt, int output, int maximumBytes, CancellationToken caller, Func<bool>? admission)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _shutdown.Token);
        void Check() { caller.ThrowIfCancellationRequested(); _shutdown.Token.ThrowIfCancellationRequested(); linked.Token.ThrowIfCancellationRequested(); }
        bool Permitted() { Check(); return admission?.Invoke() ?? true; }
        Check();
        if (_profile.Backend != LearningModelBackend.Endpoint || string.IsNullOrWhiteSpace(_profile.EndpointModel))
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.NotConfigured);
        if (!LearningExplanationModelPolicy.SupportsGeneralInstructions(_profile.EndpointModel, LearningChatTemplate.Automatic))
            throw new LearningExplanationException(LearningExplanationFailure.InstructionModelRequired);
        if (output is < 1 or > 4096 || maximumBytes is < 1 or > 24_000
            || _profile.ContextWindowTokens is < 1024 or > ModelContextBudget.MaximumEndpointTokens
            || output >= _profile.ContextWindowTokens)
            throw new LearningExplanationException(LearningExplanationFailure.ContextBudgetExceeded);
        try
        {
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > OpenAICompatibleLearningTranslationClient.MaximumPromptCodeUnits
                || _profile.EndpointModel.Length > 1024
                || _profile.BaseUrl.Length > 4096 || StrictUtf8.GetByteCount(_profile.BaseUrl) > 16384
                || StrictUtf8.GetByteCount(_profile.EndpointModel) > 4096
                || (long)prompt.Length * 6 + (long)_profile.EndpointModel.Length * 6 + 256 > OpenAICompatibleLearningTranslationClient.MaximumRequestBytes
                || StrictUtf8.GetByteCount(prompt) > OpenAICompatibleLearningTranslationClient.MaximumRequestBytes)
                throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.InvalidRequest);
        }
        catch (EncoderFallbackException) { throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.InvalidRequest); }
        var configuration = new OpenAICompatibleEndpointConfiguration(_profile.BaseUrl, _profile.EndpointModel, OpenAICompatibleApiMode.ChatCompletions);
        if (!Permitted()) throw new OperationCanceledException();
        string? apiKey;
        try { apiKey = _credentials.ReadApiKey(); }
        catch { throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.CredentialUnavailable); }
        Check();
        var sampling = _profile.SamplingOverrides.EndpointOnly;
        var request = new LearningInstructionEndpointRequest(prompt, false,
            LearningInstructionSampling.Standard with { Temperature = sampling.Temperature ?? LearningInstructionSampling.Standard.Temperature,
                TopP = sampling.TopP ?? LearningInstructionSampling.Standard.TopP }, output, maximumBytes, Permitted);
        var raw = await new OpenAICompatibleLearningTranslationClient(_http).GenerateAsync(configuration, request, apiKey, null, linked.Token).ConfigureAwait(false);
        Check();
        if (!Permitted()) throw new OperationCanceledException();
        try { if (StrictUtf8.GetByteCount(raw) > maximumBytes) throw new LearningExplanationException(LearningExplanationFailure.InvalidContent); }
        catch (EncoderFallbackException) { throw new LearningExplanationException(LearningExplanationFailure.InvalidContent); }
        return raw;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_retirement ??= RetireAsync(_work));
    }
    private async Task RetireAsync(Task? work)
    {
        Task cancellation;
        try { cancellation = _shutdown.CancelAsync(); } catch { cancellation = Task.CompletedTask; }
        try
        {
            if (work is not null) { try { await work.ConfigureAwait(false); } catch { } }
            try { await cancellation.ConfigureAwait(false); } catch { }
        }
        finally { if (_ownsHttp) _http.Dispose(); _shutdown.Dispose(); }
    }
}
