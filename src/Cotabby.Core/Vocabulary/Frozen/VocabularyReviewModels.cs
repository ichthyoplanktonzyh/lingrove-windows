using Cotabby.Core.Review;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>
/// One sense-owned review relationship: current editable plan, acknowledged remote bindings and
/// retained plans. This never contains a connection API key or starts a server request. Ambiguous
/// bindings require a choice; array order cannot silently choose another note after merging.
/// </summary>
public sealed record VocabularyReview
{
    private IReadOnlyList<AnkiNoteBinding> _bindings = Array.Empty<AnkiNoteBinding>();
    private IReadOnlyList<ReviewPlan> _preservedPlans = Array.Empty<ReviewPlan>();
    public VocabularyReview(ReviewPlan plan, IReadOnlyList<AnkiNoteBinding>? bindings = null,
        Guid? activeBindingId = null, IReadOnlyList<ReviewPlan>? preservedPlans = null)
    { Plan = plan; Bindings = bindings ?? []; ActiveBindingId = activeBindingId; PreservedPlans = preservedPlans ?? []; }
    public ReviewPlan Plan { get; init; }
    public IReadOnlyList<AnkiNoteBinding> Bindings { get => _bindings; init => _bindings = CollectionValues.List(value); }
    public Guid? ActiveBindingId { get; init; }
    public IReadOnlyList<ReviewPlan> PreservedPlans { get => _preservedPlans; init => _preservedPlans = CollectionValues.List(value); }
    public AnkiNoteBinding? ActiveBinding => Bindings.FirstOrDefault(binding => binding.Id == ActiveBindingId);
    public bool NeedsBindingChoice => Bindings.Count != 0 && ActiveBinding is null;
    public bool Equals(VocabularyReview? other) => other is not null && Plan == other.Plan && ActiveBindingId == other.ActiveBindingId
        && CollectionValues.SequenceEqual(Bindings, other.Bindings) && CollectionValues.SequenceEqual(PreservedPlans, other.PreservedPlans);
    public override int GetHashCode() => HashCode.Combine(Plan, ActiveBindingId, CollectionValues.SequenceHash(Bindings), CollectionValues.SequenceHash(PreservedPlans));
}

/// <summary>
/// A durable remote receipt retains original sense identity after a merge. Endpoint/profile scope
/// a note; NoteId and acknowledged fields outlive navigation. Immutable identity fields cannot be
/// rewritten by a record clone; only deck/fields/sync time and binding ID are editable as on Mac.
/// Storing this value authorizes no network operation and carries no API key.
/// </summary>
public sealed record AnkiNoteBinding
{
    private readonly IReadOnlyDictionary<ReviewField, string> _mapping = CollectionValues.Map(Array.Empty<KeyValuePair<ReviewField, string>>());
    private IReadOnlyDictionary<string, string> _fields = CollectionValues.Map(Array.Empty<KeyValuePair<string, string>>());
    public AnkiNoteBinding(Guid id, Guid originSenseId, string endpoint, string profile, long? noteId, string model,
        string deck, string identityField, IReadOnlyDictionary<ReviewField, string> mapping,
        IReadOnlyDictionary<string, string> fields, SourceDate? syncedAt = null)
    {
        Id = id; OriginSenseId = originSenseId; Endpoint = endpoint; Profile = profile; NoteId = noteId; Model = model;
        Deck = deck; IdentityField = identityField; _mapping = CollectionValues.Map(mapping); Fields = fields; SyncedAt = syncedAt;
    }
    public Guid Id { get; init; }
    public Guid OriginSenseId { get; }
    public string Endpoint { get; }
    public string Profile { get; }
    public long? NoteId { get; }
    public string Model { get; }
    public string Deck { get; init; }
    public string IdentityField { get; }
    public IReadOnlyDictionary<ReviewField, string> Mapping => _mapping;
    public IReadOnlyDictionary<string, string> Fields { get => _fields; init => _fields = CollectionValues.Map(value); }
    public SourceDate? SyncedAt { get; init; }
    public static AnkiNoteBinding Create(Guid originSenseId, string endpoint, string profile, long? noteId, string model,
        string deck, string identityField, IReadOnlyDictionary<ReviewField, string> mapping, IReadOnlyDictionary<string, string> fields) =>
        new(Guid.NewGuid(), originSenseId, endpoint, profile, noteId, model, deck, identityField, mapping, fields);
    public bool Equals(AnkiNoteBinding? other) => other is not null && Id == other.Id && OriginSenseId == other.OriginSenseId
        && CollectionValues.Same(Endpoint, other.Endpoint) && CollectionValues.Same(Profile, other.Profile) && NoteId == other.NoteId
        && CollectionValues.Same(Model, other.Model) && CollectionValues.Same(Deck, other.Deck)
        && CollectionValues.Same(IdentityField, other.IdentityField) && CollectionValues.MapEqual(Mapping, other.Mapping)
        && CollectionValues.MapEqual(Fields, other.Fields) && SyncedAt == other.SyncedAt;
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Id); hash.Add(OriginSenseId); hash.Add(CollectionValues.StringHash(Endpoint));
        hash.Add(CollectionValues.StringHash(Profile)); hash.Add(NoteId); hash.Add(CollectionValues.StringHash(Model));
        hash.Add(CollectionValues.StringHash(Deck)); hash.Add(CollectionValues.StringHash(IdentityField));
        hash.Add(CollectionValues.MapHash(Mapping)); hash.Add(CollectionValues.MapHash(Fields)); hash.Add(SyncedAt); return hash.ToHashCode();
    }
}
