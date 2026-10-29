using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class VocabularyFrozenSemanticTests
{
    [Fact]
    public void ExactEvidencePatternRolesAndNodeKindsAreValidated()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var example = new R.VocabularyExample(Guid.NewGuid(), "We pushed it back.", "", [], R.VocabularyExampleCompleteness.Sentence, "delay", now);
        var sense = new R.VocabularySense(Guid.NewGuid(), "delay", now, now) { Examples = [example] };
        var other = new R.VocabularySense(Guid.NewGuid(), "resist", now, now);
        var entry = new R.VocabularyEntry(Guid.NewGuid(), "push back", "push back", LearningLanguage.English, VocabularyEntryKind.Expression, [sense, other], now, now);
        var role = new R.VocabularyMeaningRole("event"); var slot = new R.VocabularyPatternSlot("event");
        var node = new R.VocabularyMeaningNode(Guid.NewGuid(), [new("Postponement")]) { Roles = [role] };
        var pattern = new R.VocabularyUsagePattern(sense.Id, "push [event] back") { Slots = [slot] };
        var mapping = new R.VocabularyMeaningMapping(node.Id, sense.Id) { PatternId = pattern.Id, RoleBindings = [new(role.Id, slot.Id)], EvidenceIds = [example.Id] };
        var index = new R.VocabularySemanticIndex { Nodes = [node], Patterns = [pattern], Mappings = [mapping] };
        R.VocabularySemanticPolicy.Validate(index, [entry]);
        Assert.Throws<VocabularyException>(() => R.VocabularySemanticPolicy.Validate(index with { Mappings = [mapping with { EvidenceIds = [Guid.NewGuid()] }] }, [entry]));
        Assert.Throws<VocabularyException>(() => R.VocabularySemanticPolicy.Validate(index with { Patterns = [pattern with { SenseId = other.Id }] }, [entry]));
        Assert.Throws<VocabularyException>(() => R.VocabularySemanticPolicy.Validate(index with { Nodes = [node with { Kind = R.VocabularyMeaningNodeKind.Function }] }, [entry]));
        Assert.Throws<VocabularyException>(() => R.VocabularySemanticPolicy.Validate(index with { Mappings = [mapping with { RoleBindings = [new(Guid.NewGuid(), slot.Id)] }] }, [entry]));
    }
    [Fact]
    public void CyclesDuplicatePreferredLabelsInvalidEnumsAndMalformedTextAreRejected()
    {
        var a = new R.VocabularyMeaningNode(Guid.NewGuid(), [new("A")]); var b = new R.VocabularyMeaningNode(Guid.NewGuid(), [new("B")]);
        var index = new R.VocabularySemanticIndex { Nodes = [a,b], Relations = [new(a.Id,b.Id) { Kind = R.VocabularyMeaningRelationKind.Narrower }] };
        R.VocabularySemanticPolicy.Validate(index, []);
        Assert.Throws<VocabularyException>(() => R.VocabularySemanticPolicy.Validate(index with { Relations = [index.Relations[0],new(b.Id,a.Id) { Kind = R.VocabularyMeaningRelationKind.Narrower }] }, []));
        Assert.Throws<VocabularyException>(() => R.VocabularySemanticPolicy.Validate(index with { Nodes = [a with { Labels = [new("A"),new("Alias")] },b] }, []));
        Assert.Throws<VocabularyException>(() => R.VocabularySemanticPolicy.Validate(index with { Nodes = [a with { Kind = (R.VocabularyMeaningNodeKind)99 },b] }, []));
        Assert.Throws<VocabularyException>(() => R.VocabularySemanticPolicy.Validate(index with { Nodes = [a with { Labels = [new("\ud800")] },b] }, []));
    }
}
