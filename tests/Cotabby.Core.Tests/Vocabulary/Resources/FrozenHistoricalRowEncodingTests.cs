using System.Text.Json;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenHistoricalRowEncodingTests
{
    private static readonly Guid Id = Guid.Parse("601b6729-bbf5-40c1-acbd-01d8c5d17310");
    private static F.VocabularyDatabaseRecord Row(object payload, F.VocabularyDatabaseTable table) => new(table, Id.ToString(), null, 0, payload);

    [Fact]
    public void HistoricalLeafRetainsItsShapeWhileCurrentEncodingPreservesGuidance()
    {
        var category = new R.VocabularyCategory(Id, R.VocabularyCategoryAxis.Content, [new("Meaning")]);
        var row = Row(category, F.VocabularyDatabaseTable.Categories);
        using var historical = JsonDocument.Parse(F.VocabularyDatabasePayloadCodec.EncodeAt35b(row));
        Assert.False(historical.RootElement.TryGetProperty("scopeNote", out _));
        foreach (var changed in new[]
        {
            category with { ScopeNote = "A narrower domain" },
            category with { Examples = [new("Example", "en")] },
            category with { Localizations = [new("tr", "Anlam")] },
            category with { References = [new("Source", "id", "https://example.test/", R.VocabularyCategoryReferenceRelationship.Adapted)] },
        })
        {
            var changedRow = row with { Payload = changed };
            Assert.Throws<VocabularyException>(() => F.VocabularyDatabasePayloadCodec.EncodeAt35b(changedRow));
            Assert.Equal(changed, F.VocabularyDatabasePayloadCodec.Decode(changedRow.Table, F.VocabularyDatabasePayloadCodec.Encode(changedRow)));
        }
    }

    [Fact]
    public void HistoricalOverlayRejectsEvenExplicitEmptyGuidanceOverrides()
    {
        var overlay = new R.VocabularyCategoryOverride(Id);
        var row = Row(overlay, F.VocabularyDatabaseTable.CategoryOverrides);
        F.VocabularyDatabasePayloadCodec.EncodeAt35b(row);
        foreach (var changed in new[] { overlay with { ScopeNote = "" }, overlay with { Examples = [] },
            overlay with { Localizations = [] }, overlay with { References = [] } })
            Assert.Throws<VocabularyException>(() => F.VocabularyDatabasePayloadCodec.EncodeAt35b(row with { Payload = changed }));
    }
}
