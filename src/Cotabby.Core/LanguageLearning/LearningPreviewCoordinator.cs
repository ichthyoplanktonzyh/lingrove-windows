namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Owns one on-demand, readonly sentence translation preview for the lifetime of its app adapter.
/// This ports the translation task identity and cancellation responsibility of macOS
/// <c>LearningSentencePreviewCoordinator</c>; Windows capture/freshness and UI dispatch stay with
/// the app layer, and this Core type never reads or edits a live field.
/// </summary>
/// <remarks>
/// Each call freezes its source and settings into one request, cancels the prior task, and advances
/// an identity that every completion and streamed prefix must still match. A caller-provided opaque
/// scope can retain a bounded cache across hides/retries without mixing endpoint profiles. The
/// engine is caller-owned; this type owns preview state, request cancellation, and task lifetime.
/// </remarks>
public sealed class LearningPreviewCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan DisposeWaitLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinimumPartialPublishInterval = TimeSpan.FromSeconds(1d / 30d);
    private const int MaximumCachedTranslations = 64;
    private const string EngineUnavailableMessage = "The translation engine is not ready. Check the translation settings.";
    private const string UnexpectedFailureMessage = "Translation failed. Check the translation settings and try again.";

    private readonly object _gate = new();
    private readonly Dictionary<long, Task> _runningTasks = [];
    private readonly Queue<Notification> _notifications = [];
    private readonly Dictionary<CacheKey, string> _translationCache = [];
    private readonly TimeProvider _clock;

    private LearningPreviewState _state = LearningPreviewState.Idle;
    private CancellationTokenSource? _activeCancellation;
    private long _generation;
    private long _cacheEpoch;
    private long _publishedPartialSequence;
    private bool _disposed;
    private bool _drainingNotifications;

    /// <summary>
    /// Creates a preview owner with a monotonic clock for stream coalescing. Supplying a clock is
    /// useful for deterministic policy tests; production uses <see cref="TimeProvider.System"/>.
    /// </summary>
    public LearningPreviewCoordinator(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

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
    public Task PreviewAsync(
        string source,
        LearningTranslationOptions options,
        ILearningTranslationEngine engine,
        string? cacheScope = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var capturedOptions = Freeze(options);
        if (string.IsNullOrWhiteSpace(cacheScope)) cacheScope = null;
        var requestId = $"req_{Guid.NewGuid():N}";
        CancellationTokenSource cancellation;
        CancellationTokenSource? priorCancellation;
        Task task;
        bool drainNotifications;
        long generation;
        long cacheEpoch;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            cancellation = new CancellationTokenSource();
            priorCancellation = _activeCancellation;
            _activeCancellation = cancellation;
            generation = ++_generation;
            cacheEpoch = _cacheEpoch;
            _publishedPartialSequence = 0;
            _state = new LearningPreviewState
            {
                RequestId = requestId,
                Source = source,
                Result = null,
                PartialResult = null,
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
                () => RunAsync(generation, requestId, source, capturedOptions, engine, cacheScope, cacheEpoch, cancellation),
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
                PartialResult = null,
                Status = LearningPreviewStatus.Cancelled,
                ErrorMessage = null,
            };
            drainNotifications = EnqueueNotificationLocked(_state);
        }

        CancelBestEffort(cancellation);
        if (drainNotifications) DrainNotifications();
    }

    /// <summary>
    /// Drops every completed translation and invalidates cache writes from requests already running.
    /// Call this with <see cref="Cancel"/> when the selected provider, endpoint, model profile, or
    /// cache owner changes. Cache scope remains opaque and is never logged or shown in preview state.
    /// </summary>
    public void ClearCache()
    {
        lock (_gate)
        {
            if (_disposed) return;
            ++_cacheEpoch;
            _translationCache.Clear();
        }
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
                    PartialResult = null,
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
        string? cacheScope,
        long cacheEpoch,
        CancellationTokenSource cancellation)
    {
        // Retain the value token rather than reading CancellationTokenSource.Token from the stream
        // callback: a provider may retain that callback after this task has disposed its source.
        var token = cancellation.Token;
        try
        {
            if (!IsCurrent(generation, requestId, token)) return;

            if (engine is null)
            {
                PublishFailure(generation, requestId, token, EngineUnavailableMessage);
                return;
            }

            LearningTranslationRequest request;
            if (engine is ILearningTranslationRequestPlanner planner)
            {
                // Native learning models may resolve a profile/template asynchronously. This
                // await remains on the coordinator's worker task, and the identity check below
                // prevents a slow profile load from starting generation for a retired capture.
                var plannedRequest = await planner.PlanAsync(requestId, source, options, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!IsCurrent(generation, requestId, token)) return;
                request = ValidatePlannedRequest(requestId, source, options, plannedRequest);
            }
            else
            {
                request = LearningTranslationRequestFactory.Create(requestId, source, options);
            }

            token.ThrowIfCancellationRequested();
            if (!IsCurrent(generation, requestId, token)) return;

            var cacheKey = cacheScope is null
                ? (CacheKey?)null
                : new CacheKey(cacheScope, request.Source, request.Options);
            if (cacheKey is { } key && TryGetCached(generation, requestId, token, cacheEpoch, key, out var cached))
            {
                token.ThrowIfCancellationRequested();
                var validatedCached = LearningExpressionOutputPolicy.ValidatedText(cached);
                PublishReady(generation, requestId, token, validatedCached, cacheKey: null, cacheEpoch: cacheEpoch);
                return;
            }

            // A cache miss and a cache hit both follow request planning. Recheck here so a request
            // invalidated while its key is being compared never reaches inference.
            if (!IsCurrent(generation, requestId, token)) return;

            var stream = new LearningExpressionOutputPolicy.Stream();
            var streamGate = new object();
            Exception? streamFailure = null;
            var streamClosed = false;
            var partialSequence = 0L;
            var lastPublishedAt = 0L;
            var hasPublishedPartial = false;

            void ReceiveDelta(string delta)
            {
                if (!IsCurrent(generation, requestId, token)) return;

                string? prefix;
                long sequence;
                bool publish;
                lock (streamGate)
                {
                    // Once a bad delta has exceeded the byte cap or failed safety validation, a
                    // provider may swallow the callback exception and try again. Keep the request
                    // failed and its last safe prefix frozen until the final path reports that error.
                    if (streamClosed || streamFailure is not null || !IsCurrent(generation, requestId, token)) return;
                    try
                    {
                        if (delta is null)
                            throw new LearningTranslationException(LearningTranslationFailure.InvalidOutput);
                        prefix = stream.Append(delta);
                    }
                    catch (LearningTranslationException error)
                    {
                        streamFailure ??= error;
                        throw;
                    }

                    if (prefix is null) return;
                    sequence = ++partialSequence;
                    var now = _clock.GetTimestamp();
                    // Match macOS: show the first safe nonblank prefix immediately, then coalesce
                    // later updates to at most 30 publications per second using monotonic time.
                    publish = !hasPublishedPartial ||
                        _clock.GetElapsedTime(lastPublishedAt, now) >= MinimumPartialPublishInterval;
                    if (publish)
                    {
                        hasPublishedPartial = true;
                        lastPublishedAt = now;
                    }
                }

                if (publish) PublishPartial(generation, requestId, token, sequence, prefix);
            }

            var raw = await engine.TranslateAsync(request, ReceiveDelta, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            lock (streamGate)
            {
                streamClosed = true;
                if (streamFailure is not null) throw streamFailure;
            }

            // The shared macOS output contract rejects rather than truncates oversized or unsafe
            // text, so a backend cannot grow a preview without bound or smuggle control characters.
            var result = LearningExpressionOutputPolicy.ValidatedText(raw);
            PublishReady(generation, requestId, token, result, cacheKey, cacheEpoch);
        }
        catch (OperationCanceledException)
        {
            // Cancel/Dispose already published their terminal state. An engine-originated cancel
            // only becomes visible if this request is still current.
            PublishCancelled(generation, requestId, token);
        }
        catch (LearningTranslationException error)
        {
            PublishFailure(generation, requestId, token, error.Message);
        }
        catch (Exception)
        {
            // Provider exception strings can contain source text, URLs or credentials; only a
            // fixed message crosses the Core-to-UI boundary.
            PublishFailure(generation, requestId, token, UnexpectedFailureMessage);
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

    private bool TryGetCached(
        long generation,
        string requestId,
        CancellationToken token,
        long cacheEpoch,
        CacheKey key,
        out string translation)
    {
        lock (_gate)
        {
            if (!IsCurrentLocked(generation, requestId, token) || cacheEpoch != _cacheEpoch)
            {
                translation = string.Empty;
                return false;
            }

            if (_translationCache.TryGetValue(key, out var cached))
            {
                translation = cached;
                return true;
            }

            translation = string.Empty;
            return false;
        }
    }

    private static LearningTranslationRequest ValidatePlannedRequest(
        string requestId,
        string source,
        LearningTranslationOptions options,
        LearningTranslationRequest? request)
    {
        if (request is null)
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        if (!string.Equals(request.RequestId, requestId, StringComparison.Ordinal))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidRequestId);
        if (!string.Equals(request.Source, source, StringComparison.Ordinal))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidSourceRange);
        if (!EqualityComparer<LearningTranslationOptions>.Default.Equals(request.Options, options) ||
            request.Prompt is null)
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        }

        return request;
    }

    private void PublishPartial(
        long generation,
        string requestId,
        CancellationToken token,
        long sequence,
        string partial)
    {
        bool drainNotifications;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation, requestId, token) || sequence <= _publishedPartialSequence) return;
            _publishedPartialSequence = sequence;
            _state = _state with
            {
                Result = null,
                PartialResult = partial,
                ErrorMessage = null,
            };
            drainNotifications = EnqueueNotificationLocked(_state);
        }

        if (drainNotifications) DrainNotifications();
    }

    private void PublishReady(
        long generation,
        string requestId,
        CancellationToken token,
        string result,
        CacheKey? cacheKey,
        long cacheEpoch)
    {
        bool drainNotifications;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation, requestId, token)) return;
            if (cacheKey is { } key && cacheEpoch == _cacheEpoch)
            {
                // Match the macOS cache's bounded flush-all policy: no result is preferred by recency.
                if (_translationCache.Count >= MaximumCachedTranslations) _translationCache.Clear();
                _translationCache[key] = result;
            }

            _state = _state with
            {
                Result = result,
                PartialResult = null,
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
                PartialResult = null,
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
                PartialResult = null,
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

    private readonly record struct CacheKey(
        string Scope,
        string Source,
        LearningTranslationOptions Options);
}
