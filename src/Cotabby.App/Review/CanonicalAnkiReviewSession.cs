using System.Collections.ObjectModel;
using System.ComponentModel;
using Cotabby.Core.Review;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Review;

/// <summary>A window-local draft borrows the canonical Store and the existing review controller.
/// The owner must admit/drain operations through AnkiReviewFeature; this object owns no client.</summary>
internal sealed partial class CanonicalAnkiReviewSession : INotifyPropertyChanged, IDisposable
{
    internal AnkiReviewController Controller { get; }
    internal F.ICanonicalVocabularyStore Store { get; }
    internal Guid EntryId { get; }
    internal Guid SenseId { get; }
    internal Guid? CardId { get; }
    private F.FrozenVocabularyImportSnapshot _snapshot;
    private readonly object _snapshotGate = new();
    private readonly SynchronizationContext? _context;
    private IDisposable? _subscription;
    private bool _retired;
    private F.VocabularyReview? _baseline;
    private ReviewPlan _plan;
    private Guid? _bindingId;
    private string _deck;
    private readonly Dictionary<string, bool> _keepRemote = new(CanonicalStringComparer.Instance);
    private AnkiRemoteNote? _conflictRemote;
    private ReviewOperationScope? _sendScope;
    private SendState? _sendState;
    private Guid _metadataId = Guid.NewGuid();
    private readonly string _initialDefaultDeck;
    private readonly bool _initialDefaultRecognition, _initialDefaultGap;
    private IReadOnlyDictionary<string, VocabularyPreparedImage> _images = new ReadOnlyDictionary<string, VocabularyPreparedImage>(new Dictionary<string, VocabularyPreparedImage>());
    internal IReadOnlyDictionary<string, VocabularyPreparedImage> ImportedImages { get => _images; set { _images = new ReadOnlyDictionary<string, VocabularyPreparedImage>(new Dictionary<string, VocabularyPreparedImage>(value, StringComparer.Ordinal)); Changed(); } }
    internal ReviewPlan Plan { get => _plan; set { if (value.Layout is { } layout && (value.AudioKinds is null || !value.AudioKinds.SetEquals(layout.AudioKinds))) value = value with { AudioKinds = layout.AudioKinds }; _plan = value; Changed(); } }
    internal string Deck { get => _deck; set { _deck = value; Changed(); } }
    internal Guid? BindingId => _bindingId;
    internal F.FrozenVocabularyImportSnapshot Snapshot { get { lock (_snapshotGate) return _snapshot; } }
    internal F.AnkiNoteBinding? Binding => _baseline?.Bindings.FirstOrDefault(value => value.Id == _bindingId);
    internal string Model => Binding?.Model ?? (CardId is null ? Controller.Settings.Model : ReviewLayoutPolicy.Model);
    internal bool Busy { get; private set; }
    internal bool Loading { get; private set; }
    internal bool MissingNote { get; private set; }
    internal string? Message { get; private set; }
    internal IReadOnlyList<AnkiFieldConflict> Conflicts { get; private set; } = [];
    internal IReadOnlyList<string> AvailableFields { get; private set; } = [];
    internal AnkiReviewCatalog? Catalog { get; private set; }
    internal bool SafeGapFront { get; private set; }
    internal bool CurrentPresetTemplate { get; private set; }
    internal bool LegacyPresetTemplate { get; private set; }
    internal string PresetCss { get; private set; } = AnkiNoteTemplate.Css;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() { lock (_snapshotGate) { if (_retired) return; } PropertyChanged?.Invoke(this, new(null)); }
    internal void SetKeepRemote(string field, bool value) { _keepRemote[field] = value; Changed(); }
    internal void SelectBinding(Guid? id) { _bindingId = id; if (Binding is { } value) Deck = value.Deck; if (!Same(Model, AnkiReviewPolicy.PresetName)) Plan = Plan with { Recognition = true }; Conflicts = []; _keepRemote.Clear(); _conflictRemote = null; Changed(); }
    private CanonicalAnkiReviewSession(AnkiReviewController controller, F.ICanonicalVocabularyStore store, F.FrozenVocabularyImportSnapshot snapshot, F.VocabularyEntry entry, F.VocabularySense sense, Guid? cardId)
    {
        Controller = controller; Store = store; _snapshot = snapshot; _context = SynchronizationContext.Current; EntryId = entry.Id; SenseId = sense.Id; CardId = cardId;
        _initialDefaultDeck = controller.Settings.Deck; _initialDefaultRecognition = controller.Settings.DefaultRecognition; _initialDefaultGap = controller.Settings.DefaultGap;
        _baseline = ReviewValue(sense, cardId); var initial = _baseline?.Plan ?? CanonicalReviewCardPolicy.InitialPlan(sense);
        if (cardId is { } id && _baseline is null) initial = new ReviewPlan(id) { Name = "New card", Layout = ReviewLayoutPolicy.Preset(0), ExampleId = sense.Examples.FirstOrDefault()?.Id };
        if (_baseline is null && cardId is null) { var example = sense.Examples.FirstOrDefault(); bool gap = _initialDefaultGap && example is not null && ReviewCardPolicy.Gap(example.Text, example.Ranges, entry.Kind) is not null; initial = initial with { Recognition = _initialDefaultRecognition || !gap, Gap = gap }; }
        _plan = initial; _bindingId = _baseline?.ActiveBindingId; _deck = _baseline?.ActiveBinding?.Deck ?? initial.Deck ?? _initialDefaultDeck;
        if (!Same(Model, AnkiReviewPolicy.PresetName)) Plan = _plan with { Recognition = true };
    }
    internal static async Task<CanonicalAnkiReviewSession> OpenAsync(AnkiReviewController controller, F.ICanonicalVocabularyStore store, Guid entryId, Guid senseId, Guid? cardId = null, CancellationToken token = default)
    {
        var gate = new object(); F.FrozenVocabularyImportSnapshot? queued = null; CanonicalAnkiReviewSession? owner = null;
        var subscription = store.Subscribe(snapshot =>
        {
            lock (gate) { if (owner is null) { if (queued is null || snapshot.Revision > queued.Revision) queued = snapshot; } else owner.QueueSnapshot(snapshot); }
        });
        try
        {
            var snapshot = await store.ReadAsync(token);
            lock (gate)
            {
                if (queued is not null && queued.Revision > snapshot.Revision) snapshot = queued;
                var current = Locate(snapshot, entryId, senseId);
                owner = new(controller, store, snapshot, current.Entry, current.Sense, cardId) { _subscription = subscription };
                return owner;
            }
        }
        catch { subscription.Dispose(); throw; }
    }
    private void QueueSnapshot(F.FrozenVocabularyImportSnapshot snapshot)
    {
        if (_context is { } context) context.Post(_ => ApplySnapshot(snapshot, true), null);
        else ApplySnapshot(snapshot, true);
    }
    private void ApplySnapshot(F.FrozenVocabularyImportSnapshot snapshot, bool notification = false)
    {
        bool changed;
        lock (_snapshotGate) { changed = !_retired && snapshot.Revision > _snapshot.Revision; if (changed) _snapshot = snapshot; }
        if (changed && notification) Changed();
    }
    public void Dispose()
    { lock (_snapshotGate) _retired = true; Interlocked.Exchange(ref _subscription, null)?.Dispose(); }
    private static (F.VocabularyEntry Entry, F.VocabularySense Sense) Locate(F.FrozenVocabularyImportSnapshot snapshot, Guid entryId, Guid senseId)
    {
        var entry = snapshot.Document.CollectedEntries.FirstOrDefault(value => value.Id == entryId);
        var sense = entry?.Senses.FirstOrDefault(value => value.Id == senseId);
        return entry is not null && sense is not null ? (entry, sense) : throw Conflict();
    }
    private async Task<(F.VocabularyEntry Entry, F.VocabularySense Sense)> CurrentAsync(CancellationToken token)
    { var current = await Store.ReadAsync(token); ApplySnapshot(current); lock (_snapshotGate) return Locate(_retired ? current : _snapshot, EntryId, SenseId); }
    private static F.VocabularyReview? ReviewValue(F.VocabularySense sense, Guid? id) => id is null ? sense.Review : sense.ReviewCards?.FirstOrDefault(value => value.Plan.Id == id);
    private F.VocabularyReview? ReviewAfterReceipt(F.FrozenVocabularyImportSnapshot snapshot)
    {
        var entry = snapshot.Document.CollectedEntries.FirstOrDefault(value => value.Id == EntryId) ?? throw Conflict();
        var sense = entry.Senses.FirstOrDefault(value => value.Id == SenseId || value.MergedFrom.Contains(SenseId)) ?? throw Conflict();
        return ReviewValue(sense, CardId);
    }
    private static bool RemovesExistingCard(ReviewPlan plan, F.AnkiNoteBinding binding)
    {
        if (binding.NoteId is null) return false;
        string Field(ReviewField field) => binding.Fields.GetValueOrDefault(binding.Mapping.GetValueOrDefault(field) ?? "") ?? "";
        return !plan.Recognition && Field(ReviewField.Recognition).Length != 0 || !plan.Gap && Field(ReviewField.GapPrompt).Length != 0;
    }
    private void RequireNoAudio()
    {
        if ((Plan.AudioKinds?.Count ?? 0) != 0 || (Plan.Layout?.AudioKinds.Count ?? 0) != 0)
            throw new AnkiReviewException("Speech is not available for review audio.");
    }
    private sealed record SendState(ReviewPlan Plan, string Deck, Guid? BindingId, string Model, IReadOnlyDictionary<ReviewField, string> Mapping);
    private SendState CaptureSendState() => new(Plan, Deck, BindingId, Model, Mapping);
    private bool MatchesSendState(SendState state) => Plan == state.Plan && Same(Deck, state.Deck) && BindingId == state.BindingId
        && Same(Model, state.Model) && Mapping.Count == state.Mapping.Count
        && Mapping.All(pair => state.Mapping.TryGetValue(pair.Key, out var value) && Same(pair.Value, value));
    private void RequireSendState()
    { RequireNoAudio(); if (_sendState is not { } state || !MatchesSendState(state)) throw Conflict(); }
    internal IReadOnlyDictionary<ReviewField, string> Mapping => CardId is not null ? ReviewLayoutPolicy.Mapping : Same(Model, AnkiReviewPolicy.PresetName)
        ? new ReadOnlyDictionary<ReviewField, string>(AnkiReviewPolicy.PresetMapping.Where(pair => pair.Key is not (ReviewField.ExpressionAudio or ReviewField.SentenceAudio) || AvailableFields.Count == 0 || Has(AvailableFields, FieldName(pair.Key))).ToDictionary(pair => pair.Key, pair => pair.Value))
        : Binding?.Mapping ?? Controller.Settings.EffectiveMapping;
    internal async Task<ReviewCardContent> ContentAsync(CancellationToken token = default) { var current = await CurrentAsync(token); return CanonicalReviewCardPolicy.Content(current.Entry, current.Sense, Plan); }
    internal async Task<bool> CanGapAsync(CancellationToken token = default)
    {
        var current = await CurrentAsync(token); var example = current.Sense.Examples.FirstOrDefault(value => value.Id == Plan.ExampleId);
        return example is not null && ReviewCardPolicy.Gap(example.Text, example.Ranges, current.Entry.Kind) is not null;
    }
    internal async Task<string> AnswerAsync(CancellationToken token = default)
    {
        if (Plan.Answer is { } answer) return answer;
        try { var current = await CurrentAsync(token); return CanonicalReviewCardPolicy.Content(current.Entry, current.Sense, Plan with { Recognition = true, Gap = false }).Answer; }
        catch (OperationCanceledException) { throw; }
        catch { return ""; }
    }
    internal bool HasExistingCard(ReviewField field) => Binding is { NoteId: not null } binding
        && binding.Mapping.TryGetValue(field, out string? name) && (binding.Fields.GetValueOrDefault(name) ?? "").Length > 0;
    internal async Task<string?> ValidationMessageAsync(CancellationToken token = default)
    {
        try
        {
            RequireNoAudio(); var current = await CurrentAsync(token);
            if (Binding is { } binding && RemovesExistingCard(Plan, binding)) return "Manage existing card types in Anki.";
            if (_baseline?.NeedsBindingChoice == true && BindingId is null || Deck.Length == 0
                || Binding?.NoteId is null && Catalog is { } catalog && !Has(catalog.Decks, Deck)) return "Choose a deck and an Anki association.";
            var content = CanonicalReviewCardPolicy.Content(current.Entry, current.Sense, Plan);
            if (Same(Model, AnkiReviewPolicy.PresetName) && Plan.Gap && content.SenseContent.Count > 0 && !SafeGapFront && !Loading)
                return "Update the Lingrove template before adding explanation content to gap cards.";
            var fields = AnkiReviewPolicy.Fields(content, Binding?.OriginSenseId ?? CardId ?? SenseId, Mapping, Plan.FieldOverrides);
            return AvailableFields.Count > 0 && fields.Keys.Any(field => !Has(AvailableFields, field)) ? "The Anki note type does not contain the mapped fields." : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return error.Message; }
    }
    internal void RestorePlan(ReviewPlan saved)
    {
        var restored = saved with { Id = Guid.NewGuid() };
        if (!Same(Model, AnkiReviewPolicy.PresetName)) restored = restored with { Recognition = true };
        Plan = restored; if (Binding?.NoteId is null && saved.Deck is { } deck) Deck = deck;
    }
    internal async Task<bool> SaveAsync(CancellationToken token = default)
    {
        if (Busy) return false;
        Busy = true; Changed();
        try { await SavePlanAsync(token); return true; }
        catch (Exception error) { Message = error is OperationCanceledException ? null : error.Message; Changed(); return false; }
        finally { Busy = false; Changed(); }
    }
    private async Task SavePlanAsync(CancellationToken token)
    {
        var captured = CaptureSendState(); var images = ImportedImages;
        var current = await CurrentAsync(token); if (ReviewValue(current.Sense, CardId) != _baseline) throw Conflict();
        if (_sendState is not null) RequireSendState();
        var plan = captured.Plan;
        if (CardId is not null && (plan.Name is null || FoundationText.TrimWhitespaceAndNewlines(plan.Name).Length == 0)) plan = plan with { Name = "New card" };
        plan = plan with { Deck = captured.Deck };
        if (plan.Layout is { } layout && (plan.AudioKinds is null || !plan.AudioKinds.SetEquals(layout.AudioKinds))) plan = plan with { AudioKinds = layout.AudioKinds };
        if (MatchesSendState(captured))
        {
            Plan = plan;
            if (_sendState is not null) { _sendState = captured with { Plan = plan }; RequireSendState(); }
        }
        var next = (_baseline ?? new F.VocabularyReview(plan)) with { Plan = plan, ActiveBindingId = captured.BindingId };
        int index = next.Bindings.ToList().FindIndex(value => value.Id == captured.BindingId && value.NoteId is null);
        if (index >= 0) { var bindings = next.Bindings.ToArray(); bindings[index] = bindings[index] with { Deck = captured.Deck }; next = next with { Bindings = bindings }; }
        var ack = await Store.SaveReviewAsync(EntryId, SenseId, next, _baseline, CardId, images, token);
        ApplySnapshot(ack); _baseline = next;
        var remaining = new Dictionary<string, VocabularyPreparedImage>(ImportedImages, StringComparer.Ordinal);
        foreach (var pair in images) if (remaining.TryGetValue(pair.Key, out var value) && ReferenceEquals(value, pair.Value)) remaining.Remove(pair.Key);
        ImportedImages = remaining;
        if (_sendState is not null) RequireSendState();
    }
    private static bool Same(string? a, string? b) => CanonicalStringComparer.Instance.Equals(a, b);
    private static bool Has(IEnumerable<string> values, string value) => values.Contains(value, CanonicalStringComparer.Instance);
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
    private static string FieldName(ReviewField field) => System.Text.Json.JsonSerializer.SerializeToElement(field).GetString()!;
    private static IReadOnlyDictionary<string, AnkiValue> Params(params (string Key, AnkiValue Value)[] values) => new ReadOnlyDictionary<string, AnkiValue>(values.ToDictionary(value => value.Key, value => value.Value, CanonicalStringComparer.Instance));
}
