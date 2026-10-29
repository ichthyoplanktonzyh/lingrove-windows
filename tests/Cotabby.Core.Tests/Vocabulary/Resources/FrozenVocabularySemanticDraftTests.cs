using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenVocabularySemanticDraftTests
{
    [Fact]
    public void MeaningOrganizationMergesBothIndexesAndRejectsConflictTogether()
    {
        var category = Category("category"); var baseline = new F.VocabularyDocument { Classification = new() { Categories = [category] } }; var node = Node("meaning");
        var editedSemantics = baseline.Semantics with { Nodes = [node] }; var membership = new VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, category.Id); var editedClassification = baseline.Classification with { Memberships = [membership] };
        var other = Category("concurrent"); var live = baseline with { Classification = baseline.Classification with { Categories = [category, other] } };
        var result = F.VocabularySemanticDraftPolicy.UpdateMeaningOrganization(live, editedSemantics, baseline.Semantics, editedClassification, baseline.Classification);
        Assert.Equal(node, Assert.Single(result.Semantics.Nodes)); Assert.Equal(membership, Assert.Single(result.Classification.Memberships)); Assert.Equal(2, result.Classification.Categories.Count);
        var changed = live with { Classification = live.Classification with { Categories = [category with { Definition = "other" }, other] } };
        var proposed = editedClassification with { Categories = [category with { Definition = "mine" }] };
        Assert.Throws<VocabularyException>(() => F.VocabularySemanticDraftPolicy.UpdateMeaningOrganization(changed, editedSemantics, baseline.Semantics, proposed, baseline.Classification));
        Assert.Empty(changed.Semantics.Nodes);
    }
    private static readonly F.SourceDate Time = new(814759445.1234567);
    private static F.VocabularyMeaningNode Node(string name) => new(Guid.NewGuid(), [new VocabularyMeaningLabel(name)]) { CreatedAt = Time, UpdatedAt = Time };
    private static VocabularyCategory Category(string name) => new(Guid.NewGuid(), VocabularyCategoryAxis.Content, [new VocabularyMeaningLabel(name)]);
    [Fact]
    public void MeaningDraftCarriesCategoryAndSensesInOneUnpublishedValue()
    {
        var category = Category("function") with { Axis = VocabularyCategoryAxis.Function }; var classification = new VocabularyClassificationIndex { Categories = [category] }; var sense = Guid.NewGuid();
        var draft = new F.VocabularyMeaningDraft(new(), classification, [], categoryId: category.Id, senseId: sense);
        Assert.Equal(VocabularyMeaningNodeKind.Function, draft.Original.Kind); var values = draft.Indexes("greet", "", draft.Original.Kind, Time);
        Assert.Equal(sense, Assert.Single(values.Semantics.Mappings).SenseId); Assert.Equal(VocabularyMappingRelation.RealizesFunction, values.Semantics.Mappings.Single().Relation); Assert.Equal(category.Id, Assert.Single(values.Classification.Memberships).CategoryId); Assert.Empty(classification.Memberships);
    }
    [Fact]
    public void IndependentRowsMergeAndConflictingRowRejectsWhileDatesStayExact()
    {
        var a = Node("a"); var b = Node("b"); var baseline = new F.VocabularySemanticIndex { Nodes = [a] };
        var current = new F.VocabularyDocument { Semantics = baseline with { Nodes = [a, b] } };
        var edited = baseline with { Nodes = [a with { Definition = "edited" }] };
        var next = F.VocabularySemanticDraftPolicy.UpdateSemantics(current, edited, baseline);
        Assert.Equal(new[] { a.Id, b.Id }, next.Semantics.Nodes.Select(value => value.Id));
        Assert.Equal(Time, next.Semantics.Nodes[0].CreatedAt); Assert.Equal("edited", next.Semantics.Nodes[0].Definition);
        var conflicting = current with { Semantics = current.Semantics with { Nodes = [a with { Definition = "other" }, b] } };
        Assert.Throws<VocabularyException>(() => F.VocabularySemanticDraftPolicy.UpdateSemantics(conflicting, edited, baseline));
        var alreadyRemoved = current with { Semantics = new() };
        Assert.Empty(F.VocabularySemanticDraftPolicy.UpdateSemantics(alreadyRemoved, baseline with { Nodes = [] }, baseline).Semantics.Nodes);
        Assert.Throws<VocabularyException>(() => F.VocabularySemanticDraftPolicy.UpdateSemantics(current, baseline with { Nodes = [a, a] }, baseline));
    }
    [Fact]
    public void DeleteNodeRemovesOnlyItsMembershipAndLeavesCatalogOverridesAndOtherMembers()
    {
        var a = Node("a"); var b = Node("b"); var category = Category("custom");
        var m1 = new VocabularyMeaningCategoryMembership(Guid.NewGuid(), a.Id, category.Id);
        var m2 = new VocabularyMeaningCategoryMembership(Guid.NewGuid(), b.Id, category.Id);
        var baseline = new F.VocabularySemanticIndex { Nodes = [a, b] };
        var doc = new F.VocabularyDocument { Semantics = baseline, Classification = new() { Categories = [category], Memberships = [m1, m2] } };
        var result = F.VocabularySemanticDraftPolicy.UpdateSemantics(doc, baseline with { Nodes = [b] }, baseline);
        Assert.Equal(new[] { m2 }, result.Classification.Memberships); Assert.Equal(category, Assert.Single(result.Classification.Categories));
        F.VocabularyArchiveValidation.Validate(result);
    }
    [Fact]
    public void CatalogDefinitionsAreProtectedWhileOverridesAndMembershipsAreExplicit()
    {
        var node = Node("meaning"); var doc = F.VocabularyClassificationInstaller.InstallBundled(new F.VocabularyDocument { Semantics = new() { Nodes = [node] } }, true).Document;
        var baseline = doc.Classification; var builtIn = baseline.Categories[0];
        var membership = new VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, builtIn.Id);
        var edited = baseline with { Memberships = [membership], Overrides = [new VocabularyCategoryOverride(builtIn.Id) { Definition = "custom" }] };
        var result = F.VocabularySemanticDraftPolicy.UpdateClassification(doc, edited, baseline);
        Assert.Equal(membership, Assert.Single(result.Classification.Memberships)); Assert.Equal("custom", Assert.Single(result.Classification.Overrides).Definition);
        Assert.Throws<VocabularyException>(() => F.VocabularySemanticDraftPolicy.UpdateClassification(doc,
            baseline with { Categories = baseline.Categories.Select(value => value.Id == builtIn.Id ? value with { Definition = "tampered" } : value).ToArray() }, baseline));
        Assert.Throws<VocabularyException>(() => F.VocabularySemanticDraftPolicy.UpdateClassification(doc,
            baseline with { Memberships = [membership with { NodeId = Guid.NewGuid() }] }, baseline));
        Assert.NotEmpty(baseline.Parents);
        Assert.Throws<VocabularyException>(() => F.VocabularySemanticDraftPolicy.UpdateClassification(doc,
            baseline with { Parents = baseline.Parents.Skip(1).ToArray() }, baseline));
        F.VocabularyArchiveValidation.Validate(result);
    }
    [Fact]
    public void ClassificationIndependentRowsSurviveWhileSameRowConflictFails()
    {
        var a = Category("a"); var b = Category("b");
        var baseline = new VocabularyClassificationIndex { Categories = [a] };
        var doc = new F.VocabularyDocument { Classification = baseline with { Categories = [a, b] } };
        var edit = baseline with { Categories = [a with { Definition = "edited" }] };
        var result = F.VocabularySemanticDraftPolicy.UpdateClassification(doc, edit, baseline);
        Assert.Equal(new[] { a.Id, b.Id }, result.Classification.Categories.Select(value => value.Id));
        var conflicting = doc with { Classification = doc.Classification with { Categories = [a with { Definition = "other" }, b] } };
        Assert.Throws<VocabularyException>(() => F.VocabularySemanticDraftPolicy.UpdateClassification(conflicting, edit, baseline));
    }
    [Fact]
    public void SemanticAndClassificationCyclesAreRejected()
    {
        var a = Node("a"); var b = Node("b"); var empty = new F.VocabularyDocument();
        var edges = new[] { new VocabularyMeaningRelation(a.Id, b.Id) { Kind = VocabularyMeaningRelationKind.Narrower }, new VocabularyMeaningRelation(b.Id, a.Id) { Kind = VocabularyMeaningRelationKind.Narrower } };
        Assert.Throws<VocabularyException>(() => F.VocabularySemanticDraftPolicy.UpdateSemantics(empty, new() { Nodes = [a, b], Relations = edges }, new()));
        var c = Category("c"); var d = Category("d");
        Assert.Throws<VocabularyException>(() => F.VocabularySemanticDraftPolicy.UpdateClassification(empty,
            new() { Categories = [c, d], Parents = [new(Guid.NewGuid(), c.Id, d.Id), new(Guid.NewGuid(), d.Id, c.Id)] }, new()));
    }
}
