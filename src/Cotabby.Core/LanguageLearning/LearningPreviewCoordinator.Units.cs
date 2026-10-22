namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Ports LearningSentencePreviewCoordinator.startUnits: completed translations acquire lexical
/// annotations through a separate task/revision/cache, without borrowing the translation engine
/// lease. The shared coordinator owns these fields; App still owns exact editor freshness.
/// </summary>
public sealed partial class LearningPreviewCoordinator
{
    private const int MaximumCachedUnits = 64;
    private readonly ILearningUnitRecognizing? _unitRecognizer;
    private readonly Dictionary<UnitCacheKey, IReadOnlyList<LearningUnit>> _unitCache = [];
    private readonly Dictionary<long, Task> _runningUnitTasks = [];
    private LearningRequestLifetime? _unitLifetime;
    private long _unitRevision;

    // Caller holds the shared gate. Enqueuing Pending before releasing that gate means even a
    // synchronous recognizer cannot publish Ready ahead of the completed translation snapshot.
    private void StartUnitsLocked(long generation, string requestId, string translation, long epoch)
    {
        RetireUnitWorkLocked();
        if (_unitRecognizer is null)
        {
            _state = _state with { UnitStage = LearningUnitStage.Ready };
            return;
        }
        var key = new UnitCacheKey(_state.Options!.TargetLanguage, translation);
        if (_unitCache.TryGetValue(key, out var units))
        {
            _state = _state with { Units = units, UnitStage = LearningUnitStage.Ready };
            return;
        }
        long revision = _unitRevision;
        var lifetime = new LearningRequestLifetime();
        _unitLifetime = lifetime;
        _runningUnitTasks.Add(revision, Task.Run(() => RunUnitsAsync(
            generation, requestId, revision, epoch, key, lifetime), CancellationToken.None));
    }

    private async Task RunUnitsAsync(long generation, string requestId, long revision, long epoch,
        UnitCacheKey key, LearningRequestLifetime lifetime)
    {
        try
        {
            if (!Current()) return;
            var recognized = await _unitRecognizer!.RecognizeAsync(key.Translation, key.Language, lifetime.Token).ConfigureAwait(false);
            var units = Array.AsReadOnly(recognized.ToArray());
            bool drain;
            lock (_gate)
            {
                if (!CurrentLocked()) return;
                if (epoch == _cacheEpoch)
                {
                    if (_unitCache.Count >= MaximumCachedUnits) _unitCache.Clear();
                    _unitCache[key] = units;
                }
                _state = _state with { Units = units, UnitStage = LearningUnitStage.Ready };
                drain = EnqueueNotificationLocked(_state);
            }
            if (drain) DrainNotifications();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            bool drain;
            lock (_gate)
            {
                if (!CurrentLocked()) return;
                // No provider exception text or resource error replaces a valid translation.
                _state = _state with { Units = Array.Empty<LearningUnit>(), UnitStage = LearningUnitStage.Failed };
                drain = EnqueueNotificationLocked(_state);
            }
            if (drain) DrainNotifications();
        }
        finally
        {
            try { await lifetime.DisposeAsync().ConfigureAwait(false); }
            catch { /* Consumer cancellation callbacks cannot fault the UI task owner. */ }
            lock (_gate)
            {
                _runningUnitTasks.Remove(revision);
                if (ReferenceEquals(_unitLifetime, lifetime)) _unitLifetime = null;
            }
        }

        bool Current() { lock (_gate) return CurrentLocked(); }
        bool CurrentLocked() => !_disposed && !lifetime.Token.IsCancellationRequested
            && _generation == generation && _unitRevision == revision && _state.RequestId == requestId
            && _state.Status == LearningPreviewStatus.Ready
            && string.Equals(_state.Result, key.Translation, StringComparison.Ordinal)
            && _state.Options?.TargetLanguage == key.Language;
    }

    private void RetireUnitWorkLocked()
    {
        ++_unitRevision;
        if (_unitLifetime is { } lifetime) _ = ObserveUnitRetirementAsync(lifetime.Retire());
        _unitLifetime = null;
    }
    private static async Task ObserveUnitRetirementAsync(Task retirement)
    {
        try { await retirement.ConfigureAwait(false); }
        catch { /* Retirement already revoked the direct flag; callback failures stay private. */ }
    }
    private readonly record struct UnitCacheKey(LearningLanguage Language, string Translation);
}
