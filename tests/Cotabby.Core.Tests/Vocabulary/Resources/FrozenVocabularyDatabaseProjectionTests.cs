using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenVocabularyDatabaseProjectionTests
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static F.VocabularyDocument Graph()
    {
        var time = new F.SourceDate(814759445.12345672); var sense = new F.VocabularySense(Id(2), "greeting", time, time);
        var entry = new F.VocabularyEntry(Id(1), "hello", "hello", null, VocabularyEntryKind.Word, [sense], time, time);
        var node = new F.VocabularyMeaningNode(Id(6), [new("Greeting") { Id = Id(7), Language = LearningLanguage.English }]) { CreatedAt = time, UpdatedAt = time };
        var category = new R.VocabularyCategory(Id(9), R.VocabularyCategoryAxis.Content, [new("Category") { Id = Id(10) }]);
        return new() { Catalog = [entry], Sources = [new(Id(3), "hello", R.VocabularySourceKind.Imported, null, time)], Occurrences = [new(Id(4), Id(3), [new(0,5)])],
            Interpretations = [new F.VocabularyInterpretation(Id(5), Id(4), time) { SenseId = Id(2), Status = R.VocabularyInterpretationStatus.Accepted }],
            Collections = [new F.VocabularyCollectionItem(Id(2), time) { InterpretationIds = [Id(5)], Content = [] }],
            Semantics = new() { Nodes = [node], Mappings = [new(node.Id,Id(2)) { Id = Id(8), EvidenceIds = [Id(5)] }] },
            Classification = new() { Categories = [category], Memberships = [new(Id(11),node.Id,category.Id)] } };
    }
    [Fact]
    public void PayloadsAreClosedImmutableAlternativesAndTableDispatchIsChecked()
    {
        Assert.ThrowsAny<VocabularyException>(() => new F.VocabularyDatabaseRecord(F.VocabularyDatabaseTable.Entries,"id",null,0,new List<string>()));
        var mismatch = new F.VocabularyDatabaseRecord(F.VocabularyDatabaseTable.Entries,"id",null,0,Guid.Empty);
        Assert.ThrowsAny<VocabularyException>(() => F.VocabularyDatabasePayloadCodec.Encode(mismatch));
        var extra = new List<string?> { "first", null }; var row = new F.VocabularyDatabaseRecord(F.VocabularyDatabaseTable.MappingRoles,"id","parent",0,Guid.Empty,extra);
        extra.Clear(); Assert.Equal(2,row.Extra.Count); Assert.Throws<NotSupportedException>(() => ((IList<string?>)row.Extra).Clear());
    }
    [Fact]
    public void SeparateRowsAndSourceDoubleRoundTripWithNullableIndexes()
    {
        var source = Graph(); var rows = F.VocabularyDatabaseProjection.Rows(source);
        var reconstructed = F.VocabularyDatabaseProjection.Document(rows.Values.ToArray(), false);
        Assert.Equal(source, reconstructed);
        foreach (var row in rows.Values)
        {
            var bytes = F.VocabularyDatabasePayloadCodec.Encode(row);
            Assert.Equal(row.Payload, F.VocabularyDatabasePayloadCodec.Decode(row.Table, bytes));
        }
        var candidate = source.Interpretations[0] with { SenseId = null, Status = R.VocabularyInterpretationStatus.Candidate };
        var uncollected = source with { Interpretations = [candidate], Collections = [], Semantics = new(), Classification = new() };
        var usage = F.VocabularyDatabaseProjection.Rows(uncollected).Values.Single(x => x.Table == F.VocabularyDatabaseTable.Usages);
        Assert.Null(usage.Extra[1]); Assert.Equal(uncollected, F.VocabularyDatabaseProjection.Document(F.VocabularyDatabaseProjection.Rows(uncollected).Values.ToArray(), false));
    }
    [Fact]
    public void ReconstructionRejectsOrphansChangedIndexesAndHiddenChildren()
    {
        var rows = F.VocabularyDatabaseProjection.Rows(Graph()).Values.ToArray();
        var entry = rows.Single(x => x.Table == F.VocabularyDatabaseTable.Entries);
        Assert.ThrowsAny<VocabularyException>(() => F.VocabularyDatabaseProjection.Document(rows.Where(x => x != entry).ToArray(), false));
        var altered = rows.Select(x => x == entry ? x with { Extra = ["wrong", "und", "hello"] } : x).ToArray();
        Assert.ThrowsAny<VocabularyException>(() => F.VocabularyDatabaseProjection.Document(altered, false));
        var hidden = rows.Select(x => x == entry ? x with { Payload = ((F.VocabularyEntry)x.Payload) with { Senses = [Graph().Catalog[0].Senses[0]] } } : x).ToArray();
        Assert.ThrowsAny<VocabularyException>(() => F.VocabularyDatabaseProjection.Document(hidden, false));
    }
}
