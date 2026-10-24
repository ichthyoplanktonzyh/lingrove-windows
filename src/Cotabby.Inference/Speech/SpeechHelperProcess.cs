namespace Cotabby.Inference.Speech;

/// <summary>One local speech session owns this exact child and its binary pipes. Implementations
/// keep native startup/reads/termination off UI, discard diagnostic stderr, and never reuse an
/// autocomplete or translation process. Streams are effects, not a replacement framed protocol.</summary>
public interface ISpeechHelperProcess : IAsyncDisposable
{
    int Id { get; }
    bool HasExited { get; }
    Stream Input { get; }
    Stream Output { get; }

    /// <summary>Retire and reap this exact child through an uncancelled native barrier. False
    /// retains ownership so the runtime can retry instead of starting an overlapping model.
    /// Concurrent callers share retirement; caller generation cancellation cannot skip reaping.</summary>
    Task<bool> StopAndReapAsync();
}

/// <summary>The Windows composition root supplies one native process factory to its independent
/// speech runtime. A startup task owns any child it creates even if cancellation races with launch;
/// it must either return that owned process for retirement or reap it before throwing. Paths are
/// passed as individual arguments, never through a shell, and no model weights are downloaded.</summary>
public interface ISpeechHelperProcessFactory
{
    Task<ISpeechHelperProcess> StartAsync(string executable, SpeechLocalConfiguration configuration, CancellationToken cancellationToken);
}

/// <summary>A runtime subscribes lazily when local speech first runs, and releases this binding
/// at shutdown. The native source owns its wait handle/registration; notifications only request an
/// idle release and must never unload a helper while an utterance is active.</summary>
public interface ISpeechMemoryPressureSource
{
    IDisposable Observe(Action warning);
}
