using System.Runtime.ExceptionServices;
using System.Text;
using Cotabby.Core.LanguageLearning;
using LLama.Native;

namespace Cotabby.Inference.LanguageLearning.Local;

/// <summary>
/// One explicit local explanation adapted from Mac LearningExplanationService/ModelController.
/// The future service owns this one-use facade; the app owns an explanation-only runtime. Work
/// runs off the UI thread with one frozen configuration, metadata gate, exact token plan and output
/// limit. Disposal retires work, drains its lease, and leaves the reusable runtime alive.
/// </summary>
public sealed class LearningLocalExplanationEngine : ILearningExplanationEngine
{
    private readonly object _gate = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly LearningLocalModelConfiguration _configuration;
    private readonly ILearningLocalRuntime _runtime;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _ownerToken;
    private ILearningLocalOperation? _operation;
    private Task<LearningExplanationResult>? _work;
    private Task? _disposeTask;
    private bool _disposed;

    public LearningLocalExplanationEngine(LearningLocalModelConfiguration configuration, ILearningLocalRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _configuration = new(configuration.ModelPath, configuration.RequestedTemplate, configuration.ContextWindowTokens,
            configuration.SamplingOverrides, configuration.GpuLayerCount, requiresGeneralInstructions: true);
        _ownerToken = _lifetime.Token;
    }

