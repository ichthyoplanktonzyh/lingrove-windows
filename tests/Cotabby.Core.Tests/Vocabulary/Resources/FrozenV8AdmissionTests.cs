using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenV8AdmissionTests
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static F.VocabularyDocument Graph()
    {
        var time = new F.SourceDate(814759445.12345672);
        var sense = new F.VocabularySense(Id(2), "meaning", time, time);
        var entry = new F.VocabularyEntry(Id(1), "hello", "hello", null, VocabularyEntryKind.Word, [sense], time, time);
        var source = new F.VocabularySourceSnapshot(Id(3), "hello", R.VocabularySourceKind.Imported, null, time);
        return new() { Catalog = [entry], Sources = [source], Occurrences = [new(Id(4), Id(3), [new(0,5)])],
            Interpretations = [new F.VocabularyInterpretation(Id(5), Id(4), time) { SenseId = Id(2), Status = R.VocabularyInterpretationStatus.Accepted }] };
    }
    private static void Reject(F.VocabularyDocument value) => Assert.ThrowsAny<VocabularyException>(() => F.VocabularyArchiveValidation.Validate(value));
    [Fact]
    public void V8AdmissionRetainsUncollectedEvidenceAndValidatesIndependentIdentities()
    {
        var graph = Graph(); F.VocabularyArchiveValidation.Validate(graph);
        Assert.Empty(graph.CollectedEntries); Assert.Single(graph.Entries[0].Examples);
        var entry = graph.Catalog[0]; var sense = entry.Senses[0];
        Reject(graph with { Catalog = [entry with { Senses = [sense with { MergedFrom = [entry.Id] }] }] });
        Reject(graph with { Occurrences = [new(Id(4), Id(3), [new(100,1)])] });
        Reject(graph with { Collections = [new F.VocabularyCollectionItem(Id(2), new(0)) { InterpretationIds = [Id(999)] }] });
    }
    [Fact]
    public void V8AdmissionChecksSemanticAndClassificationReferencesWithoutDateConversion()
    {
        var graph = Graph(); var node = new F.VocabularyMeaningNode(Id(6), [new("Greeting") { Id = Id(7) }]) { CreatedAt = new(1e100), UpdatedAt = new(1e-8) };
        var category = new R.VocabularyCategory(Id(8), R.VocabularyCategoryAxis.Content, [new("Category") { Id = Id(9) }]);
        var valid = graph with { Semantics = new() { Nodes = [node], Mappings = [new(node.Id, Id(2)) { Id = Id(10), EvidenceIds = [Id(5)] }] },
            Classification = new() { Categories = [category], Memberships = [new(Id(11), node.Id, category.Id)] } };
        F.VocabularyArchiveValidation.Validate(valid);
        Reject(valid with { Semantics = valid.Semantics with { Mappings = [new(node.Id, Id(2)) { EvidenceIds = [Id(999)] }] } });
        Reject(valid with { Classification = valid.Classification with { Memberships = [new(Id(11), Id(999), category.Id)] } });
        Reject(valid with { Semantics = valid.Semantics with { Nodes = [node with { Provenance = null! }] } });
    }
}
