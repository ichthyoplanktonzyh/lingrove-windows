using System.Text;
using System.Text.Json;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests;

public sealed class CliResourceProjectionTests
{
    private sealed class Search : IVocabularyTextSearching { public bool Contains(string field, string term) => field.Contains(term, StringComparison.OrdinalIgnoreCase); }
    private static F.VocabularyDocument Fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "fixtures", "parity", "vocabulary-document-frozen-v1.json")));
        var value = json.RootElement.GetProperty("cases").EnumerateArray().First(x => x.GetProperty("id").GetString() == "optional-absence").GetProperty("input");
        return F.VocabularyDocumentCodec.Decode(Encoding.UTF8.GetBytes(value.GetRawText()));
    }
    [Fact]
    public void AllResourceFamiliesProjectOnlyClosedFieldsAndPreserveUnixDate()
    {
        var doc = Fixture(); foreach (string name in CliArguments.Resources)
        {
            var shape = CliShapeContract.Resource(name); foreach (var row in CliResourceProjection.Records(doc, name))
                Assert.All(row, field => Assert.Contains(field.Key, shape.Fields.Keys));
        }
        var source = CliResourceProjection.Records(doc, "sources")[0];
        Assert.Equal(doc.Sources[0].EncounteredAt.ReferenceSeconds + 978307200, source["encounteredAt"]!.GetValue<double>());
        var sense = CliResourceProjection.Records(doc, "senses")[0]; Assert.Equal(doc.Catalog[0].Id, Guid.Parse(sense["entryID"]!.GetValue<string>()));
        Assert.DoesNotContain("review", CliResourceProjection.Records(doc, "collections")[0].ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void RelatedStatusAndPagePreserveRevisionAndRawResources()
    {
        var doc = Fixture(); var sourceId = doc.Sources[0].Id;
        var page = CliResourceProjection.List(doc, 7, "occurrences", new Search(), related: sourceId, limit: 1);
        Assert.Equal(7ul, page["revision"]!.GetValue<ulong>()); Assert.True(page["total"]!.GetValue<int>() > 0); Assert.Single(page["records"]!.AsArray());
        Assert.Equal(sourceId, Guid.Parse(CliResourceProjection.Get(doc, 7, "sources", sourceId)["record"]!["id"]!.GetValue<string>()));
        Assert.Throws<CliException>(() => CliResourceProjection.List(doc, 7, "sources", new Search(), status: "accepted"));
        Assert.Equal(CliErrorCode.NotFound, Assert.Throws<CliException>(() => CliResourceProjection.Get(doc, 7, "sources", Guid.NewGuid())).Code);
    }
}
