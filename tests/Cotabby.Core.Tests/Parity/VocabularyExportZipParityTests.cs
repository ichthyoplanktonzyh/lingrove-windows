using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

// Boundary cases intentionally allocate near 192 MiB. Serialize this collection against other
// tests, so independent formatter budget cases do not multiply the runner's live memory ceiling.
[CollectionDefinition("ExportZipBudgets", DisableParallelization = true)]
public sealed class ExportZipBudgetCollection { }

[Collection("ExportZipBudgets")]
public sealed class VocabularyExportZipParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-zip-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacStoredZipBytesAndAdmission(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Support/Vocabulary/VocabularyExportZIP.swift", row.GetProperty("macSource").GetString());
        var actual = JsonSerializer.SerializeToNode(Observe(row));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static object Observe(JsonElement row)
    {
        var package = BuildPackage(row);
        var token = new CancellationToken(row.TryGetProperty("cancelled", out var cancelled) && cancelled.GetBoolean());
        try
        {
            byte[] bytes = VocabularyExportZip.Data(package, token);
            var result = new Dictionary<string, object> { ["byteCount"] = bytes.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
            if (row.TryGetProperty("captureBytes", out var capture) && capture.GetBoolean()) result["base64"] = Convert.ToBase64String(bytes);
            return result;
        }
        catch (OperationCanceledException) { return new { error = "cancelled" }; }
        catch (VocabularyExportException error) { return new { error = JsonNamingPolicy.CamelCase.ConvertName(error.Failure.ToString()) }; }
    }
    private static VocabularyExportPackage BuildPackage(JsonElement row)
    {
        var options = new VocabularyExportConfiguration {
            Format = Enum.Parse<VocabularyExportFormat>(row.GetProperty("format").GetString()!, true),
            TextStyle = row.TryGetProperty("style", out var style) ? Enum.Parse<VocabularyExportTextStyle>(style.GetString()!, true) : VocabularyExportTextStyle.Notes,
            Images = !row.TryGetProperty("imagesFlag", out var imagesFlag) || imagesFlag.GetBoolean()
        };
        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (row.TryGetProperty("images", out var inputs))
            foreach (var input in inputs.EnumerateArray())
            {
                byte[] bytes;
                if (input.TryGetProperty("base64", out var base64)) bytes = Convert.FromBase64String(base64.GetString()!);
                else
                {
                    bytes = new byte[input.GetProperty("byteCount").GetInt32()];
                    if (input.TryGetProperty("fill", out var fill)) Array.Fill(bytes, fill.GetByte());
                }
                images.Add(input.GetProperty("name").GetString()!, bytes);
            }
        if (row.TryGetProperty("generatedFiles", out var generated))
            for (int i = 0; i < generated.GetInt32(); i++) images.Add("lg-image-" + i.ToString("x64", CultureInfo.InvariantCulture) + ".png", []);
        return VocabularyExportPolicy.Package([], options).WithImages(images);
    }
}
