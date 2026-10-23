using System.Text.Json.Serialization;
using Cotabby.Core.Support;

namespace Cotabby.Core.Review;

/// <summary>Optional plan-owned audio identities, independent of a meaning or generated audio bytes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewAudioKind>))]
public enum ReviewAudioKind
{
    [JsonStringEnumMemberName("expression")] Expression,
    [JsonStringEnumMemberName("sentence")] Sentence,
}

/// <summary>Pure logical-field projection used by future review serialization; it starts no audio work.</summary>
public static class ReviewAudioKinds
{
    public static ReviewField Field(this ReviewAudioKind kind) => kind switch
    {
        ReviewAudioKind.Expression => ReviewField.ExpressionAudio,
        ReviewAudioKind.Sentence => ReviewField.SentenceAudio,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>
/// Acknowledged Anki media identity from Mac; a review plan retains settings/text fingerprints and
/// remote scope, never audio bytes or credentials. This value performs no generation/upload.
/// </summary>
public sealed record ReviewAudioReceipt(string Fingerprint, string TextHash, string Endpoint, string Profile, string Filename)
{
    public bool Equals(ReviewAudioReceipt? other) => other is not null && CollectionValues.Same(Fingerprint, other.Fingerprint)
        && CollectionValues.Same(TextHash, other.TextHash) && CollectionValues.Same(Endpoint, other.Endpoint)
        && CollectionValues.Same(Profile, other.Profile) && CollectionValues.Same(Filename, other.Filename);
    public override int GetHashCode() => HashCode.Combine(CollectionValues.StringHash(Fingerprint), CollectionValues.StringHash(TextHash),
        CollectionValues.StringHash(Endpoint), CollectionValues.StringHash(Profile), CollectionValues.StringHash(Filename));
}
