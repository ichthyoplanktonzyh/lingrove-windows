using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Tests.Parity;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

/// <summary>Draft outcomes are observations of original pinned Mac editor closures, not C# expectations.</summary>
public sealed class VocabularyEntryEditingParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-editor-v1.json"))).RootElement.Clone();
    // The five reset traces remain in the v43 archive and its unchanged native verifier. Current
    // source preserves annotations; those superseded expectations are not current product tests.
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Where(row => row.GetProperty("operation").GetString() != "change").Select(row => new object[] { row.GetProperty("id").GetString()! });
    private static Guid Id(int value) => Guid.Parse("00000000-0000-0000-0000-" + value.ToString("x12"));
    private static string? Text(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.GetString() : null;
    private static bool Flag(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.GetBoolean();
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalMacEditorSuppliesUnchangedTagSaveAndMoveOutcomes(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        var actual = Observe(row); Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static JsonNode? Observe(JsonElement row)
    {
        var entry = VocabularyProjectionParityTests.Document().Entries[0]; var sense = entry.Senses[0];
        switch (row.GetProperty("operation").GetString())
        {
            case "tags": return JsonSerializer.SerializeToNode(new { tags = VocabularyEntryEditingPolicy.ParseTags(Text(row, "text")!) });
            case "save":
                entry = entry with { Expression = Text(row, "expression") ?? entry.Expression, DictionaryForm = Flag(row, "emptyForm") ? "" : entry.DictionaryForm };
                if (Flag(row, "emptyContent")) sense = sense with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new()))] };
                if (Flag(row, "noExamples")) sense = sense with { Examples = [] };
                else if (Text(row, "text") is { } value) sense = sense with { Examples = [sense.Examples[0] with { Text = value }] };
                entry = entry with { Senses = [sense] };
                return JsonSerializer.SerializeToNode(new { enabled = VocabularyEntryEditingPolicy.CanSave(entry, Flag(row, "missingSense") ? Id(999) : Id(2), Flag(row, "saving"), Flag(row, "importing")) });
            case "move":
                var blocks = new VocabularyContentBlock[] { new(Id(20), new VocabularyBlockContent.Text(new("literal"))),
                    new(Id(21), new VocabularyBlockContent.Image(new(new string('a', 64), 1, 1, 1), "retain")), new(Id(22), new VocabularyBlockContent.Text(new("literal"))) };
                var moved = VocabularyEntryEditingPolicy.MoveBlock(blocks, Id(row.GetProperty("block").GetInt32()), row.GetProperty("offset").GetInt32())!;
                return JsonSerializer.SerializeToNode(new { ids = moved.Select(block => block.Id.ToString("D")), imageRetained = moved.Any(block => block.Content is VocabularyBlockContent.Image { Caption: "retain" }) });
            default: throw new InvalidOperationException("Unknown fixed editor operation");
        }
    }
}
