using Cotabby.Core.LanguageLearning;
using LLama.Native;

namespace Cotabby.Inference.LanguageLearning.Local;

/// <summary>
/// Owns the learning task's resident LLamaSharp weights and context. It is intentionally separate
/// from the autocomplete manager: a local translation request must not borrow completion KV state,
/// prompt cache, or cancellation identity.
/// </summary>
public interface ILearningLocalRuntime : IAsyncDisposable
{
    /// <summary>
    /// Records retirement synchronously, then drains native work on a worker without closing the
    /// runtime. This call must not run cancellation callbacks or native disposal on the caller.
    /// A later request can open a new operation; an older queued unload cannot free its allocation.
    /// </summary>
    Task UnloadAsync();

    /// <summary>
    /// Opens an exclusive operation lease for one immutable model configuration. The lease stays
    /// active through prompt planning and generation, and must be disposed even on a cache hit.
    /// Caller and facade-owner tokens stay distinct so admission can inspect both direct flags
    /// even when asynchronous cancellation has not yet invoked linked-token callbacks.
    /// </summary>
    Task<ILearningLocalOperation> BeginOperationAsync(
        LearningLocalModelConfiguration configuration,
        CancellationToken ct,
        CancellationToken ownerCancellation = default);
}

/// <summary>
/// An exclusive view of the learning model. It exposes only bounded GGUF metadata and the three
/// operations the facade needs: trusted framing tokenization, literal payload tokenization, and a
/// complete EOG-terminated generation. Disposing the lease drains native work before the runtime
/// can unload its context or weights.
/// </summary>
public interface ILearningLocalOperation : IAsyncDisposable
{
    /// <summary>The small, bounded metadata projection used by Core's template policy.</summary>
    LearningGgufMetadata Metadata { get; }

    /// <summary>The context size reported by the loaded native model.</summary>
    int ContextWindowTokens { get; }

    /// <summary>Tokenize only trusted template framing, allowing model control-token spellings.</summary>
    IReadOnlyList<LLamaToken> TokenizeTrusted(string text);

    /// <summary>Tokenize user/source content literally; control-token spellings remain plain text.</summary>
    IReadOnlyList<LLamaToken> TokenizeLiteral(string text);

    /// <summary>
    /// Generate from an immutable, already-budgeted token plan using the frozen sampler profile.
    /// Returns only after the model emits an end-of-generation token. If the positive token limit
    /// is reached first, throws <see cref="LearningLocalRuntimeException"/> with
    /// <see cref="LearningLocalRuntimeFailure.OutputLimitReached"/> rather than presenting a
    /// truncated translation as complete.
    /// </summary>
    Task<string> GenerateAsync(
        IReadOnlyList<LLamaToken> promptTokens,
        LearningModelSamplingValues sampling,
        int maxTokens,
        Action<string>? onDelta,
        CancellationToken ct);

    /// <summary>Fixed lookup grammars are optional; implementations lacking support fail closed.</summary>
    Task<string> GenerateLookupAsync(IReadOnlyList<LLamaToken> promptTokens, LearningModelSamplingValues sampling,
        int maxTokens, Action<string>? onDelta, bool hasContext, CancellationToken ct) =>
        Task.FromException<string>(new NotSupportedException("Constrained lookup is unavailable."));
}
