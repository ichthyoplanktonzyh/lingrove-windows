using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenVocabularySemanticFormTests
{
    [Fact]
    public void RenameKeepsSelectedLabelIdentityAndOtherSourceValues()
    {
        var fr = new R.VocabularyMeaningLabel("bonjour") { Language = LearningLanguage.French }; var en = new R.VocabularyMeaningLabel("hello") { Language = LearningLanguage.English };
        var node = new VocabularyMeaningNode(Guid.NewGuid(), [fr, en]) { CreatedAt = new SourceDate(1e20), UpdatedAt = new SourceDate(-1e20), ScopeNote = "scope", Roles = [new R.VocabularyMeaningRole("actor")] };
        var result = VocabularySemanticFormPolicy.Rename(node, "  welcome  ", "  definition  ", en.Id, new SourceDate(42));
        Assert.Equal(fr, result.Labels[0]); Assert.Equal(en.Id, result.Labels[1].Id); Assert.Equal(en.Language, result.Labels[1].Language); Assert.Equal("welcome", result.Labels[1].Text); Assert.Equal("definition", result.Definition); Assert.Equal(node.CreatedAt, result.CreatedAt); Assert.Equal(node.Roles, result.Roles); Assert.Equal(new SourceDate(42), result.UpdatedAt);
    }
    [Fact]
    public void ReclassifyingKeepsEvidenceAndPatternButClearsOnlyOldRoleBindings()
    {
        var first = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("one")]); var next = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("two")]) { Kind = R.VocabularyMeaningNodeKind.Function };
        var mapping = new R.VocabularyMeaningMapping(first.Id, Guid.NewGuid()) { PatternId = Guid.NewGuid(), EvidenceIds = [Guid.NewGuid()], RoleBindings = [new R.VocabularyRoleBinding(Guid.NewGuid(), Guid.NewGuid())] };
        var unchanged = VocabularySemanticFormPolicy.Classify(mapping, first, "x", "y"); Assert.Single(unchanged.RoleBindings);
        var changed = VocabularySemanticFormPolicy.Classify(mapping, next, "conditions", "nuance"); Assert.Equal(mapping.Id, changed.Id); Assert.Equal(mapping.PatternId, changed.PatternId); Assert.Equal(mapping.EvidenceIds, changed.EvidenceIds); Assert.Empty(changed.RoleBindings); Assert.Equal(R.VocabularyMappingRelation.RealizesFunction, changed.Relation);
    }
    [Fact]
    public void DeletingNodeRemovesOnlyItsLinksAndKeepsCue()
    {
        var one = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("one")]); var two = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("two")]); var cue = new R.VocabularyRetrievalCue("cue");
        var doc = new VocabularySemanticIndex { Nodes = [one, two], Mappings = [new R.VocabularyMeaningMapping(one.Id, Guid.NewGuid()), new R.VocabularyMeaningMapping(two.Id, Guid.NewGuid())], Relations = [new R.VocabularyMeaningRelation(one.Id, two.Id)], Cues = [cue], CueTargets = [new R.VocabularyCueTarget(cue.Id, one.Id)] };
        var result = VocabularySemanticFormPolicy.RemoveNode(doc, one.Id); Assert.Single(result.Nodes); Assert.Single(result.Mappings); Assert.Empty(result.Relations); Assert.Empty(result.CueTargets); Assert.Single(result.Cues);
    }
    [Fact]
    public void ExampleSelectionDoesNotPromoteRepresentativeOrBroadenRestrictedEvidence()
    {
        var first = new VocabularyExample(Guid.NewGuid(), "first", "", [], R.VocabularyExampleCompleteness.Sentence, "", new SourceDate(1));
        var second = new VocabularyExample(Guid.NewGuid(), "second", "", [], R.VocabularyExampleCompleteness.Sentence, "", new SourceDate(2));
        var sense = new VocabularySense(Guid.NewGuid(), "meaning", new SourceDate(1), new SourceDate(1)) { Examples = [first, second] };
        var mapping = new R.VocabularyMeaningMapping(Guid.NewGuid(), sense.Id);
        Assert.Equal(first, VocabularySemanticFormPolicy.Example(mapping, sense)); Assert.Empty(mapping.EvidenceIds);
        Assert.Null(VocabularySemanticFormPolicy.Example(mapping with { PatternId = Guid.NewGuid() }, sense));
        Assert.Null(VocabularySemanticFormPolicy.Example(mapping with { EvidenceIds = [Guid.NewGuid()] }, sense));
        Assert.Equal(second, VocabularySemanticFormPolicy.Example(mapping with { EvidenceIds = [second.Id] }, sense));
    }
}
