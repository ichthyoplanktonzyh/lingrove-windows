using System.Net.Http;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.Inference.LanguageLearning;

/// <summary>Explanation prompt/complete-result boundary over the independent instruction
/// transport. Construction is inert; disposal closes transport admission and joins prompt work.</summary>
public sealed class LearningEndpointExplanationEngine : ILearningExplanationEngine
{
    private readonly LearningModelSnapshot _profile;
    private readonly LearningEndpointInstructionEngine _instruction;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task<LearningExplanationResult>? _work;
    private Task? _retirement;

    public LearningEndpointExplanationEngine(LearningModelSnapshot profile,
        IOpenAICompatibleCredentialStore credentials, HttpClient? httpClient = null)
    {
        _profile = (profile ?? throw new ArgumentNullException(nameof(profile))).ForExplanation();
        _instruction = new(_profile, credentials, httpClient);
    }

    public Task<LearningExplanationResult> GenerateAsync(LearningExplanationInput input,
        LearningLanguage responseLanguage, int maximumOutputTokens, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_retirement is not null) throw new OperationCanceledException();
            if (_work is not null) throw new InvalidOperationException("An explanation facade owns one request.");
            return _work = Task.Run(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
                linked.Token.ThrowIfCancellationRequested();
                if (input is null || !LearningExplanationPolicy.IsValid(input) || !Enum.IsDefined(responseLanguage))
                    throw new LearningExplanationException(LearningExplanationFailure.InvalidContent);
                int output = LearningExplanationModelPolicy.ResolveOutputTokens(maximumOutputTokens);
                string prompt = LearningExplanationPolicy.CurrentPrompt(input, responseLanguage, _profile.ContextWindowTokens, output);
                linked.Token.ThrowIfCancellationRequested();
                string raw = await _instruction.GenerateAsync(prompt, output, 24_000, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                return LearningExplanationPolicy.Parse(raw);
            });
        }
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
            try { await _instruction.DisposeAsync().ConfigureAwait(false); }
            finally { if (work is not null) { try { await work.ConfigureAwait(false); } catch { } } }
            try { await cancellation.ConfigureAwait(false); } catch { }
        }
        finally { _shutdown.Dispose(); }
    }
}
