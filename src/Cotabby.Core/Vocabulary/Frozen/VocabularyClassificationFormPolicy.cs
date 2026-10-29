using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Detached classification sheet edits. The Store owns conflict checks, catalog protection
/// and acknowledgement; these rules preserve the source baseline and unedited identities.</summary>
public static class VocabularyClassificationFormPolicy
{
    public static VocabularyClassificationIndex EditCurrentCategory(VocabularyClassificationIndex baseline, VocabularyCategory original, string locale, string name, string definition, VocabularyCategoryAxis axis, IReadOnlySet<Guid> parents, bool hidden, bool restoringDefaults = false)
    {
        var (value, overlay) = VocabularyCategoryLocalizationPolicy.Edit(original, baseline, locale, name, definition, hidden, restoringDefaults);
        bool exists = baseline.Categories.Any(x => x.Id == original.Id); if (!exists) value = value with { Axis = axis };
        var categories = exists ? baseline.Categories : baseline.Categories.Append(value).ToArray();
        var overlays = baseline.Overrides.Where(x => x.CategoryId != original.Id).ToList(); if (overlay is not null) overlays.Add(overlay);
        var edges = baseline.Parents;
        if (original.FrameworkId is null)
        {
            var mutable = edges.Where(edge => edge.CategoryId != value.Id || parents.Contains(edge.ParentId)).ToList();
            foreach (var parent in parents.Where(parent => !mutable.Any(edge => edge.CategoryId == value.Id && edge.ParentId == parent))) mutable.Add(new(Guid.NewGuid(), value.Id, parent)); edges = mutable;
        }
        return baseline with { Categories = categories, Parents = edges, Overrides = overlays };
    }
    public static VocabularyClassificationIndex AdoptDirectory(VocabularyClassificationIndex baseline, VocabularyClassificationIndex current) => current with { Memberships = baseline.Memberships };
    public static VocabularyClassificationIndex SelectMemberships(VocabularyClassificationIndex baseline, Guid nodeId, IReadOnlySet<Guid> selected)
    {
        var memberships = baseline.Memberships.Where(m => m.NodeId != nodeId || selected.Contains(m.CategoryId))
            .Select(m => m.NodeId == nodeId && selected.Contains(m.CategoryId) && m.Provenance.Status == VocabularySemanticStatus.Proposed
                ? m with { Provenance = new VocabularySemanticProvenance { Origin = VocabularySemanticOrigin.User, Status = VocabularySemanticStatus.Confirmed } } : m).ToList();
        foreach (var id in selected.Where(id => !memberships.Any(m => m.NodeId == nodeId && m.CategoryId == id))) memberships.Add(new(Guid.NewGuid(), nodeId, id));
        return baseline with { Memberships = memberships };
    }
    public static VocabularyClassificationIndex EditCategory(VocabularyClassificationIndex baseline, VocabularyCategory original, VocabularyMeaningLabel label,
        string name, string definition, VocabularyCategoryAxis axis, IReadOnlySet<Guid> parents, bool hidden)
    {
        var labels = VocabularyClassificationPolicy.Effective(original, baseline).Labels.Select(value => value.Id == label.Id ? value with { Text = LearningExplanationText.Trim(name) } : value).ToArray();
        var overlays = baseline.Overrides.Where(value => value.CategoryId != original.Id).ToList();
        if (hidden || original.FrameworkId is not null) overlays.Add(new(original.Id) { Labels = original.FrameworkId is null ? null : labels, Definition = original.FrameworkId is null ? null : definition, IsHidden = hidden });
        var categories = baseline.Categories; var edges = baseline.Parents;
        if (original.FrameworkId is null)
        {
            var value = original with { Axis = axis, Labels = labels, Definition = definition };
            categories = categories.Any(c => c.Id == value.Id) ? categories.Select(c => c.Id == value.Id ? value : c).ToArray() : categories.Append(value).ToArray();
            var mutable = edges.Where(edge => edge.CategoryId != value.Id || parents.Contains(edge.ParentId)).ToList();
            foreach (Guid parent in parents.Where(parent => !mutable.Any(edge => edge.CategoryId == value.Id && edge.ParentId == parent))) mutable.Add(new(Guid.NewGuid(), value.Id, parent));
            edges = mutable;
        }
        return baseline with { Categories = categories, Parents = edges, Overrides = overlays };
    }
}
