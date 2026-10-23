using Cotabby.Core.Support;

namespace Cotabby.Core.Review;

/// <summary>One explicit operation freezes its destination before any await/write. A controller
/// creates this detached snapshot, and a session/batch borrows it until completion. Credentials
/// remain ephemeral inside Connection; this value is never a notebook row or diagnostic payload.</summary>
public sealed class ReviewOperationScope(AnkiConnection connection, string profile, string model,
    IReadOnlyDictionary<ReviewField, string> mapping, string? speechId, string defaultDeck,
    bool hadSavedDeck, bool defaultRecognition, bool defaultGap) : IEquatable<ReviewOperationScope>
{
    public AnkiConnection Connection { get; } = connection;
    public string Profile { get; } = profile;
    public string Model { get; } = model;
    public IReadOnlyDictionary<ReviewField, string> Mapping { get; } = CollectionValues.Map(mapping);
    public string? SpeechId { get; } = speechId;
    public string DefaultDeck { get; } = defaultDeck;
    public bool HadSavedDeck { get; } = hadSavedDeck;
    public bool DefaultRecognition { get; } = defaultRecognition;
    public bool DefaultGap { get; } = defaultGap;
    public bool Equals(ReviewOperationScope? other) => other is not null && Connection.Equals(other.Connection)
        && CollectionValues.Same(Profile, other.Profile) && CollectionValues.Same(Model, other.Model)
        && CollectionValues.MapEqual(Mapping, other.Mapping) && CollectionValues.Same(SpeechId, other.SpeechId)
        && CollectionValues.Same(DefaultDeck, other.DefaultDeck) && HadSavedDeck == other.HadSavedDeck
        && DefaultRecognition == other.DefaultRecognition && DefaultGap == other.DefaultGap;
    public override bool Equals(object? obj) => obj is ReviewOperationScope other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Connection); hash.Add(CollectionValues.StringHash(Profile));
        hash.Add(CollectionValues.StringHash(Model)); hash.Add(CollectionValues.MapHash(Mapping));
        hash.Add(CollectionValues.StringHash(SpeechId)); hash.Add(CollectionValues.StringHash(DefaultDeck));
        hash.Add(HadSavedDeck); hash.Add(DefaultRecognition); hash.Add(DefaultGap); return hash.ToHashCode();
    }
}
