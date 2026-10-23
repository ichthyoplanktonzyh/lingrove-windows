using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Tests.Shared;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Compare the complete native JSON bytes, not a parse/reserialize of their object shape.
/// Large budget observations use a native digest/count; all smaller outputs keep exact source text.</summary>
public sealed class VocabularyExportJsonParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-json-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsPortableJsonBytesAndWholeExportBudget(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id); var expected = row.GetProperty("expected");
        try
        {
            byte[] bytes = VocabularyExportFormatter.RenderJson(Document(row));
            Assert.False(expected.TryGetProperty("error", out _), "Expected a whole-export budget failure.");
            Assert.Equal(expected.GetProperty("byteCount").GetInt32(), bytes.Length);
            if (expected.TryGetProperty("text", out var text)) Assert.Equal(text.GetString(), new UTF8Encoding(false, true).GetString(bytes));
            else Assert.Equal(expected.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        catch (VocabularyExportException error)
        {
            Assert.True(expected.TryGetProperty("error", out var failure), "Unexpected whole-export budget failure.");
            Assert.Equal(failure.GetString(), JsonNamingPolicy.CamelCase.ConvertName(error.Failure.ToString()));
        }
    }
    private static VocabularyExportDocument Document(JsonElement row)
    {
        string mode = row.GetProperty("mode").GetString()!; var options = VocabularyExportProjectionParityTests.Configuration(row);
        if (mode.StartsWith("budget", StringComparison.Ordinal))
        {
            VocabularyExportDocument Projected(string expression) => VocabularyExportPolicy.Document([
                new(Id(1), expression, "", LearningLanguage.English, VocabularyEntryKind.Word, [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)], options);
            int overhead = VocabularyExportFormatter.RenderJson(Projected("")).Length;
            return Projected(new('a', VocabularyExportPolicy.MaximumDataBytes - overhead + (mode == "budgetOver" ? 1 : 0)));
        }
        if (mode == "uuidLetters") return new([new(Id(0xabcdef), "literal", "literal", LearningLanguage.English, VocabularyEntryKind.Word, [])]);
        if (mode == "dates")
        {
            var usage = new VocabularyExportDocument.Usage(Id(3), DateTimeOffset.UnixEpoch.AddMilliseconds(1875), "😀 bank", VocabularyExampleCompleteness.Fragment,
                [new(3, 4)], "", null, [], null, null, null);
            var sense = new VocabularyExportDocument.Sense(Id(2), DateTimeOffset.UnixEpoch.AddMilliseconds(-125), "", null, [], "", [], [usage], null, []);
            return new([new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [sense])]);
        }
        var entries = VocabularyExportProjectionParityTests.ExportEntries(); var s = entries[0].Senses[0];
        switch (mode)
        {
            case "empty": entries.Clear(); break;
            case "unicode": entries[0] = entries[0] with { Expression = "\uFEFF\0\u0001\u001A\u001F\b\f\n\r\t\" \\ / </script> & \u007F\u0085\u2028\u2029 😀 café e\u0301" }; break;
            case "styledText": s = s with { Content = [new(Id(24), new VocabularyBlockContent.Text(new("literal <&>\n拾语", VocabularyTextStyle.Heading, true, true)))] }; break;
            case "emptyCaption": s = s with { Content = [new(Id(25), new VocabularyBlockContent.Image(new(new('a', 64), 1, 1, 70), ""))] }; break;
            case "variants": s = s with { StandaloneMeanings = [new("literal", null), new("literal", LearningLanguage.French)],
                Examples = [s.Examples[0] with { TranslationVariants = [new("other", null)] }] }; break;
            case "integerMetadata": s = s with { Content = [new(Id(26), new VocabularyBlockContent.Image(new(new('a', 64), long.MaxValue, 1, long.MinValue), "literal"))] }; break;
        }
        if (entries.Count != 0) entries[0] = entries[0] with { Senses = new[] { s }.Concat(entries[0].Senses.Skip(1)).ToArray() };
        return VocabularyExportPolicy.Document(entries, options);
    }
    private static Guid Id(int n) => VocabularyReferenceGraph.Id(n);
}
