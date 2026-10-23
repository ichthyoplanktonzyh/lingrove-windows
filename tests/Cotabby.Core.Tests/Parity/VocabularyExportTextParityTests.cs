using System.Text.Json;
using System.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Consumes independent observations of the actual Mac formatter helper bodies.</summary>
public sealed class VocabularyExportTextParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-text-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsCsvCellsAndByteLimitedPreview(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        if (row.GetProperty("operation").GetString() == "cell")
            Assert.Equal(row.GetProperty("scalars").EnumerateArray().Select(s => s.GetInt32()),
                row.GetProperty("text").GetString()!.EnumerateRunes().Select(r => r.Value));
        string actual = row.GetProperty("operation").GetString() switch
        {
            "cell" => VocabularyExportFormatter.CsvCell(row.GetProperty("text").GetString()!, row.GetProperty("compatibility").GetBoolean()),
            "preview" => VocabularyExportFormatter.Preview(Convert.FromBase64String(row.GetProperty("data").GetString()!),
                Enum.Parse<VocabularyExportFormat>(row.GetProperty("format").GetString()!, true)),
            _ => throw new InvalidOperationException("Unknown export text probe.")
        };
        Assert.Equal(row.GetProperty("expected").GetString(), actual);
    }
}
