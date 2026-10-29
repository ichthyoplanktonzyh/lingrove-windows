using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>A single detached meaning task; child selections do not publish intermediate state.</summary>
public sealed class VocabularyMeaningDraft
{
    public VocabularySemanticIndex SemanticBaseline { get; }
    public R.VocabularyClassificationIndex ClassificationBaseline { get; }
    public R.VocabularyClassificationIndex Classification { get; set; }
    public VocabularyMeaningNode Original { get; }
    public Guid LabelId { get; }
    public HashSet<Guid> CategoryIds { get; }
    public HashSet<Guid> SenseIds { get; }
    public bool IsNew => !SemanticBaseline.Nodes.Any(x => x.Id == Original.Id);
    public VocabularyMeaningDraft(VocabularySemanticIndex semantics, R.VocabularyClassificationIndex classification, IReadOnlyList<LearningLanguage> languages, VocabularyMeaningNode? node = null, Guid? categoryId = null, Guid? senseId = null)
    {
        SemanticBaseline = semantics; ClassificationBaseline = Classification = classification;
        var category = classification.Categories.FirstOrDefault(x => x.Id == categoryId && !x.IsRetired);
        Original = node ?? new(Guid.NewGuid(), [new R.VocabularyMeaningLabel("") { Language = languages.Count > 0 ? languages[0] : null }]) { Kind = category?.Axis == R.VocabularyCategoryAxis.Function ? R.VocabularyMeaningNodeKind.Function : R.VocabularyMeaningNodeKind.Concept };
        LabelId = (VocabularyMeaningLabelPolicy.Label(Original.Labels, languages) ?? Original.Labels[0]).Id;
        CategoryIds = node is null ? category is null ? [] : [category.Id] : classification.Memberships.Where(x => x.NodeId == node.Id).Select(x => x.CategoryId).ToHashSet();
        SenseIds = senseId is { } id ? [id] : [];
    }
    public (VocabularySemanticIndex Semantics, R.VocabularyClassificationIndex Classification) Indexes(string title, string definition, R.VocabularyMeaningNodeKind kind, SourceDate now)
    {
        var node = VocabularySemanticFormPolicy.Rename(Original, title, definition, LabelId, now); var semantics = SemanticBaseline;
        if (IsNew)
        {
            node = node with { Kind = kind }; semantics = semantics with { Nodes = semantics.Nodes.Append(node).ToArray(), Mappings = semantics.Mappings.Concat(SenseIds.Select(id => new R.VocabularyMeaningMapping(node.Id, id) { Relation = kind == R.VocabularyMeaningNodeKind.Concept ? R.VocabularyMappingRelation.ExpressesContent : R.VocabularyMappingRelation.RealizesFunction })).ToArray() };
        }
        else semantics = semantics with { Nodes = semantics.Nodes.Select(x => x.Id == node.Id ? node : x).ToArray() };
        return (semantics, VocabularyClassificationFormPolicy.SelectMemberships(Classification, node.Id, CategoryIds));
    }
}
