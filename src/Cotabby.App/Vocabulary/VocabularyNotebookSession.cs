using System.Collections.ObjectModel;
using System.Windows.Threading;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>
/// A notebook window owns this UI-actor lifetime, adapting Mac VocabularyNotebookSession. Each
/// saved occurrence borrows one lightweight explanation session from the app's shared service;
/// selection only restores saved results. The separate dictionary receives no explainer, so an
/// explicit dictionary lookup cannot start AI. Dispatcher access replaces MainActor ownership.
/// Native word preparation runs on a worker with weak ownership and stale-result cancellation.
/// </summary>
internal sealed class VocabularyNotebookSession : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ILearningLexicalText _text;
    private readonly ILearningExplanationGenerating? _service;
    private IReadOnlyDictionary<string, LearningExplanationSession> _explanations = Empty();
    private VocabularyEntry? _dictionaryEntry, _pendingDictionary;
    private CancellationTokenSource? _preparation;
    private long _selectionVersion, _dictionaryVersion;
    private bool _disposed;
    internal Guid? EntryId { get; private set; }
    internal Guid? SenseId { get; private set; }
    internal IReadOnlyDictionary<string, LearningExplanationSession> Explanations { get { VerifyAccess(); return _explanations; } }
    internal LearningDictionarySession Dictionary { get; }
    internal string? DictionaryError { get; private set; }
    internal bool IsConfigured => _service?.IsConfigured == true;
    internal Task LastDictionaryOperation { get; private set; } = Task.CompletedTask;
    internal event Action? Changed;

    internal VocabularyNotebookSession(Dispatcher dispatcher, ILearningDictionaryProviding provider, ILearningLexicalText text,
        ILearningExplanationGenerating? service = null)
    {
        dispatcher.VerifyAccess(); _dispatcher = dispatcher; _text = text; _service = service;
        Dictionary = new LearningDictionarySession(provider); // Intentionally no explanation service.
    }
    private static IReadOnlyDictionary<string, LearningExplanationSession> Empty() =>
        new ReadOnlyDictionary<string, LearningExplanationSession>(new Dictionary<string, LearningExplanationSession>());
    private void VerifyAccess() => _dispatcher.VerifyAccess();

    internal void Select(VocabularyEntry? entry, Guid? senseId = null)
    {
        VerifyAccess(); if (_disposed) return;
        var sense = VocabularyNotebookTargetPolicy.SelectedSense(entry, senseId);
        long version = ++_selectionVersion; var previous = _explanations; var created = new List<LearningExplanationSession>();
        bool newOwner = EntryId != entry?.Id || SenseId != sense?.Id;
        var plans = new List<(string Key, LearningExplanationInput Input, IReadOnlyList<LearningExplanationRecord> Records)>();
        if (entry is not null && sense is not null)
        {
            IEnumerable<VocabularyExample?> examples = (sense.IsSavedWithoutContext ? new VocabularyExample?[] { null } : []).Concat(sense.Examples);
            foreach (var example in examples) plans.Add((VocabularyNotebookTargetPolicy.Key(example),
                LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, example, sense)), example?.Explanations ?? sense.Explanations));
        }
        // Revoke all affected children before a constructor, saved lookup or view callback can
        // let a worker finish. Equal effective inputs retain their current generation/record.
        var effectiveInputs = new Dictionary<string, LearningExplanationInput?>();
        foreach (var plan in plans) effectiveInputs[plan.Key] = LearningExplanationPolicy.IsValid(plan.Input) ? plan.Input : null;
        foreach (var pair in previous)
        {
            if (newOwner || !effectiveInputs.TryGetValue(pair.Key, out var input) || pair.Value.State.Input != input) pair.Value.RevokeWork();
        }
        bool closeDictionary = EntryId != entry?.Id || _dictionaryEntry is { } remembered && entry is not null
            && VocabularyNotebookTargetPolicy.DictionaryChanged(remembered, entry);
        // Mac computes words synchronously. During Windows preparation an empty form can make
        // an edited expression change the effective query without changing form/language. Retire
        // only that unfinished preparation; an already-open dictionary keeps Mac's source rule.
        closeDictionary |= _pendingDictionary is { } pending && entry is not null && pending.Id == entry.Id
            && !VocabularyNotebookTargetPolicy.SameQuery(VocabularyNotebookTargetPolicy.DictionaryQuery(pending), VocabularyNotebookTargetPolicy.DictionaryQuery(entry));
        EntryId = entry?.Id; SenseId = sense?.Id;
        if (newOwner) _explanations = Empty();
        try
        {
            if (closeDictionary)
            {
                _dictionaryEntry = null; CancelPreparation(); DictionaryError = null; Dictionary.Close();
                if (version != _selectionVersion || _disposed) return;
            }
            var next = new Dictionary<string, LearningExplanationSession>();
            if (entry is not null && sense is not null)
            {
                foreach (var plan in plans)
                {
                    string key = plan.Key;
                    if (newOwner || !previous.TryGetValue(key, out var session))
                    { session = new LearningExplanationSession(_service); created.Add(session); }
                    if (version != _selectionVersion || _disposed) return;
                    var saved = plan.Records.LastOrDefault(record => record.Input == plan.Input && record.Language == _service?.Language);
                    session.SetTarget(plan.Input, saved);
                    if (version != _selectionVersion || _disposed) return;
                    next[key] = session;
                }
            }
            _explanations = new ReadOnlyDictionary<string, LearningExplanationSession>(next); Notify();
        }
        finally
        {
            // C# service events strongly retain subscribers. Retiring a removed row therefore
            // also unsubscribes it; Mac's ARC/weak Combine subscription releases that child.
            foreach (var session in previous.Values.Concat(created).Distinct())
                if (!_explanations.Values.Any(current => ReferenceEquals(current, session))) _ = session.DisposeAsync();
        }
    }
    internal bool IsCurrent(string key, LearningExplanationSession session)
    { VerifyAccess(); return !_disposed && _explanations.TryGetValue(key, out var current) && ReferenceEquals(current, session); }
    internal void ActivateExplanation(string key, LearningExplanationSession? expected = null)
    {
        VerifyAccess(); if (_disposed || expected is not null && !IsCurrent(key, expected)) return;
        long version = _selectionVersion;
        foreach (var pair in _explanations) if (pair.Key != key) pair.Value.RevokeWork();
        foreach (var pair in _explanations)
        {
            if (version != _selectionVersion || _disposed) return;
            if (pair.Key != key) pair.Value.Cancel();
        }
    }
    internal Task ShowDictionaryAsync(VocabularyEntry entry)
    {
        VerifyAccess(); if (_disposed || EntryId != entry.Id) return Task.CompletedTask;
        string query = VocabularyNotebookTargetPolicy.DictionaryQuery(entry); var state = Dictionary.State;
        if (state.DocumentId == VocabularyNotebookTargetPolicy.DocumentId(entry) && VocabularyNotebookTargetPolicy.SameQuery(state.Query, query)) return LastDictionaryOperation;
        if (!LastDictionaryOperation.IsCompleted && _pendingDictionary is { } pending && pending.Id == entry.Id && pending.Language == entry.Language
            && VocabularyNotebookTargetPolicy.SameQuery(VocabularyNotebookTargetPolicy.DictionaryQuery(pending), query)) return LastDictionaryOperation;
        CancelPreparation(); _dictionaryEntry = entry; _pendingDictionary = entry; DictionaryError = null;
        long version = _dictionaryVersion; var lifetime = new CancellationTokenSource(); var token = lifetime.Token; _preparation = lifetime;
        var work = PrepareDictionaryAsync(new(this), _dispatcher, _text, entry, version, lifetime); LastDictionaryOperation = work;
        // Native segmentation is synchronous on Mac. Windows prepares it asynchronously, so
        // clear the previous article during that interval instead of showing it for a new query.
        Dictionary.Close();
        if (CurrentDictionary(version, entry, token)) Notify();
        return work;
    }
    private static async Task PrepareDictionaryAsync(WeakReference<VocabularyNotebookSession> owner, Dispatcher dispatcher,
        ILearningLexicalText text, VocabularyEntry entry, long version, CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        try
        {
            string query = VocabularyNotebookTargetPolicy.DictionaryQuery(entry);
            var words = await Task.Run(() => text.Words(query, entry.Language), token).ConfigureAwait(false);
            var unit = VocabularyNotebookTargetPolicy.DictionaryUnit(entry, words);
            if (token.IsCancellationRequested || dispatcher.HasShutdownStarted) return;
            var lookup = await dispatcher.InvokeAsync(() =>
            {
                if (!owner.TryGetTarget(out var session) || !session.CurrentDictionary(version, entry, token)) return Task.CompletedTask;
                session._pendingDictionary = null; session.DictionaryError = null;
                var lookup = session.Dictionary.OpenAsync(unit, VocabularyNotebookTargetPolicy.DocumentId(entry), entry.Language);
                if (session.CurrentDictionary(version, entry, token)) session.Notify();
                return lookup;
            }, DispatcherPriority.DataBind, token).Task.ConfigureAwait(false);
            await lookup.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (token.IsCancellationRequested || dispatcher.HasShutdownStarted) return;
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (!owner.TryGetTarget(out var session) || !session.CurrentDictionary(version, entry, token)) return;
                    session._pendingDictionary = null; session.DictionaryError = "Lexical text processing is unavailable on this system."; session.Notify();
                }, DispatcherPriority.DataBind, token).Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (dispatcher.HasShutdownStarted) { }
        }
        finally { lifetime.Dispose(); }
    }
    private bool CurrentDictionary(long version, VocabularyEntry entry, CancellationToken token) => !_disposed && !token.IsCancellationRequested
        && _dictionaryVersion == version && EntryId == entry.Id;
    internal void HideDictionary()
    { VerifyAccess(); if (_disposed) return; _dictionaryEntry = null; CancelPreparation(); DictionaryError = null; Dictionary.Close(); Notify(); }
    internal Task Resume()
    { VerifyAccess(); return !_disposed && _dictionaryEntry is { } entry ? ShowDictionaryAsync(entry) : Task.CompletedTask; }
    internal void Cancel()
    {
        VerifyAccess(); if (_disposed) return;
        long version = ++_selectionVersion;
        foreach (var session in _explanations.Values) session.RevokeWork();
        foreach (var session in _explanations.Values)
        { if (version != _selectionVersion || _disposed) return; session.Cancel(); }
        if (version != _selectionVersion || _disposed) return;
        CancelPreparation(); DictionaryError = null; Dictionary.Close(); Notify();
    }
    private void CancelPreparation()
    {
        ++_dictionaryVersion; _pendingDictionary = null; var work = _preparation; _preparation = null;
        try { work?.Cancel(); } catch (ObjectDisposedException) { /* The completed worker already released its source. */ }
    }
    private void Notify() { if (Changed is { } handlers) foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch { } }
    public ValueTask DisposeAsync()
    {
        VerifyAccess(); if (_disposed) return ValueTask.CompletedTask;
        _disposed = true; ++_selectionVersion; EntryId = SenseId = null; _dictionaryEntry = null; CancelPreparation();
        foreach (var session in _explanations.Values) session.RevokeWork();
        foreach (var session in _explanations.Values) _ = session.DisposeAsync(); _explanations = Empty(); Changed = null;
        // Word preparation owns no native runtime. Weak workers release their own resources;
        // the dictionary owns and boundedly retires its provider tasks, as in other cards.
        return Dictionary.DisposeAsync();
    }
}
