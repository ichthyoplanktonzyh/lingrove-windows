using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Adapts Mac beginQuery/selectPage: explicit occurrence navigation starts one contextual query;
/// tabs load dictionary content once and never restart stopped explanation work. App supplies a
/// worker-built unit input. This boundary owns only transient target sessions, not models or UIA.
/// </summary>
public sealed partial class LearningDictionarySession
{
    private readonly ILearningExplanationGenerating? _explanationService;
    private LearningExplanationInput? _unitExplanationInput;
    private ContextualTarget? _contextual;
    private bool _dictionaryRequested;

    public Task SelectPageAsync(LearningDictionaryPage page, Guid? targetIdentity = null)
    {
        Task task = Task.CompletedTask; bool drain;
        lock (_gate)
        {
            if (_disposed || _state.Unit is null || !Enum.IsDefined(page)
                || targetIdentity is not null && targetIdentity != _state.TargetIdentity || _state.Page == page
                || page == LearningDictionaryPage.Contextual && !_state.HasContextualExplanation) return task;
            _state = _state with { Page = page };
            if (page == LearningDictionaryPage.Dictionary && !_dictionaryRequested)
                (task, drain) = RequestDictionaryLocked();
            else drain = EnqueueLocked();
        }
        if (drain) Drain();
        return task;
    }

    /// <summary>
    /// Actions carry both occurrence identity and the snapshot actually rendered. The inner
    /// session atomically checks that snapshot, closing completion/preferences races after this
    /// owner's lock is released. A retired target can never address the next target's session.
    /// </summary>
    public Task ExplainAsync(Guid targetIdentity, LearningExplanationState displayedState, bool refresh = false)
    {
        ContextualTarget? target;
        lock (_gate)
        {
            if (_disposed || _contextual is not { } current || current.Identity != targetIdentity
                || !ReferenceEquals(_state.Explanation, displayedState)) return Task.CompletedTask;
            target = current;
        }
        return target.Owner.Session?.ExplainAsync(refresh, displayedState) ?? Task.CompletedTask;
    }

    public void CancelExplanation(Guid targetIdentity, LearningExplanationState displayedState)
    {
        ContextualTarget? target;
        lock (_gate)
        {
            if (_disposed || _contextual is not { } current || current.Identity != targetIdentity
                || !ReferenceEquals(_state.Explanation, displayedState)) return;
            target = current;
        }
        target.Owner.Session?.Cancel(displayedState);
    }

    private TargetTransition BeginTargetLocked(bool resetExplanation = false)
    {
        RetireLocked(); _dictionaryRequested = false;
        var previous = _contextual;
        Guid identity = Guid.NewGuid();
        _contextual = null;
        // The current explanation input contract cannot represent unknown language yet.
        // Keep the real bundled dictionary available without inventing a language.
        bool hasExplanation = _explanationService is not null && (_state.ReferenceQuery is not null || _state.Language is not null || _unitExplanationInput is not null);
        _state = _state with { TargetIdentity = identity, RequestIdentity = Guid.NewGuid(),
            Status = LearningDictionaryContentStatus.Loading, Result = null, ErrorMessage = null,
            Page = hasExplanation ? LearningDictionaryPage.Contextual : LearningDictionaryPage.Dictionary,
            HasContextualExplanation = hasExplanation, ExplanationConfigured = false,
            ExplanationLanguage = null, Explanation = null };
        if (hasExplanation)
        {
            var input = _state.ReferenceQuery is { } reference
                ? new LearningExplanationInput(reference, reference, _state.Language,
                    _state.ReferenceIsExpression ? Cotabby.Core.Vocabulary.VocabularyEntryKind.Expression : Cotabby.Core.Vocabulary.VocabularyEntryKind.Word,
                    reference, [new Cotabby.Core.Vocabulary.VocabularyTextRange(0, reference.Length)], LearningExplanationContext.None)
                : _unitExplanationInput;
            if (_state.SelectedWord is { } word && _state.Unit is { } unit && input is not null)
                input = LearningExplanationInputFactory.ProjectComponent(word, unit, input);
            ContextualOwner scope;
            if (!resetExplanation && previous is not null && previous.Input == input)
                scope = previous.Owner; // Mac setTarget(equalInput) retains an answer or live work.
            else
            {
                scope = new(); // Inert: subscribing to the borrowed service happens after the lock.
            }
            var target = new ContextualTarget(identity, scope, input);
            _contextual = target;
            _state = _state with { Explanation = scope.Session?.State ?? new LearningExplanationState() };
        }
        if (previous is not null && !ReferenceEquals(previous.Owner, _contextual?.Owner))
            _ = ObserveRetirementAsync(previous.Owner.Lifetime.Retire());
        else previous = null;
        var (task, drain) = _state.Page == LearningDictionaryPage.Dictionary
            ? RequestDictionaryLocked() : (Task.CompletedTask, EnqueueLocked());
        return new(task, drain, previous, _contextual);
    }

