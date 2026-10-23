namespace Cotabby.Core.Review;

/// <summary>One explicit local connection snapshot, borrowed by a review operation. It is never
/// a durable settings object and has no value-style ToString that could reveal the API key.</summary>
public sealed class AnkiConnection(string endpoint, string apiKey)
{
    public string Endpoint { get; } = endpoint;
    public string ApiKey { get; } = apiKey;
}

/// <summary>App-owned Anki boundary, independent of inference and vocabulary storage. A controller
/// owns transaction sequencing; implementations own only request/response effects and cancellation.</summary>
public interface IAnkiConnecting
{
    Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters,
        AnkiConnection connection, CancellationToken cancellationToken = default);
}
