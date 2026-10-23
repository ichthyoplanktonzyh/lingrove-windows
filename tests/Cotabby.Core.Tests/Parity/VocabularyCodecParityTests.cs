using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Actual Mac-produced bytes must decode and re-encode with matching values and native JSON shape.</summary>
public sealed class VocabularyCodecParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-codec-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsBarePayloadWireShapeAndDecoderAcceptance(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var table = Enum.GetValues<VocabularyDatabaseTable>().Single(table => table.Name() == row.GetProperty("table").GetString());
        JsonNode? actual;
        try
        {
            var decoded = VocabularyPayloadCodec.Decode(Convert.FromBase64String(row.GetProperty("inputBase64").GetString()!), table);
            var encoded = JsonNode.Parse(VocabularyPayloadCodec.Encode(decoded));
            var (identity, time) = Observe(decoded);
            actual = JsonSerializer.SerializeToNode(new { valid = true, json = encoded, identity = identity.ToString("D"), unixTime = time });
        }
        catch (VocabularyException error) when (error.Failure == VocabularyFailure.InvalidArchive) { actual = JsonSerializer.SerializeToNode(new { valid = false }); }
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static double Unix(DateTimeOffset value) => (value - DateTimeOffset.UnixEpoch).TotalSeconds;
    private static (Guid, double?) Observe(VocabularyDatabasePayload payload) => payload switch
    {
        VocabularyDatabasePayload.Entry value => (value.Value.Id, Unix(value.Value.CreatedAt)),
        VocabularyDatabasePayload.Sense value => (value.Value.Id, Unix(value.Value.CreatedAt)),
        VocabularyDatabasePayload.Usage value => (value.Value.Id, Unix(value.Value.CreatedAt)),
        VocabularyDatabasePayload.Explanation value => (value.Value.Id, Unix(value.Value.CreatedAt)),
        VocabularyDatabasePayload.Block value => (value.Value.Id, null),
        VocabularyDatabasePayload.Review value => (value.Value.Plan.Id, null),
        VocabularyDatabasePayload.Binding value => (value.Value.Id, value.Value.SyncedAt is { } time ? Unix(time) : null),
        VocabularyDatabasePayload.Template value => (value.Value.Id, null),
        _ => throw new InvalidOperationException("Unknown payload fixture."),
    };
}
