using System.Text;
using System.Text.Json;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests;

public sealed class CliVocabularyProjectionTests
{
    private sealed class Search : IVocabularyTextSearching { public bool Contains(string field, string term) => field.Contains(term, StringComparison.OrdinalIgnoreCase); }
    private static F.VocabularyDocument Fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "fixtures", "parity", "vocabulary-document-frozen-v1.json")));
        var input = json.RootElement.GetProperty("cases").EnumerateArray().First(x => x.GetProperty("id").GetString() == "optional-absence").GetProperty("input"); return F.VocabularyDocumentCodec.Decode(Encoding.UTF8.GetBytes(input.GetRawText()));
    }
    [Fact]
    public void CollectedListAndGetOmitUncollectedAndPreserveIdentity()
    {
        var doc = Fixture(); var collected = doc.CollectedEntries; var status = CliVocabularyProjection.Status(doc, 9);
        Assert.Equal(collected.Count, status["entryCount"]!.GetValue<int>()); Assert.Equal(doc.Catalog.Count, status["catalogEntryCount"]!.GetValue<int>());
        var entry = collected[0]; var list = CliVocabularyProjection.List(doc, 9, new Search(), entry.Expression);
        Assert.Equal(entry.Id, Guid.Parse(list["entries"]![0]!["entryID"]!.GetValue<string>()));
        var detail = CliVocabularyProjection.Get(doc, 9, entry.Id); Assert.Equal(entry.Id, Guid.Parse(detail["entry"]!["id"]!.GetValue<string>()));
        Assert.Throws<CliException>(() => CliVocabularyProjection.Get(doc, 9, Guid.NewGuid()));
    }
    [Fact]
    public void EntryBudgetRejectsBeforeOutputAndSearchValidatesLimits()
    {
        var doc = Fixture(); var entry = doc.CollectedEntries[0]; Assert.Throws<CliException>(() => CliReadBudget.Validate(entry, 64));
        Assert.Throws<CliException>(() => CliVocabularyProjection.List(doc, 0, new Search(), language: "invalid"));
        Assert.Throws<CliException>(() => CliVocabularyProjection.List(doc, 0, new Search(), limit: 201));
    }
}
