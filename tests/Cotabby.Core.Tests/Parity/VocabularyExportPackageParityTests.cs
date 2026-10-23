using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;
using Cotabby.Tests.Shared;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Exercises the real package factory/renderer composition against native Swift values.
/// Fixed input construction and observations are the only shared scaffolding.</summary>
public sealed class VocabularyExportPackageParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-package-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsPreparedPayloadCountsPreviewNamesAndValueOwnership(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var actual = JsonSerializer.SerializeToNode(Observe(row));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static object Observe(JsonElement row)
    {
        var options = VocabularyExportProjectionParityTests.Configuration(row); string mode = row.GetProperty("mode").GetString()!;
        var entries = VocabularyExportProjectionParityTests.ExportEntries();
        if (mode == "empty") entries.Clear();
        if (mode == "longPreview") entries[0] = entries[0] with { Expression = new('拾', 6000) };
        if (mode == "manyExamples")
        {
            var sense = entries[0].Senses[0]; var usage = sense.Examples[0];
            sense = sense with { Examples = [usage, new(VocabularyReferenceGraph.Id(8), usage.Text, usage.Translation, usage.Ranges, usage.Completeness, usage.Meaning, DateTimeOffset.UnixEpoch)] };
            entries[0] = entries[0] with { Senses = new[] { sense }.Concat(entries[0].Senses.Skip(1)).ToArray() };
        }
        var cancellation = new CancellationToken(row.TryGetProperty("cancelled", out var c) && c.GetBoolean());
        try
        {
            var p = VocabularyExportPolicy.Package(entries, options, cancellationToken: cancellation);
            if (mode == "ownership")
            {
                var exposed = p.CopyData(); exposed[0] ^= 0xFF;
                byte[] bytes = [1, 2, 3]; var input = new Dictionary<string, byte[]> { ["image.png"] = bytes };
                var attached = p.WithImages(input); bytes[0] = 9; input["image.png"] = bytes;
                var detached = attached.CopyImages(); detached["image.png"][1] = 8;
                var second = VocabularyExportPolicy.Package(entries, options);
                return new { original = Facts(p, options), attached = Facts(attached, options), sameID = p.Id == attached.Id,
                    differentPreparationID = second.Id != p.Id, attachedBytes = attached.CopyImages()["image.png"].Select(b => (int)b).ToArray(),
                    detachedBytes = detached["image.png"].Select(b => (int)b).ToArray(), dataUnchanged = !exposed.AsSpan().SequenceEqual(p.CopyData()) };
            }
            return Facts(p, options);
        }
        catch (OperationCanceledException) { return new { error = "cancelled" }; }
        catch (VocabularyExportException error) { return new { error = JsonNamingPolicy.CamelCase.ConvertName(error.Failure.ToString()) }; }
    }
    private static object Facts(VocabularyExportPackage p, VocabularyExportConfiguration options) => new {
        format = p.Configuration.Format.Code(), sameConfiguration = p.Configuration == options, idNonzero = p.Id != Guid.Empty,
        dataFilename = p.DataFilename, filename = p.Filename, byteCount = p.DataByteCount, sha256 = Convert.ToHexStringLower(SHA256.HashData(p.CopyData())),
        preview = p.Preview, entryCount = p.EntryCount, senseCount = p.SenseCount, exampleCount = p.ExampleCount, imageCount = p.CopyImages().Count };
}