    private Task ActivateTarget(TargetTransition transition)
    {
        // Do not leave notification ownership parked in a blocked foreign subscription/store.
        // Reentrant navigation can publish its new target before the old activation returns.
        if (transition.Drain) Drain();
        RetireContextualTarget(transition.Previous);
        if (transition.Current is { } current && ActivateOwner(current.Owner) is { } session)
        {
            // Mac's MainActor orders setTarget/explain. A target-owned session gives the same
            // isolation across Windows threads without calling saved-store/view callbacks under
            // the dictionary lock: a concurrent navigation disposes only this old session.
            session.SetTarget(current.Input);
            PublishExplanation(new(this), current.Owner, session.State);
            if (session.IsConfigured) _ = session.ExplainAsync();
        }
        return transition.Task;
    }

    private LearningExplanationSession? ActivateOwner(ContextualOwner scope)
    {
        if (scope.Lifetime.Token.IsCancellationRequested) return null;
        var session = scope.Session;
        if (session is null)
        {
            // Even an event add accessor is foreign code. Construct without either owner's lock;
            // concurrent/reentrant activation may create a candidate, but only one is retained.
            var candidate = new LearningExplanationSession(_explanationService, scope.Lifetime.Token);
            var owner = new WeakReference<LearningDictionarySession>(this);
            candidate.StateChanged += state => PublishExplanation(owner, scope, state);
            session = scope.Install(candidate);
            if (!ReferenceEquals(candidate, session)) _ = candidate.DisposeAsync();
        }
        if (!scope.Lifetime.Token.IsCancellationRequested) return session;
        _ = session.DisposeAsync(); // Covers retirement while construction/subscription was blocked.
        return null;
    }

    private static void PublishExplanation(WeakReference<LearningDictionarySession> owner,
        ContextualOwner target, LearningExplanationState state)
    {
        if (!owner.TryGetTarget(out var session)) return;
        if (target.Session is not { } explanation) return;
        bool configured = explanation.IsConfigured;
        var language = session._explanationService?.Language;
        bool drain;
        lock (session._gate)
        {
            if (session._disposed || !ReferenceEquals(session._contextual?.Owner, target)
                || !ReferenceEquals(explanation.State, state)) return;
            if (ReferenceEquals(session._state.Explanation, state) && session._state.ExplanationConfigured == configured
                && session._state.ExplanationLanguage == language) return;
            session._state = session._state with { Explanation = state, ExplanationConfigured = configured, ExplanationLanguage = language };
            drain = session.EnqueueLocked();
        }
        if (drain) session.Drain();
    }

    private static LearningExplanationInput? ValidUnitInput(LearningUnit unit, LearningLanguage language,
        LearningExplanationInput? input)
    {
        var kind = unit.Kind switch { LearningUnitKind.Word => VocabularyEntryKind.Word,
            LearningUnitKind.Expression => VocabularyEntryKind.Expression, _ => VocabularyEntryKind.Pattern };
        string target = unit.Kind == LearningUnitKind.Pattern ? unit.Query : unit.Surface;
        return Enum.IsDefined(unit.Kind) && input is not null && LearningExplanationPolicy.IsValid(input) && input.Language == language && input.Kind == kind
            && CanonicalStringComparer.Instance.Equals(input.Target, target)
            && CanonicalStringComparer.Instance.Equals(input.DictionaryForm, unit.Query) ? input : null;
    }

    private static void RetireContextualTarget(ContextualTarget? target)
    {
        // Session disposal cancels/unsubscribes immediately; the borrowed app service/runtime
        // retains its separate native drain responsibility. No foreign operation is joined here.
        if (target is not null)
        {
            if (target.Owner.Session is { } session) _ = session.DisposeAsync();
            _ = ObserveRetirementAsync(target.Owner.Lifetime.DisposeAsync().AsTask());
        }
    }

    /// <summary>Occurrence controls have a fresh identity even when equal input retains its session.</summary>
    private sealed record ContextualTarget(Guid Identity, ContextualOwner Owner, LearningExplanationInput? Input);
    /// <summary>Only changed input/open/close retires this lightweight session; model lifetime is borrowed.</summary>
    private sealed class ContextualOwner
    {
        private LearningExplanationSession? _session;
        public LearningRequestLifetime Lifetime { get; } = new();
        public LearningExplanationSession? Session => Volatile.Read(ref _session);
        public LearningExplanationSession Install(LearningExplanationSession candidate) =>
            Interlocked.CompareExchange(ref _session, candidate, null) ?? candidate;
    }
    private sealed record TargetTransition(Task Task, bool Drain, ContextualTarget? Previous, ContextualTarget? Current);
}
