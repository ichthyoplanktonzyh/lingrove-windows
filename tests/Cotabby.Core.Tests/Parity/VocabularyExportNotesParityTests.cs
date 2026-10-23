using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Tests.Shared;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Only fixed graph inputs are shared with projection probes. Expected complete UTF-8
/// text and byte counts come from the pinned original Mac renderer, not a test reimplementation.</summary>
public sealed class VocabularyExportNotesParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-notes-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsCompleteTxtAndCsvBytes(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var actual = JsonSerializer.SerializeToNode(Observe(row));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static object Observe(JsonElement row)
    {
        var options = VocabularyExportProjectionParityTests.Configuration(row); var entries = VocabularyExportProjectionParityTests.ExportEntries();
        string mode = row.GetProperty("mode").GetString()!; var sense = entries[0].Senses[0]; var usage = sense.Examples[0];
        if (mode is "budgetExact" or "budgetOver")
        {
            VocabularyExportDocument Projected(string expression) => new([new(Id(1), expression, options.Format == VocabularyExportFormat.Csv ? "" : expression,
                LearningLanguage.English, VocabularyEntryKind.Word, options.Format == VocabularyExportFormat.Csv
                    ? [new(Id(2), DateTimeOffset.UnixEpoch, null, null, null, null, null, null, null, null)] : [])]);
            byte[] RenderBudget(VocabularyExportDocument d) => options.Format == VocabularyExportFormat.Csv ? VocabularyExportFormatter.RenderCsv(d, options) : VocabularyExportFormatter.RenderText(d);
            int overhead = RenderBudget(Projected("")).Length;
            string expression = new('a', VocabularyExportPolicy.MaximumDataBytes - overhead + (mode == "budgetOver" ? 1 : 0));
            try
            {
                byte[] bytes = RenderBudget(Projected(expression));
                return new { byteCount = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
            }
            catch (VocabularyExportException error) { return new { error = JsonNamingPolicy.CamelCase.ConvertName(error.Failure.ToString()) }; }
        }
        switch (mode)
        {
            case "empty": entries.Clear(); break;
            case "noExamples": entries = entries.Select(e => e with { Senses = e.Senses.Select(s => s with { Examples = [] }).ToArray() }).ToList(); break;
            case "manyExamples": sense = sense with { Examples = [usage, new(Id(8), usage.Text, usage.Translation, usage.Ranges, usage.Completeness, usage.Meaning, DateTimeOffset.UnixEpoch)] }; break;
            case "unicodeQuotes":
                entries[0] = entries[0] with { Expression = "=😀,\"語\"\nform" };
                sense = sense with { MeaningLanguage = LearningLanguage.Chinese, Note = " \t=1,\"note\"\r\nline", Tags = ["A", "B;C"],
                    Examples = [usage with { Text = "😀,\"bank\"\nline", TranslationLanguage = LearningLanguage.Chinese, MeaningLanguage = LearningLanguage.English }] }; break;
            case "normalizedList":
                entries[0] = entries[0] with { Expression = "  café\u00A0world " }; entries[1] = entries[1] with { Expression = "cafe\u0301 world" };
                entries.Add(new(Id(10), "café world", "bank", LearningLanguage.French, VocabularyEntryKind.Word, [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
                entries.Add(new(Id(12), "Café world", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)); break;
            case "canonicalForm": entries[0] = entries[0] with { Expression = "é", DictionaryForm = "e\u0301" }; break;
            case "emptyTextBlock": sense = sense with { Content = [new(Id(24), new VocabularyBlockContent.Text(new()))] }; break;
            case "dates":
                var datedUsage = new VocabularyExample(usage.Id, usage.Text, usage.Translation, usage.Ranges, usage.Completeness, usage.Meaning,
                    DateTimeOffset.UnixEpoch.AddMilliseconds(1875), usage.TranslationLanguage, usage.TranslationVariants, usage.MeaningLanguage, usage.MeaningOrigin, usage.SourceApplicationId, usage.Explanations);
                sense = new(sense.Id, sense.Meaning, DateTimeOffset.UnixEpoch.AddMilliseconds(-125), sense.UpdatedAt) {
                    MeaningLanguage = sense.MeaningLanguage, Note = sense.Note, Tags = sense.Tags, Examples = [datedUsage],
                    StandaloneMeanings = sense.StandaloneMeanings, AlternateMeanings = sense.AlternateMeanings, Explanations = sense.Explanations, Content = sense.Content }; break;
        }
        if (entries.Count != 0 && mode != "noExamples") entries[0] = entries[0] with { Senses = new[] { sense }.Concat(entries[0].Senses.Skip(1)).ToArray() };
        var document = VocabularyExportPolicy.Document(entries, options);
        var cancellation = new CancellationToken(row.TryGetProperty("cancelled", out var c) && c.GetBoolean());
        try
        {
            byte[] bytes = options.Format == VocabularyExportFormat.Csv ? VocabularyExportFormatter.RenderCsv(document, options, cancellation)
                : VocabularyExportFormatter.RenderText(document, options.IsExpressionList, cancellationToken: cancellation);
            return new { text = new UTF8Encoding(false, true).GetString(bytes), byteCount = bytes.Length };
        }
        catch (OperationCanceledException) { return new { error = "cancelled" }; }
        catch (VocabularyExportException error) { return new { error = JsonNamingPolicy.CamelCase.ConvertName(error.Failure.ToString()) }; }
    }
    private static Guid Id(int n) => VocabularyReferenceGraph.Id(n);
}
