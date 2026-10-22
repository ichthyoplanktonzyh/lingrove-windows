namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Owns one on-demand, readonly sentence translation preview for the lifetime of its app adapter.
/// This ports the translation task identity and cancellation responsibility of macOS
/// <c>LearningSentencePreviewCoordinator</c>; Windows capture/freshness and UI dispatch stay with
/// the app layer, and this Core type never reads or edits a live field.
/// </summary>
/// <remarks>
/// Each call freezes its source and settings into one request, cancels the prior task, and advances
/// an identity that every completion must still match. The engine is owned by the caller and owns
/// its own inference runtime; this coordinator owns only the request token, latest state, and bounded
/// set of tasks it must observe during disposal.
/// </remarks>
public sealed class LearningPreviewCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan DisposeWaitLimit = TimeSpan.FromSeconds(5);
    private const string EngineUnavailableMessage = "The translation engine is not ready. Check the translation settings.";
    private const string UnexpectedFailureMessage = "Translation failed. Check the translation settings and try again.";

    private readonly object _gate = new();
    private readonly Dictionary<long, Task> _runningTasks = [];
    private readonly Queue<Notification> _notifications = [];

    private LearningPreviewState _state = LearningPreviewState.Idle;
    private CancellationTokenSource? _activeCancellation;
    private long _generation;
    private bool _disposed;
    private bool _drainingNotifications;

    /// <summary>Raised in state-transition order, outside the coordinator lock.</summary>
    /// <remarks>
    /// Engine completion may raise this event on a worker thread. A UI adapter must marshal the
    /// immutable snapshot to its dispatcher before touching controls.
    /// </remarks>
    public event Action<LearningPreviewState>? StateChanged;

    /// <summary>Returns the latest immutable preview snapshot.</summary>
    public LearningPreviewState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    /// <summary>
    /// Starts a complete translation for an already-captured sentence. A later call supersedes this
    /// one even if its engine ignores cancellation; only the latest request may publish a result.
    /// </summary>
    public Task PreviewAsync(string source, LearningTranslationOptions options, ILearningTranslationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var capturedOptions = Freeze(options);
        var requestId = $"req_{Guid.NewGuid():N}";
        CancellationTokenSource cancellation;
        CancellationTokenSource? priorCancellation;
        Task task;
        bool drainNotifications;
        long generation;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            cancellation = new CancellationTokenSource();
            priorCancellation = _activeCancellation;
            _activeCancellation = cancellation;
            generation = ++_generation;
            _state = new LearningPreviewState
            {
                RequestId = requestId,
                Source = source,
                Result = null,
                Status = LearningPreviewStatus.Loading,
                ErrorMessage = null,
                Options = capturedOptions,
            };
            drainNotifications = EnqueueNotificationLocked(_state);

            // Task.Run deliberately clears a caller's SynchronizationContext. PreviewAsync is often
            // invoked by WPF, and prompt construction belongs with engine work on the pool rather
            // than resuming through the UI dispatcher. CancellationToken.None ensures a queued
            // delegate still runs its finally block and disposes its per-request source.
            task = Task.Run(
                () => RunAsync(generation, requestId, source, capturedOptions, engine, cancellation),
                CancellationToken.None);
            _runningTasks.Add(generation, task);
        }

        CancelBestEffort(priorCancellation);
        if (drainNotifications) DrainNotifications();
        return task;
    }

    /// <summary>Invalidates the current preview and asks its engine to stop.</summary>
    /// <remarks>
    /// Cancel also clears a completed result because an app adapter uses it when the captured
    /// source or translation settings change. A subsequent explicit preview starts a fresh request.
    /// </remarks>
    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        bool drainNotifications;

        lock (_gate)
        {
            if (_disposed || _state.Status == LearningPreviewStatus.Idle) return;

            cancellation = _activeCancellation;
            _activeCancellation = null;
            ++_generation;
            _state = _state with
            {
                Result = null,
                Status = LearningPreviewStatus.Cancelled,
                ErrorMessage = null,
            };
            drainNotifications = EnqueueNotificationLocked(_state);
        }

        CancelBestEffort(cancellation);
        if (drainNotifications) DrainNotifications();
    }

    /// <summary>
    /// Cancels current work and waits briefly for engines that honor cancellation. A provider that
    /// ignores cancellation may finish later, but its generation check prevents any state publish.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cancellation;
        Task[] tasks;
        bool drainNotifications = false;

        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            cancellation = _activeCancellation;
            _activeCancellation = null;
            ++_generation;

            if (_state.Status != LearningPreviewStatus.Idle && _state.Status != LearningPreviewStatus.Cancelled)
            {
                _state = _state with
                {
                    Result = null,
                    Status = LearningPreviewStatus.Cancelled,
                    ErrorMessage = null,
                };
                drainNotifications = EnqueueNotificationLocked(_state);
            }

            tasks = _runningTasks.Values.ToArray();
        }

        CancelBestEffort(cancellation);
        if (drainNotifications) DrainNotifications();

        if (tasks.Length == 0) return;
        var all = Task.WhenAll(tasks);
        var finished = await Task.WhenAny(all, Task.Delay(DisposeWaitLimit)).ConfigureAwait(false);
        if (ReferenceEquals(finished, all))
        {
            try { await all.ConfigureAwait(false); }
            catch { /* RunAsync normally contains provider failures; disposal must still complete. */ }
        }
    }

    private async Task RunAsync(
        long generation,
        string requestId,
        string source,
        LearningTranslationOptions options,
        ILearningTranslationEngine? engine,
        CancellationTokenSource cancellation)
    {
        try
        {
            if (!IsCurrent(generation, requestId, cancellation.Token)) return;

            if (engine is null)
            {
                PublishFailure(generation, requestId, cancellation.Token, EngineUnavailableMessage);
                return;
            }

            var request = LearningTranslationRequestFactory.Create(requestId, source, options);
            var raw = await engine.TranslateAsync(request, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();

            // The shared macOS output contract rejects rather than truncates oversized or unsafe
            // text, so a backend cannot grow a preview without bound or smuggle control characters.
            var result = LearningExpressionOutputPolicy.ValidatedText(raw);
            PublishReady(generation, requestId, cancellation.Token, result);
        }
        catch (OperationCanceledException)
        {
            // Cancel/Dispose already published their terminal state. An engine-originated cancel
            // only becomes visible if this request is still current.
            PublishCancelled(generation, requestId, cancellation.Token);
        }
        catch (LearningTranslationException error)
        {
            PublishFailure(generation, requestId, cancellation.Token, error.Message);
        }
        catch (Exception)
        {
            // Provider exception strings can contain source text, URLs or credentials; only a
            // fixed message crosses the Core-to-UI boundary.
            PublishFailure(generation, requestId, cancellation.Token, UnexpectedFailureMessage);
        }
        finally
        {
            bool drainNotifications;
            lock (_gate)
            {
                if (_generation == generation && ReferenceEquals(_activeCancellation, cancellation))
                {
                    _activeCancellation = null;
                }

                _runningTasks.Remove(generation);
                drainNotifications = _notifications.Count > 0 && !_drainingNotifications;
                if (drainNotifications) _drainingNotifications = true;
            }

            cancellation.Dispose();
            if (drainNotifications) DrainNotifications();
        }
    }

    private void PublishReady(long generation, string requestId, CancellationToken token, string result)
    {
        bool drainNotifications;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation, requestId, token)) return;
            _state = _state with
            {
                Result = result,
                Status = LearningPreviewStatus.Ready,
                ErrorMessage = null,
            };
            _activeCancellation = null;
            drainNotifications = EnqueueNotificationLocked(_state);
        }

        if (drainNotifications) DrainNotifications();
    }

    private void PublishFailure(long generation, string requestId, CancellationToken token, string message)
    {
        bool drainNotifications;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation, requestId, token)) return;
            _state = _state with
            {
                Result = null,
                Status = LearningPreviewStatus.Failed,
                ErrorMessage = message,
            };
            _activeCancellation = null;
            drainNotifications = EnqueueNotificationLocked(_state);
        }

        if (drainNotifications) DrainNotifications();
    }

    private void PublishCancelled(long generation, string requestId, CancellationToken token)
    {
        bool drainNotifications;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation, requestId, token)) return;
            _state = _state with
            {
                Result = null,
                Status = LearningPreviewStatus.Cancelled,
                ErrorMessage = null,
            };
            _activeCancellation = null;
            drainNotifications = EnqueueNotificationLocked(_state);
        }

        if (drainNotifications) DrainNotifications();
    }

    private bool IsCurrent(long generation, string requestId, CancellationToken token)
    {
        lock (_gate) return IsCurrentLocked(generation, requestId, token);
    }

    private bool IsCurrentLocked(long generation, string requestId, CancellationToken token) =>
        !_disposed && !token.IsCancellationRequested && _generation == generation &&
        _state.RequestId == requestId && _state.Status == LearningPreviewStatus.Loading;

    private bool EnqueueNotificationLocked(LearningPreviewState state)
    {
        _notifications.Enqueue(new Notification(state, StateChanged));
        if (_drainingNotifications) return false;
        _drainingNotifications = true;
        return true;
    }

    /// <summary>Serializes notifications without holding the state lock while user callbacks run.</summary>
    private void DrainNotifications()
    {
        while (true)
        {
            Notification notification;
            lock (_gate)
            {
                if (_notifications.Count == 0)
                {
                    _drainingNotifications = false;
                    return;
                }

                notification = _notifications.Dequeue();
            }

            if (notification.Handlers is null) continue;
            foreach (Action<LearningPreviewState> handler in notification.Handlers.GetInvocationList())
            {
                try { handler(notification.State); }
                catch { /* A presentation callback cannot corrupt Core task lifetime or expose text. */ }
            }
        }
    }

    private static LearningTranslationOptions Freeze(LearningTranslationOptions options) =>
        options with
        {
            // Match the request factory's effective style so the state describes the exact settings
            // used by the prompt; both options and style are immutable records after this copy.
            Style = options.Style is null ? null! : options.Style.Effective,
        };

    private static void CancelBestEffort(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception)
        {
            // Cancellation callbacks belong to provider code. A callback fault cannot undo the
            // generation change that already made its result stale.
        }
    }

    private sealed record Notification(
        LearningPreviewState State,
        Action<LearningPreviewState>? Handlers);
}
