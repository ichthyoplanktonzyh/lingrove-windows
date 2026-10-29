using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using F = Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class CanonicalVocabularyGraphProjectionTests
{
    private sealed class Search : IVocabularyGraphTextSearching { public bool Contains(string field, string term) => field.Contains(term, StringComparison.OrdinalIgnoreCase); public bool HeadwordContains(string field, string term) => Contains(field, term); }
    private static string Title(F.VocabularySense sense) => sense.Meaning;
    private static string Language(LearningLanguage? language) => language?.Name() ?? "Unspecified";
    [Fact]
    public void UnclassifiedMeaningsStayDistinctAndRelationsRequireConfirmation()
    {
        var shared = Guid.NewGuid(); var a = new F.VocabularyMeaningNode(shared, [new R.VocabularyMeaningLabel("A")]); var b = new F.VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("B")]);
        var confirmed = new R.VocabularyMeaningRelation(a.Id, b.Id) { Kind = R.VocabularyMeaningRelationKind.Opposite }; var proposed = new R.VocabularyMeaningRelation(b.Id, a.Id) { Kind = R.VocabularyMeaningRelationKind.Related, Provenance = new() { Status = R.VocabularySemanticStatus.Proposed } };
        var graph = CanonicalVocabularyGraphProjection.Meanings(new() { Nodes = [a, b], Relations = [confirmed, proposed] }, [], new HashSet<Guid>(), "", new Search(), StringComparer.Ordinal, [], ["en"], Title, Language);
        Assert.Equal(2, graph.HeadwordCount); Assert.Single(graph.Edges); Assert.Equal(VocabularyGraphEdgeKind.Opposite, graph.Edges[0].Kind); Assert.Equal(0, graph.TreePositions[VocabularyGraphNodeId.Meaning(a.Id)].X); Assert.Equal(-48.1, graph.TreePositions[VocabularyGraphNodeId.Meaning(a.Id)].Y, 10); Assert.NotEqual(VocabularyGraphNodeId.Meaning(shared), VocabularyGraphNodeId.Category(shared)); Assert.NotEqual(VocabularyGraphNodeId.Meaning(shared), VocabularyGraphNodeId.Headword(shared));
    }
    [Fact]
    public void EntryProjectionUsesFrozenIdsAndMixedLanguageLabelsWithoutDateConversion()
    {
        var doc = F.VocabularyEntryImportPolicy.Plan([new(Expression: "bank", Meaning: "shore"), new(Expression: "banque", Meaning: "finance", Language: LearningLanguage.French)], new(), new F.SourceDate(1e20)).Document;
        var graph = CanonicalVocabularyGraphProjection.Entries(doc.CollectedEntries, new HashSet<Guid>(), "", new Search(), Title, Language); Assert.Equal(2, graph.Nodes.Count); Assert.All(graph.Nodes, n => Assert.Contains(" · ", n.Label));
        var first = doc.CollectedEntries[0]; graph = CanonicalVocabularyGraphProjection.Entries(doc.CollectedEntries, new HashSet<Guid> { first.Id }, "", new Search(), Title, Language); Assert.Contains(graph.Nodes, n => n.Id.Selection == new VocabularySelection(first.Id, first.Senses[0].Id)); Assert.Equal(new F.SourceDate(1e20), first.CreatedAt);
    }
    [Fact]
    public void TaxonomyTreeKeepsPrimaryParentCountsConfirmedLinksAndStableTreePositions()
    {
        var doc = F.VocabularyEntryImportPolicy.Plan([new(Expression: "bank", Meaning: "shore")], new(), new F.SourceDate(1)).Document; var entry = doc.CollectedEntries.Single(); var sense = entry.Senses.Single();
        var node = new F.VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("River")]); var a = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("Nature")]); var b = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("Water")]); var child = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("Banks")]);
        var semantics = new F.VocabularySemanticIndex { Nodes = [node], Mappings = [new(node.Id, sense.Id)] }; var directory = new R.VocabularyClassificationIndex { Categories = [a, b, child], Parents = [new(Guid.NewGuid(), child.Id, a.Id), new(Guid.NewGuid(), child.Id, b.Id)], Memberships = [new(Guid.NewGuid(), node.Id, child.Id)] };
        var graph = CanonicalVocabularyGraphProjection.Meanings(semantics, doc.CollectedEntries, new HashSet<Guid> { node.Id }, "", new Search(), StringComparer.Ordinal, [], ["en"], Title, Language, directory);
        Assert.True(graph.IsMeaningGraph); Assert.Contains(graph.Nodes, n => n.Id == VocabularyGraphNodeId.Axis(R.VocabularyCategoryAxis.Content)); Assert.Single(graph.Nodes, n => n.Id == VocabularyGraphNodeId.Category(child.Id)); Assert.Contains(graph.Edges, e => e.Source == VocabularyGraphNodeId.Category(a.Id) && e.Target == VocabularyGraphNodeId.Category(child.Id)); Assert.DoesNotContain(graph.Edges, e => e.Source == VocabularyGraphNodeId.Category(b.Id) && e.Target == VocabularyGraphNodeId.Category(child.Id));
        Assert.Contains(graph.Edges, e => e.Source == VocabularyGraphNodeId.Meaning(node.Id) && e.Target.Selection == new VocabularySelection(entry.Id, sense.Id) && e.Kind == VocabularyGraphEdgeKind.ExpressesContent); Assert.Equal(graph.Nodes.Count, graph.TreePositions.Count);
        var again = CanonicalVocabularyGraphProjection.Meanings(semantics, doc.CollectedEntries, new HashSet<Guid> { node.Id }, "", new Search(), StringComparer.Ordinal, [], ["en"], Title, Language, directory); Assert.Equal(graph, again);
    }
    [Fact]
    public void EmptyAndHiddenCategoriesFollowUnexploredAndSearchRules()
    {
        var root = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("Root")]); var child = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("Hidden target")]); var directory = new R.VocabularyClassificationIndex { Categories = [root, child], Parents = [new(Guid.NewGuid(), child.Id, root.Id)] };
        var empty = CanonicalVocabularyGraphProjection.Meanings(new(), [], new HashSet<Guid>(), "", new Search(), StringComparer.Ordinal, [], ["en"], Title, Language, directory); Assert.Empty(empty.Nodes);
        var all = CanonicalVocabularyGraphProjection.Meanings(new(), [], new HashSet<Guid>(), "", new Search(), StringComparer.Ordinal, [], ["en"], Title, Language, directory, showsUnexplored: true); Assert.Contains(all.Nodes, n => n.Id == VocabularyGraphNodeId.Category(root.Id) && n.IsUnexplored); Assert.DoesNotContain(all.Nodes, n => n.Id == VocabularyGraphNodeId.Category(child.Id));
        var found = CanonicalVocabularyGraphProjection.Meanings(new(), [], new HashSet<Guid>(), "target", new Search(), StringComparer.Ordinal, [], ["en"], Title, Language, directory); Assert.Contains(found.Matches, id => id == VocabularyGraphNodeId.Category(child.Id));
        var hidden = directory with { Overrides = [new(child.Id) { IsHidden = true }] }; found = CanonicalVocabularyGraphProjection.Meanings(new(), [], new HashSet<Guid>(), "target", new Search(), StringComparer.Ordinal, [], ["en"], Title, Language, hidden); Assert.DoesNotContain(found.Nodes, n => n.Id == VocabularyGraphNodeId.Category(child.Id));
    }
}
