using Cotabby.Core.Support;

namespace Cotabby.Core.Review;

/// <summary>Transient navigation metadata returned by an explicit connection probe. The shared
/// controller publishes it to windows; it cannot prove the active profile of a later write.</summary>
public sealed record AnkiReviewCatalog
{
    public AnkiReviewCatalog(string profile, IReadOnlyList<string> decks, IReadOnlyList<string> models)
    { Profile = profile; Decks = CollectionValues.List(decks); Models = CollectionValues.List(models); }
    public string Profile { get; }
    public IReadOnlyList<string> Decks { get; }
    public IReadOnlyList<string> Models { get; }
    public bool Equals(AnkiReviewCatalog? other) => other is not null && CollectionValues.Same(Profile, other.Profile)
        && CollectionValues.SequenceEqual(Decks, other.Decks) && CollectionValues.SequenceEqual(Models, other.Models);
    public override int GetHashCode() => HashCode.Combine(CollectionValues.StringHash(Profile), CollectionValues.SequenceHash(Decks), CollectionValues.SequenceHash(Models));
}

/// <summary>One verified server read, retained only by preparation/acknowledgement work. Its
/// detached fields are distinct from the durable binding's last acknowledged fields.</summary>
public sealed record AnkiRemoteNote
{
    public AnkiRemoteNote(long id, string model, IReadOnlyDictionary<string, string> fields)
    { Id = id; Model = model; Fields = CollectionValues.Map(fields); }
    public long Id { get; }
    public string Model { get; }
    public IReadOnlyDictionary<string, string> Fields { get; }
    public bool Equals(AnkiRemoteNote? other) => other is not null && Id == other.Id && CollectionValues.Same(Model, other.Model) && CollectionValues.MapEqual(Fields, other.Fields);
    public override int GetHashCode() => HashCode.Combine(Id, CollectionValues.StringHash(Model), CollectionValues.MapHash(Fields));
}
