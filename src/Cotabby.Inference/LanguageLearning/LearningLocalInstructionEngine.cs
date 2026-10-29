using System.Runtime.ExceptionServices;
using System.Text;
using Cotabby.Core.LanguageLearning;
using LLama.Native;

namespace Cotabby.Inference.LanguageLearning.Local;

/// <summary>One bounded instruction request borrowing the explanation owner's existing runtime.</summary>
public sealed class LearningLocalInstructionEngine : IAsyncDisposable
{
    private readonly LearningLocalModelConfiguration _configuration;
    private readonly ILearningLocalRuntime _runtime;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private Task<string>? _work;
    private Task? _disposal;
    private ILearningLocalOperation? _operation;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public LearningLocalInstructionEngine(LearningLocalModelConfiguration configuration, ILearningLocalRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = new(configuration.ModelPath, configuration.RequestedTemplate, configuration.ContextWindowTokens,
            configuration.SamplingOverrides, configuration.GpuLayerCount, requiresGeneralInstructions: true);
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public Task<string> GenerateAsync(string prompt, int output, int maximumBytes, CancellationToken token, Func<bool>? admission = null)
        => GenerateInternal(prompt, output, maximumBytes, null, token, admission);

    public Task<string> GenerateLookupAsync(string prompt, int output, int maximumBytes, bool hasContext, CancellationToken token, Func<bool>? admission = null)
        => GenerateInternal(prompt, output, maximumBytes, hasContext, token, admission);

    private Task<string> GenerateInternal(string prompt, int output, int maximumBytes, bool? lookupContext, CancellationToken token, Func<bool>? admission)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (output <= 0 || maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(output));
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_disposal is not null) throw new OperationCanceledException();
            if (_work is not null) throw new InvalidOperationException("An instruction facade owns one request.");
            return _work = Task.Run(() => GenerateCoreAsync(prompt, output, maximumBytes, lookupContext, token, admission));
        }
    }

    private async Task<string> GenerateCoreAsync(string prompt, int output, int maximumBytes, bool? lookupContext, CancellationToken token, Func<bool>? admission)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        void Check() { token.ThrowIfCancellationRequested(); _lifetime.Token.ThrowIfCancellationRequested(); linked.Token.ThrowIfCancellationRequested(); }
        ILearningLocalOperation? operation = null;
        ExceptionDispatchInfo? callbackFailure = null;
        try
        {
            Check();
            if (string.IsNullOrWhiteSpace(_configuration.ModelPath)) throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
            if (output >= _configuration.ContextWindowTokens) throw ModelContextBudget.OversizedTarget();
            if (!LearningExplanationModelPolicy.SupportsGeneralInstructions(Path.GetFileName(_configuration.ModelPath), _configuration.RequestedTemplate))
                throw new LearningExplanationException(LearningExplanationFailure.InstructionModelRequired);
            operation = await _runtime.BeginOperationAsync(_configuration, token, _lifetime.Token).ConfigureAwait(false);
            lock (_gate) _operation = operation;
            Check();
            var profile = LearningExplanationModelPolicy.ResolveLocalProfile(_configuration, operation.Metadata);
            var segments = LearningLocalPrompt.GetSegments(profile.Template, prompt);
            var prefix = operation.TokenizeTrusted(segments.TrustedPrefix); Check();
            var body = operation.TokenizeLiteral(segments.LiteralBody); Check();
            var suffix = operation.TokenizeTrusted(segments.TrustedSuffix); Check();
            var tokens = new LLamaToken[checked(prefix.Count + body.Count + suffix.Count)];
            int index = 0;
            foreach (var part in new[] { prefix, body, suffix }) foreach (var item in part) tokens[index++] = item;
            LearningLocalPrompt.ValidateTokenBudget(tokens.Length, output, profile.Template,
                Math.Min(_configuration.ContextWindowTokens, operation.ContextWindowTokens));
            int bytes = 0;
            var outputGate = new object();
            void CheckDelta(string delta)
            {
                try
                {
                    Check();
                    lock (outputGate)
                    {
                        int count = StrictUtf8.GetByteCount(delta);
                        if (count > maximumBytes - bytes) throw new LearningExplanationException(LearningExplanationFailure.InvalidContent);
                        bytes += count;
                    }
                }
                catch (Exception error) { Interlocked.CompareExchange(ref callbackFailure, ExceptionDispatchInfo.Capture(error), null); throw; }
            }
            if (admission?.Invoke() == false) throw new OperationCanceledException();
            var raw = lookupContext is { } context
                ? await operation.GenerateLookupAsync(tokens, profile.Sampling, output, CheckDelta, context, linked.Token, admission).ConfigureAwait(false)
                : await operation.GenerateAsync(tokens, profile.Sampling, output, CheckDelta, linked.Token, admission).ConfigureAwait(false);
            callbackFailure?.Throw(); Check();
            if (StrictUtf8.GetByteCount(raw) > maximumBytes) throw new LearningExplanationException(LearningExplanationFailure.InvalidContent);
            return raw;
        }
        catch (Exception error)
        {
            if (operation is not null)
            {
                try { await operation.DisposeAsync().ConfigureAwait(false); lock (_gate) if (ReferenceEquals(_operation, operation)) _operation = null; }
                catch { }
            }
            callbackFailure?.Throw();
            if (error is OperationCanceledException or LearningExplanationException or LearningTranslationException) throw;
            if (error is LearningLocalRuntimeException native)
                throw native.Failure switch
                {
                    LearningLocalRuntimeFailure.ModelUnavailable or LearningLocalRuntimeFailure.ModelLoadFailed => new LearningTranslationException(LearningTranslationFailure.LocalModelUnavailable),
                    LearningLocalRuntimeFailure.PromptBudgetExceeded => ModelContextBudget.OversizedTarget(),
                    LearningLocalRuntimeFailure.OutputLimitReached => new LearningTranslationException(LearningTranslationFailure.LocalOutputTruncated),
                    _ => new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed),
                };
            throw new LearningTranslationException(LearningTranslationFailure.LocalGenerationFailed);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            Task cancellation;
            try { cancellation = _lifetime.CancelAsync(); } catch { cancellation = Task.CompletedTask; }
            return new(_disposal = Task.Run(() => DrainAsync(_work, cancellation)));
        }
    }

    private async Task DrainAsync(Task? work, Task cancellation)
    {
        try
        {
            if (work is not null) try { await work.ConfigureAwait(false); } catch { }
            ILearningLocalOperation? operation; lock (_gate) operation = _operation;
            if (operation is not null) await operation.DisposeAsync().ConfigureAwait(false);
        }
        finally { try { await cancellation.ConfigureAwait(false); } catch { } _lifetime.Dispose(); }
    }
}
