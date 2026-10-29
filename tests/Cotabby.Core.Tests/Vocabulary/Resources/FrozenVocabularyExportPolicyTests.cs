using System.Text;
using System.Text.Json;
using F = Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenVocabularyExportPolicyTests
{
    [Fact]
    public void CurrentFixtureSelectionUsesExactIdentitiesAndDetachesSemantics()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "fixtures", "parity", "vocabulary-document-frozen-v1.json")));
        var input = fixture.RootElement.GetProperty("cases").EnumerateArray().First(test => test.GetProperty("id").GetString() == "optional-absence").GetProperty("input");
        var graph = F.VocabularyDocumentCodec.Decode(Encoding.UTF8.GetBytes(input.GetRawText()));
        var entry = graph.CollectedEntries[0]; var sense = entry.Senses[0];
        var selection = new VocabularySelection(entry.Id, sense.Id);
        var selected = F.FrozenVocabularyExportPolicy.Selected(graph.CollectedEntries, [selection, selection]);
        Assert.Single(selected); Assert.Single(selected[0].Senses); Assert.Equal(sense.Id, selected[0].Senses[0].Id);
        var categories = graph.Classification.Categories.Select(c => c with { ScopeNote = "guidance" }).ToArray();
        var result = F.FrozenVocabularyExportPolicy.Document(selected, new(), graph.Semantics, graph.Classification with { Categories = categories });
        Assert.Equal(BitConverter.DoubleToInt64Bits(sense.CreatedAt.ReferenceSeconds), BitConverter.DoubleToInt64Bits(result.Entries[0].Senses[0].CreatedAt.ReferenceSeconds));
        Assert.All(result.Semantics!.Mappings, m => Assert.Equal(sense.Id, m.SenseId));
        using var json = JsonDocument.Parse(F.FrozenVocabularyExportFormatter.RenderJson(result));
        Assert.Equal(result.Version, json.RootElement.GetProperty("version").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("semantics", out _));
        Assert.Equal(5, result.Version); Assert.Contains("guidance", Encoding.UTF8.GetString(F.FrozenVocabularyExportFormatter.RenderJson(result)));
        var withoutExamples = F.FrozenVocabularyExportPolicy.Document(selected, new VocabularyExportConfiguration { Examples = false }, graph.Semantics, graph.Classification);
        Assert.All(withoutExamples.Semantics!.Mappings, mapping => Assert.Empty(mapping.EvidenceIds));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("entries")[0].GetProperty("senses")[0].GetProperty("createdAt").ValueKind);
        var text = Encoding.UTF8.GetString(F.FrozenVocabularyExportFormatter.RenderText(result)); Assert.Contains(entry.Expression, text);
        Assert.Throws<VocabularyExportException>(() => F.FrozenVocabularyExportPolicy.Selected(graph.CollectedEntries, [new(entry.Id, Guid.NewGuid())]));
    }
    [Fact]
    public void UnknownLanguageAndCompletenessDriveVersionsWithoutRoundingSourceDates()
    {
        var stamp = new F.SourceDate(0.99999999); var usage = new F.VocabularyExample(Guid.NewGuid(), "fragment", "", [], Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness.Unknown, "", stamp);
        var sense = new F.VocabularySense(Guid.NewGuid(), "meaning", stamp, stamp) { Examples = [usage] };
        var entry = new F.VocabularyEntry(Guid.NewGuid(), "word", "word", null, VocabularyEntryKind.Word, [sense], stamp, stamp);
        var options = new VocabularyExportConfiguration { Format = VocabularyExportFormat.Json };
        var version3 = F.FrozenVocabularyExportPolicy.Document([entry], options);
        Assert.Equal(3, version3.Version); Assert.Equal(stamp, version3.Entries[0].Senses[0].CreatedAt);
        using var json = JsonDocument.Parse(F.FrozenVocabularyExportFormatter.RenderJson(version3));
        Assert.False(json.RootElement.GetProperty("entries")[0].TryGetProperty("language", out _));
        Assert.Equal("2001-01-01T00:00:00Z", json.RootElement.GetProperty("entries")[0].GetProperty("senses")[0].GetProperty("createdAt").GetString());
        Assert.Equal(2, F.FrozenVocabularyExportPolicy.Document([entry], options with { Examples = false }).Version);
        var english = new F.VocabularyEntry(entry.Id, entry.Expression, entry.DictionaryForm, Cotabby.Core.LanguageLearning.LearningLanguage.English, entry.Kind, entry.Senses, stamp, stamp);
        Assert.Equal(1, F.FrozenVocabularyExportPolicy.Document([english], options with { Examples = false }).Version);
        Assert.Contains("[und]", Encoding.UTF8.GetString(F.FrozenVocabularyExportFormatter.RenderText(version3)));
    }

}
