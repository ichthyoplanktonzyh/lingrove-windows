using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Frozen semantic form edits; drafts retain hidden labels, evidence and source identities.
/// Validation, three-way merge and acknowledgement remain the canonical Store's responsibility.</summary>
public static class VocabularySemanticFormPolicy
{
    public static VocabularyMeaningNode Rename(VocabularyMeaningNode original, string title, string definition, Guid labelId, SourceDate now) => original with
    {
        Labels = original.Labels.Select(label => label.Id == labelId ? label with { Text = LearningExplanationText.Trim(title) } : label).ToArray(),
        Definition = LearningExplanationText.Trim(definition), UpdatedAt = now,
    };
    public static VocabularyMeaningMapping Classify(VocabularyMeaningMapping original, VocabularyMeaningNode node, string conditions, string nuance) => original with
    {
        NodeId = node.Id, RoleBindings = original.NodeId == node.Id ? original.RoleBindings : [],
        Relation = node.Kind == VocabularyMeaningNodeKind.Concept ? VocabularyMappingRelation.ExpressesContent : VocabularyMappingRelation.RealizesFunction,
        Conditions = conditions, Nuance = nuance,
    };
    public static VocabularySemanticIndex RemoveNode(VocabularySemanticIndex index, Guid nodeId) => index with
    {
        Nodes = index.Nodes.Where(node => node.Id != nodeId).ToArray(), Mappings = index.Mappings.Where(mapping => mapping.NodeId != nodeId).ToArray(),
        Relations = index.Relations.Where(relation => relation.SourceId != nodeId && relation.TargetId != nodeId).ToArray(),
        CueTargets = index.CueTargets.Where(target => target.NodeId != nodeId).ToArray(),
    };
}
