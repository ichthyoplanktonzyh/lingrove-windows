using System.Runtime.ExceptionServices;
using Cotabby.Core.LanguageLearning;
using LLama.Native;

namespace Cotabby.Inference.LanguageLearning.Local;

/// <summary>
/// Plans and generates one local learning translation against one frozen model configuration.
/// The app owns the reusable runtime; this short-lived facade owns only its exclusive operation
/// lease, exact prompt token plan, and cancellation lifetime.
/// </summary>
/// <remarks>
/// Planning resolves the chat template from GGUF metadata, asks Core to render the task for that
/// explicit family, then tokenizes trusted framing separately from the literal prompt body. The
/// resulting token IDs are paired with exactly one immutable request so another capture cannot
/// reuse this model allocation or its sampler state.
/// </remarks>
public sealed class LearningLocalTranslationEngine :
    ILearningTranslationEngine,
    ILearningTranslationRequestPlanner,
    IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly LearningLocalModelConfiguration _configuration;
    private readonly ILearningLocalRuntime _runtime;
    private readonly CancellationTokenSource _lifetime = new();

    private ILearningLocalOperation? _operation;
    private PreparedRequest? _prepared;
    private TaskCompletionSource? _planningCompleted;
    private Task? _disposeTask;
    private bool _planStarted;
    private bool _translationStarted;
    private bool _disposed;

    public LearningLocalTranslationEngine(
        LearningLocalModelConfiguration configuration,
        ILearningLocalRuntime runtime)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <inheritdoc />
    public async Task<LearningTranslationRequest> PlanAsync(
        string requestId,
        string source,
        LearningTranslationOptions options,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        TaskCompletionSource planningCompleted;
        CancellationToken lifetimeToken;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_planStarted)
                throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);

            _planStarted = true;
            planningCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _planningCompleted = planningCompleted;
            lifetimeToken = _lifetime.Token;
        }

        ILearningLocalOperation? operation = null;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetimeToken);
            linked.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(_configuration.ModelPath))
                throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
            if (options is null || options.Style is null)
                throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);

            // Preserve the direct capture and facade-owner tokens through admission. CancelAsync
            // retires their flags immediately, before linked-token callbacks may have run.
            var activeOperation = await _runtime.BeginOperationAsync(_configuration, ct, lifetimeToken).ConfigureAwait(false);
            operation = activeOperation;
            linked.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _operation = activeOperation;
                ThrowIfDisposed();
            }

            var profile = LearningLocalPrompt.ResolveProfile(_configuration, activeOperation.Metadata);
            // TranslateGemma does not support saved translation style instructions. Suppress the
            // dormant preference only for prompt rendering and keep the captured settings value
            // on the returned request for coordinator identity and cache matching.
            var planningOptions = LearningTranslationPrompt.SupportsStyleInstructions(profile.Family)
                ? options
                : options with { Style = new LearningTranslationStyle() };
            IReadOnlyList<LLamaToken>? promptTokens = null;

            int CountExactPromptTokens(string renderedPrompt)
            {
                linked.Token.ThrowIfCancellationRequested();
                var segments = LearningLocalPrompt.GetSegments(profile.Template, renderedPrompt);
                var prefix = activeOperation.TokenizeTrusted(segments.TrustedPrefix);
                linked.Token.ThrowIfCancellationRequested();
                var body = activeOperation.TokenizeLiteral(segments.LiteralBody);
                linked.Token.ThrowIfCancellationRequested();
                var suffix = activeOperation.TokenizeTrusted(segments.TrustedSuffix);

                var tokenPlan = new LLamaToken[prefix.Count + body.Count + suffix.Count];
                var index = 0;
                CopyTokens(prefix, tokenPlan, ref index);
                CopyTokens(body, tokenPlan, ref index);
                CopyTokens(suffix, tokenPlan, ref index);
                promptTokens = tokenPlan;
                return tokenPlan.Length;
            }

            var request = LearningTranslationRequestFactory.Create(
                requestId,
                source,
                planningOptions,
                profile.Family,
                activeOperation.ContextWindowTokens,
                CountExactPromptTokens);
            request = request with { Options = options };

            linked.Token.ThrowIfCancellationRequested();
            if (promptTokens is null)
                throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);

            // The exact factory count protects the Mac input allowance, including its framing
            // margin. This second policy check preserves the specialist cap and full-context
            // invariant against the context size the loaded native allocation actually reports.
            LearningLocalPrompt.ValidateTokenBudget(
                promptTokens.Count,
                request.MaxOutputTokens,
                profile.Template,
                activeOperation.ContextWindowTokens);

            // The factory keeps the captured options unchanged for coordinator identity and cache
            // checks. The resolved profile owns local sampler overrides, including the seed mode.
            var prepared = new PreparedRequest(request, promptTokens, profile.Sampling, activeOperation);
            lock (_gate)
            {
                ThrowIfDisposed();
                _prepared = prepared;
            }

            linked.Token.ThrowIfCancellationRequested();
            return request;
        }
        catch (LearningLocalRuntimeException error)
        {
            await ReleaseAfterPlanningFailureAsync(operation).ConfigureAwait(false);
            throw MapRuntimeFailure(error);
        }
        catch (OperationCanceledException)
        {
            await ReleaseAfterPlanningFailureAsync(operation).ConfigureAwait(false);
            throw;
        }
        catch (LearningTranslationException)
        {
            await ReleaseAfterPlanningFailureAsync(operation).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await ReleaseAfterPlanningFailureAsync(operation).ConfigureAwait(false);
            throw new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed);
        }
        finally
        {
            planningCompleted.TrySetResult();
        }
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
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        PreparedRequest prepared;
        CancellationToken lifetimeToken;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_prepared is null || !Equals(_prepared.Request, request) || _translationStarted)
                throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);

            _translationStarted = true;
            prepared = _prepared;
            lifetimeToken = _lifetime.Token;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetimeToken);
        var callbackFailure = new CallbackExceptionCapture();
        Action<string>? forwardDelta = onDelta is null
            ? null
            : delta =>
            {
                linked.Token.ThrowIfCancellationRequested();
                try
                {
                    onDelta(delta);
                }
                catch (Exception error)
                {
                    callbackFailure.Capture(error);
                    throw;
                }
            };

        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var result = await prepared.Operation.GenerateAsync(
                prepared.PromptTokens,
                prepared.Sampling,
                request.MaxOutputTokens,
                forwardDelta,
                linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception error)
        {
            // Preserve a Core cancellation or callback policy error exactly. Native and transport
            // exception text can include paths, model details, or prompt data, so other failures
            // cross this boundary only through a fixed Core category.
            callbackFailure.ThrowIfCaptured();
            if (error is LearningTranslationException or OperationCanceledException) throw;
            if (error is LearningLocalRuntimeException runtimeFailure)
                throw MapRuntimeFailure(runtimeFailure);
            throw new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed);
        }
    }

    /// <summary>
    /// Cancels current work and releases this facade's operation lease. The app-lifetime runtime
    /// remains alive for the next request; its operation safely drains any in-flight native call.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Task? planning;
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);

            _disposed = true;
            planning = _planningCompleted?.Task;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        try
        {
            _lifetime.Cancel();
        }
        catch (Exception)
        {
            // Cancellation callbacks belong to the native lease and are not user-facing data.
        }

        _ = CompleteDisposalAsync(planning, completion);
        return new ValueTask(completion.Task);
    }

    private async Task CompleteDisposalAsync(Task? planning, TaskCompletionSource completion)
    {
        try
        {
            if (planning is not null) await planning.ConfigureAwait(false);

            ILearningLocalOperation? operation;
            lock (_gate)
            {
                operation = _operation;
                _operation = null;
                _prepared = null;
            }

            if (operation is not null)
            {
                try
                {
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
                catch (LearningTranslationException)
                {
                    throw;
                }
                catch (LearningLocalRuntimeException error)
                {
                    throw MapRuntimeFailure(error);
                }
                catch (Exception)
                {
                    throw new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed);
                }
            }

            _lifetime.Dispose();
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            _lifetime.Dispose();
            completion.TrySetException(error is LearningTranslationException
                ? error
                : new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed));
        }
    }

    private async ValueTask ReleaseAfterPlanningFailureAsync(ILearningLocalOperation? operation)
    {
        if (operation is null) return;

        try
        {
            await operation.DisposeAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (ReferenceEquals(_operation, operation))
                {
                    _operation = null;
                    _prepared = null;
                }
            }
        }
        catch (Exception)
        {
            // Keep the original planning failure. Retain the lease field so DisposeAsync can retry
            // draining it instead of losing ownership after an unsuccessful release attempt.
        }
    }

    private static LearningTranslationException MapRuntimeFailure(LearningLocalRuntimeException error) =>
        error.Failure switch
        {
            LearningLocalRuntimeFailure.InvalidRequest =>
                new LearningTranslationException(LearningTranslationFailure.InvalidOptions),
            LearningLocalRuntimeFailure.PromptBudgetExceeded =>
                new LearningTranslationException(LearningTranslationFailure.OversizedTarget),
            LearningLocalRuntimeFailure.ModelUnavailable or LearningLocalRuntimeFailure.ModelLoadFailed =>
                new LearningTranslationException(LearningTranslationFailure.LocalModelUnavailable),
            LearningLocalRuntimeFailure.OutputLimitReached =>
                new LearningTranslationException(LearningTranslationFailure.LocalOutputTruncated),
            LearningLocalRuntimeFailure.DecodeFailed or
                LearningLocalRuntimeFailure.InvalidUtf8 or
                LearningLocalRuntimeFailure.OutputTooLarge or
                LearningLocalRuntimeFailure.RuntimeDisposed =>
                new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed),
            _ => new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed),
        };

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new OperationCanceledException();
    }

    private static void CopyTokens(
        IReadOnlyList<LLamaToken> source,
        LLamaToken[] destination,
        ref int index)
    {
        for (var tokenIndex = 0; tokenIndex < source.Count; tokenIndex++)
            destination[index++] = source[tokenIndex];
    }

    private sealed record PreparedRequest(
        LearningTranslationRequest Request,
        IReadOnlyList<LLamaToken> PromptTokens,
        LearningModelSamplingValues Sampling,
        ILearningLocalOperation Operation);

    private sealed class CallbackExceptionCapture
    {
        private readonly object _gate = new();
        private ExceptionDispatchInfo? _exception;

        public void Capture(Exception exception)
        {
            lock (_gate) _exception ??= ExceptionDispatchInfo.Capture(exception);
        }

        public void ThrowIfCaptured()
        {
            ExceptionDispatchInfo? exception;
            lock (_gate) exception = _exception;
            exception?.Throw();
        }
    }
}
