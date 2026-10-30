using System.Windows.Threading;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;
using VocabularyEntry = Cotabby.Core.Vocabulary.Frozen.VocabularyEntry;

namespace Cotabby.App.Vocabulary;

/// <summary>Window-local navigation over acknowledged canonical snapshots. The borrowed Store owns
/// its graph and lifetime; this session owns selection, filters, and bounded revision delivery.</summary>
internal sealed class FrozenVocabularyBrowserSession : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IVocabularyEntryImportStore _store;
    private readonly IVocabularyTextSearching _search;
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private FrozenVocabularyImportSnapshot? _pending;
    private bool _queued, _retired;
    private CancellationTokenSource _load = new();
    private readonly HashSet<Task> _loads = [];
    private long _revealGeneration;
    internal FrozenVocabularyBrowserSession(Dispatcher dispatcher, IVocabularyEntryImportStore store, IVocabularyTextSearching search)
    {
        _dispatcher = dispatcher; _store = store; _search = search; dispatcher.VerifyAccess();
        _subscription = store.Subscribe(QueueSnapshot);
    }
    internal FrozenVocabularyImportSnapshot? Snapshot { get; private set; }
    internal bool Loading { get; private set; } = true;
    internal string? Failure { get; private set; }
    internal bool Active { get; private set; } = true;
    internal string Query { get; set; } = "";
    internal LearningLanguage? Language { get; set; }
    internal string Tag { get; set; } = "";
    internal bool NewestFirst { get; set; } = true;
    internal bool Detailed { get; set; }
    internal bool MeaningMode { get; set; }
    internal string Category { get; set; } = "all";
    internal VocabularySelection? Selection { get; set; }
    internal Cotabby.App.UI.Vocabulary.VocabularyMeaningDestination? Destination { get; set; }
    internal IReadOnlyList<LearningLanguage> PreferredLanguages { get; set; } = [];
    internal event Action? Changed;
    internal IReadOnlyList<VocabularyEntry> Entries => Snapshot?.Document.CollectedEntries ?? [];
    internal IReadOnlyList<VocabularyEntry> Scoped
    {
        get
        {
            var values = Entries.Where(e => Language is null || e.Language == Language)
                .Select(e => e with { Senses = e.Senses.Where(s => Tag.Length == 0 || s.Tags.Contains(Tag)).ToArray() })
                .Where(e => e.Senses.Count != 0);
            return (NewestFirst ? values.OrderByDescending(e => e.UpdatedAt.ReferenceSeconds) : values.OrderBy(e => e.UpdatedAt.ReferenceSeconds)).ToArray();
        }
    }
    internal IReadOnlyList<VocabularyEntry> Visible => Scoped.Where(e => VocabularyFrozenEntrySearch.Matches(e, Query, _search)).ToArray();
    internal IReadOnlyList<VocabularyMeaningBrowserProjection.Item> MeaningItems
    {
        get
        {
            if (Snapshot is null) return [];
            var doc = Snapshot.Document;
            var items = VocabularyMeaningBrowserProjection.Items(doc.Semantics, Scoped, _search, StringComparer.CurrentCultureIgnoreCase,
                Query, includeEmpty: Language is null && Tag.Length == 0, preferredLanguages: PreferredLanguages);
            if (Category == "all") return items;
            var ids = Guid.TryParse(Category, out var id) ? R.VocabularyClassificationPolicy.MeaningIds(id, doc.Classification)
                : doc.Semantics.Nodes.Select(n => n.Id).Except(doc.Classification.Memberships.Where(m => m.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(m => m.NodeId)).ToHashSet();
            return items.Where(item => ids.Contains(item.Id)).ToArray();
        }
    }
    internal IReadOnlyList<VocabularySelection> Unclassified => Snapshot is null || Category != "all" ? []
        : VocabularyMeaningBrowserProjection.Unclassified(Snapshot.Document.Semantics, Visible);
    internal Task LoadAsync() => TrackLoad(LoadCoreAsync());
    private async Task LoadCoreAsync()
    {
        Verify(); try { var snapshot = await _store.ReadAsync(_load.Token); if (Active) Accept(snapshot); }
        catch (Exception error) { if (Active && error is not OperationCanceledException) { Loading = false; Failure = Cotabby.App.Localization.InterfaceFailureText.For(error); Notify(); } }
    }
    internal async Task RevealAsync(VocabularyInputResult result)
    {
        Verify(); if (!Active || result.Selection is not { } target) return;
        await RevealSelectionAsync(new(target.EntryId, target.SenseId));
    }
    internal async Task RevealSelectionAsync(VocabularySelection? selection)
    {
        Verify(); if (!Active || selection is not { } target) return;
        long generation = ++_revealGeneration;
        Query = ""; Language = null; Tag = ""; NewestFirst = true; MeaningMode = false; Category = "all"; Destination = null; Notify();
        await LoadAsync(); if (!Active || generation != _revealGeneration || Failure is not null) return;
        Selection = Entries.Any(e => e.Id == target.EntryId && e.Senses.Any(s => s.Id == target.SenseId)) ? target : null;
        Notify();
    }
    private async Task TrackLoad(Task operation)
    {
        _loads.Add(operation); try { await operation; } finally { _loads.Remove(operation); }
    }
    internal async Task RetireAsync()
    {
        Verify(); if (!Active) return; BeginRetire();
        while (_loads.Count != 0) { var operations = _loads.ToArray(); await Task.WhenAll(operations); _loads.RemoveWhere(t => t.IsCompleted); }
        _subscription.Dispose(); _load.Dispose();
    }
    internal void Refresh() { Verify(); Notify(); }
    private void QueueSnapshot(FrozenVocabularyImportSnapshot value)
    {
        lock (_gate)
        {
            if (_retired || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            if (_pending is null || value.Revision > _pending.Revision) _pending = value;
            if (_queued) return; _queued = true;
            try { _dispatcher.BeginInvoke(DispatcherPriority.DataBind, Drain); }
            catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) { _queued = false; _pending = null; }
        }
    }
    private void Drain()
    {
        FrozenVocabularyImportSnapshot? value;
        lock (_gate) { _queued = false; value = _retired ? null : _pending; _pending = null; }
        if (Active && value is not null) Accept(value);
    }
    internal void AcceptAcknowledgement(FrozenVocabularyImportSnapshot value) { Verify(); if (Active) Accept(value); }
    private void Accept(FrozenVocabularyImportSnapshot value)
    {
        if (Snapshot is { } previous && value.Revision < previous.Revision) return;
        Snapshot = value; Loading = false; Failure = null;
        if (Selection is { } selection && !Entries.Any(e => e.Id == selection.EntryId && e.Senses.Any(s => s.Id == selection.SenseId))) Selection = null;
        if (Destination is { NodeId: { } id } && !value.Document.Semantics.Nodes.Any(n => n.Id == id)) Destination = null;
        if (Destination is { CategoryId: { } categoryId } && (!value.Document.Classification.Categories.Any(c => c.Id == categoryId && !c.IsRetired) || value.Document.Classification.Overrides.Any(o => o.CategoryId == categoryId && o.IsHidden))) Destination = null;
        if (Destination is { SenseId: { } sense } && !Entries.Any(e => e.Senses.Any(s => s.Id == sense)))
            Destination = Destination.NodeId is { } node ? new(Cotabby.App.UI.Vocabulary.VocabularyMeaningDestinationKind.Node, node) : null;
        if (Guid.TryParse(Category, out var category) && (!value.Document.Classification.Categories.Any(c => c.Id == category && !c.IsRetired) || value.Document.Classification.Overrides.Any(o => o.CategoryId == category && o.IsHidden))) Category = "all";
        if (MeaningMode && Category != "all" && Destination?.NodeId is { } selectedNode && !MeaningItems.Any(item => item.Id == selectedNode)) Destination = null;
        Notify();
    }
    public void Dispose()
    {
        Verify(); if (!Active) return; BeginRetire(); _subscription.Dispose(); _load.Dispose();
    }
    private void BeginRetire()
    {
        Active = false; lock (_gate) { _retired = true; _pending = null; } _load.Cancel();
    }
    private void Verify() => _dispatcher.VerifyAccess();
    private void Notify() => Changed?.Invoke();
}
