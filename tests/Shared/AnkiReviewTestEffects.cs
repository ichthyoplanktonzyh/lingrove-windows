using Cotabby.Core.Review;

namespace Cotabby.ReviewTesting;

/// <summary>Synthetic effect recorder shared by portable interaction and compiled WPF tests.
/// These fakes contain no key-save guards, visibility rules, controller decisions or HTTP.</summary>
internal sealed class AnkiTestCredentials : IAnkiCredentialStore
{
    internal string Key = "fixture-key";
    internal int Reads, Writes;
    internal bool FailRead, FailWrite;
    public string ReadKey() { Reads++; if (FailRead) throw new AnkiReviewException("fixture read failure"); return Key; }
    public void SaveKey(string value) { Writes++; if (FailWrite) throw new AnkiReviewException("fixture write failure"); Key = value; }
}
internal sealed class AnkiTestClient : IAnkiConnecting, IDisposable
{
    internal List<(string Action, IReadOnlyDictionary<string, AnkiValue> Parameters, string Key)> Calls { get; } = [];
    internal int Disposals;
    internal IReadOnlyList<string> Decks { get; set; } = ["Other", "Default", "Other"];
    internal TaskCompletionSource<AnkiValue>? ProfileGate { get; set; }
    public Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken cancellationToken = default)
    {
        Calls.Add((action, parameters, connection.ApiKey));
        if (action == "getActiveProfile" && ProfileGate is { } gate) return gate.Task.WaitAsync(cancellationToken);
        AnkiValue value = action switch
        {
            "getActiveProfile" => new AnkiValue.Text("Fixture profile"),
            "deckNames" => AnkiValue.List(Decks),
            "modelNames" => AnkiValue.List([ReviewLayoutPolicy.Model]),
            "modelFieldNames" => AnkiValue.List(["LingroveID", "Front", "Back"]),
            _ => throw new InvalidOperationException("Unexpected synthetic request: " + action)
        };
        return Task.FromResult(value);
    }
    public void Dispose() => Disposals++;
}
