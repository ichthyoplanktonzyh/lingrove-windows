using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenVocabularyClassificationFormTests
{
    [Fact]
    public void NestedCreateAdoptsDirectoryButPreservesOldMembershipBaseline()
    {
        var node = Guid.NewGuid(); var other = Guid.NewGuid(); var old = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node, Guid.NewGuid()); var added = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node, other);
        var baseline = new R.VocabularyClassificationIndex { Memberships = [old] }; var current = baseline with { Categories = [new R.VocabularyCategory(other, R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("new")])], Memberships = [old, added] };
        var adopted = VocabularyClassificationFormPolicy.AdoptDirectory(baseline, current); Assert.Single(adopted.Categories); Assert.Equal(new[] { old }, adopted.Memberships);
        var edited = VocabularyClassificationFormPolicy.SelectMemberships(adopted, node, new HashSet<Guid> { old.CategoryId }); var merged = R.VocabularyClassificationPolicy.Merge(edited, adopted, current); Assert.Contains(added, merged.Memberships);
    }
    [Fact]
    public void ProposedMembershipIsExplicitlyConfirmedAndOthersRemainUntouched()
    {
        var node = Guid.NewGuid(); var category = Guid.NewGuid(); var proposed = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node, category) { Provenance = new() { Origin = R.VocabularySemanticOrigin.Model, Status = R.VocabularySemanticStatus.Proposed, Source = "model" } };
        var other = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), Guid.NewGuid(), category); var result = VocabularyClassificationFormPolicy.SelectMemberships(new() { Memberships = [proposed, other] }, node, new HashSet<Guid> { category });
        var confirmed = result.Memberships.Single(m => m.Id == proposed.Id); Assert.Equal(R.VocabularySemanticStatus.Confirmed, confirmed.Provenance.Status); Assert.Equal(R.VocabularySemanticOrigin.User, confirmed.Provenance.Origin); Assert.Equal(other, result.Memberships[1]);
    }
    [Fact]
    public void BuiltInEditWritesOnlyOverlayAndCustomKeepsParentIds()
    {
        var framework = new R.VocabularyClassificationFramework(Guid.NewGuid(), "f", 1); var label = new R.VocabularyMeaningLabel("original") { Language = LearningLanguage.French }; var built = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [label]) { FrameworkId = framework.Id, Key = "one", Definition = "base" };
        var index = new R.VocabularyClassificationIndex { Frameworks = [framework], Categories = [built] }; var edited = VocabularyClassificationFormPolicy.EditCategory(index, built, label, "  renamed  ", "override", built.Axis, new HashSet<Guid>(), true);
        Assert.Equal(built, edited.Categories.Single()); var overlay = Assert.Single(edited.Overrides); Assert.Equal(label.Id, overlay.Labels!.Single().Id); Assert.Equal(LearningLanguage.French, overlay.Labels!.Single().Language); Assert.Equal("renamed", overlay.Labels!.Single().Text); Assert.Equal("override", overlay.Definition); Assert.True(overlay.IsHidden);
        var parent = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("parent")]); var custom = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [label]); var edge = new R.VocabularyCategoryParent(Guid.NewGuid(), custom.Id, parent.Id);
        var customIndex = new R.VocabularyClassificationIndex { Categories = [parent, custom], Parents = [edge] }; var changed = VocabularyClassificationFormPolicy.EditCategory(customIndex, custom, label, "name", "description", custom.Axis, new HashSet<Guid> { parent.Id }, false);
        Assert.Equal(edge, Assert.Single(changed.Parents)); Assert.Empty(changed.Overrides); Assert.Equal("name", changed.Categories.Single(c => c.Id == custom.Id).Labels.Single().Text);
    }
}