    public Task<LearningExplanationResult> GenerateAsync(LearningExplanationInput input, LearningLanguage responseLanguage,
        int maximumOutputTokens, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_disposed) throw new OperationCanceledException();
            if (_work is not null) throw new InvalidOperationException("An explanation facade owns one request.");
            // Even an already-resident runtime can complete admission synchronously. Task.Run
            // keeps prompt construction/tokenization and metadata policy off the caller's UI thread.
            return _work = Task.Run(() => GenerateCoreAsync(input, responseLanguage, maximumOutputTokens, ct));
        }
    }

    private async Task<LearningExplanationResult> GenerateCoreAsync(LearningExplanationInput input,
        LearningLanguage responseLanguage, int output, CancellationToken ct)
    {
        ILearningLocalOperation? operation = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _ownerToken);
        void CheckCancellation()
        {
            ct.ThrowIfCancellationRequested(); _ownerToken.ThrowIfCancellationRequested(); linked.Token.ThrowIfCancellationRequested();
        }
        var callbackFailure = new CallbackFailure();
        try
        {
            CheckCancellation();
            // The Mac service validates captured material before configuration errors. Preserve
            // that precedence when both a stale/edited capture and an unavailable model exist.
            if (input is null || !LearningExplanationPolicy.IsValid(input))
                throw new LearningExplanationException(LearningExplanationFailure.InvalidContent);
            if (string.IsNullOrWhiteSpace(_configuration.ModelPath))
                throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
            if (!Enum.IsDefined(responseLanguage)) throw new LearningExplanationException(LearningExplanationFailure.InvalidContent);
            output = LearningExplanationModelPolicy.ResolveOutputTokens(output);
            if (output >= _configuration.ContextWindowTokens)
                throw new LearningExplanationException(LearningExplanationFailure.ContextBudgetExceeded);
            if (!LearningExplanationModelPolicy.SupportsGeneralInstructions(Path.GetFileName(_configuration.ModelPath), _configuration.RequestedTemplate))
                throw new LearningExplanationException(LearningExplanationFailure.InstructionModelRequired);
            string prompt = LearningExplanationPolicy.Prompt(input, responseLanguage, _configuration.ContextWindowTokens, output);
            CheckCancellation();
            operation = await _runtime.BeginOperationAsync(_configuration, ct, _ownerToken).ConfigureAwait(false);
            lock (_gate) _operation = operation;
            CheckCancellation();
            // Repeat the gate using this operation's actual metadata. A UI precheck cannot
            // authorize an allocation admitted later, nor can a manual delimiter bypass it.
            var profile = LearningExplanationModelPolicy.ResolveLocalProfile(_configuration, operation.Metadata);
            var segments = LearningLocalPrompt.GetSegments(profile.Template, prompt);
            var prefix = operation.TokenizeTrusted(segments.TrustedPrefix); CheckCancellation();
            var body = operation.TokenizeLiteral(segments.LiteralBody); CheckCancellation();
            var suffix = operation.TokenizeTrusted(segments.TrustedSuffix); CheckCancellation();
            var tokens = new LLamaToken[checked(prefix.Count + body.Count + suffix.Count)];
            int index = 0;
            foreach (var part in new[] { prefix, body, suffix }) foreach (var token in part) tokens[index++] = token;
            LearningLocalPrompt.ValidateTokenBudget(tokens.Length, output, profile.Template,
                Math.Min(_configuration.ContextWindowTokens, operation.ContextWindowTokens));
            int bytes = 0;
            var outputGate = new object();
            void ValidateDelta(string delta)
            {
                try
                {
                    CheckCancellation();
                    lock (outputGate)
                    {
                        if (!Fits(delta, 24_000 - bytes)) throw new LearningExplanationException(LearningExplanationFailure.InvalidContent);
                        bytes += Encoding.UTF8.GetByteCount(delta);
                    }
                }
                catch (Exception error) { callbackFailure.Capture(error); throw; }
            }
            string raw = await operation.GenerateAsync(tokens, profile.Sampling, output, ValidateDelta, linked.Token).ConfigureAwait(false);
            callbackFailure.ThrowIfCaptured();
            CheckCancellation();
            // EOG completion belongs to the runtime; complete bounded JSON belongs to policy.
            // A runtime returning no deltas still cannot bypass the raw response's 24k cap.
            var result = LearningExplanationPolicy.Parse(raw);
            CheckCancellation();
            return result;
        }
        catch (Exception error)
        {
            await ReleaseAfterFailureAsync(operation).ConfigureAwait(false);
            callbackFailure.ThrowIfCaptured();
            if (error is OperationCanceledException or LearningExplanationException or LearningTranslationException) throw;
            throw MapFailure(error);
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        Task? work;
        lock (_gate)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            _disposed = true; work = _work;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _disposeTask = completion.Task;
        }
        // CancelAsync retires the flag now and runs foreign/native callbacks off the UI thread.
        // Admission and output checks also inspect the direct owner flag before callbacks run.
        Task cancellation;
        try { cancellation = _lifetime.CancelAsync(); }
        catch (Exception) { cancellation = Task.CompletedTask; }
        _ = Task.Run(() => CompleteDisposalAsync(work, cancellation, completion));
        return new(completion.Task);
    }

    private async Task CompleteDisposalAsync(Task? work, Task cancellation, TaskCompletionSource completion)
    {
        try
        {
            if (work is not null) { try { await work.ConfigureAwait(false); } catch (Exception) { } }
            ILearningLocalOperation? operation;
            lock (_gate) operation = _operation;
            if (operation is not null) await operation.DisposeAsync().ConfigureAwait(false);
            lock (_gate) _operation = null;
            // Foreign cancellation callbacks may wait for the lease to drain. The direct owner
            // flag already retires native work, so release that lease before joining callbacks.
            try { await cancellation.ConfigureAwait(false); } catch (Exception) { }
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            try { await cancellation.ConfigureAwait(false); } catch (Exception) { }
            completion.TrySetException(MapFailure(error));
        }
        finally { _lifetime.Dispose(); }
    }

    private async Task ReleaseAfterFailureAsync(ILearningLocalOperation? operation)
    {
        if (operation is null) return;
        try
        {
            await operation.DisposeAsync().ConfigureAwait(false);
            lock (_gate) if (ReferenceEquals(_operation, operation)) _operation = null;
        }
        catch (Exception) { /* Keep the lease so facade disposal retries its drain. Preserve the original failure. */ }
    }

    private static bool Fits(string text, int bytes)
    {
        if (text is null) return false;
        try { return StrictUtf8.GetByteCount(text) <= bytes; }
        catch (EncoderFallbackException) { return false; }
    }

    private static Exception MapFailure(Exception error) => error is LearningLocalRuntimeException native ? native.Failure switch
    {
        LearningLocalRuntimeFailure.ModelUnavailable or LearningLocalRuntimeFailure.ModelLoadFailed =>
            new LearningTranslationException(LearningTranslationFailure.LocalModelUnavailable),
        LearningLocalRuntimeFailure.PromptBudgetExceeded => ModelContextBudget.OversizedTarget(),
        LearningLocalRuntimeFailure.OutputLimitReached => new LearningTranslationException(LearningTranslationFailure.LocalOutputTruncated),
        _ => new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed),
    } : new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed);

    private sealed class CallbackFailure
    {
        private ExceptionDispatchInfo? _first;
        public void Capture(Exception error) => Interlocked.CompareExchange(ref _first, ExceptionDispatchInfo.Capture(error), null);
        public void ThrowIfCaptured() => Volatile.Read(ref _first)?.Throw();
    }
}
