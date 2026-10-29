using System.Text.Json.Nodes;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests;

public sealed class CliSourceCorpusTests
{
    [Fact]
    public void WindowsConsumesTheShared3bFingerprintIdAndRangeCases()
    {
        var corpus = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "cli-3b-source-v1.json")))!.AsObject();
        Assert.Equal("3b53402f2cbc196b0398d133177fd80006d165b2", corpus["macReference"]!.GetValue<string>());
        foreach (var row in corpus["batches"]!.AsArray())
        {
            var batch = CliInputContract.ValidateWire(row!["batch"]!.AsObject()); var hash = CliIdentity.Fingerprint(batch);
            Assert.Equal(row["expectedFingerprint"]!.GetValue<string>(), hash);
            var id = CliIdentity.OperationId(batch.CopyValue()["idempotencyKey"]!.GetValue<string>(), hash, 0, "entry");
            Assert.Equal(row["expectedId"]!.GetValue<string>(), id.ToString("D").ToUpperInvariant());
        }
        foreach (var row in corpus["ranges"]!.AsArray())
        {
            var add = new JsonObject { ["expression"] = row!["expression"]!.DeepClone(), ["language"] = "en", ["kind"] = "word", ["meaning"] = "m",
                ["examples"] = new JsonArray(new JsonObject { ["text"] = row["text"]!.DeepClone() }) };
            var raw = new JsonObject { ["version"] = 1, ["idempotencyKey"] = "range", ["operations"] = new JsonArray(new JsonObject { ["add"] = add }) };
            var batch = CliInputContract.ValidateWire(raw); string actual;
            try
            {
                var result = CliBatchPlan.Create(batch, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(batch), new F.SourceDate(0), new Dictionary<string, VocabularyPreparedImage>());
                var ranges = result.Document.Occurrences.Single().Ranges;
                actual = ranges.Count == 0 ? "missing" : $"{ranges[0].Location}:{ranges[0].Length}";
            }
            catch (CliException) { actual = "ambiguous"; }
            Assert.Equal(row["expected"]!.GetValue<string>(), actual);
        }
    }
}
