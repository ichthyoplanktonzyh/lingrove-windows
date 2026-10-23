using Cotabby.Core.Support;

namespace Cotabby.Core.Review;

/// <summary>One explicit local connection snapshot, borrowed by a review operation. It is never
/// a durable settings object and has no value-style ToString that could reveal the API key.</summary>
public sealed class AnkiConnection(string endpoint, string apiKey) : IEquatable<AnkiConnection>
{
    public string Endpoint { get; } = endpoint;
    public string ApiKey { get; } = apiKey;
    // Settings supplies the normalized absolute URL. Foundation URL equality distinguishes
    // spelling/default-port/slash differences; Swift String key equality is canonical Unicode.
    public bool Equals(AnkiConnection? other) => other is not null && StringComparer.Ordinal.Equals(Endpoint, other.Endpoint) && CollectionValues.Same(ApiKey, other.ApiKey);
    public override bool Equals(object? obj) => obj is AnkiConnection other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Endpoint), CollectionValues.StringHash(ApiKey));
}

/// <summary>App-owned Anki boundary, independent of inference and vocabulary storage. A controller
/// owns transaction sequencing; implementations own only request/response effects and cancellation.</summary>
public interface IAnkiConnecting
{
    Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters,
        AnkiConnection connection, CancellationToken cancellationToken = default);
}
