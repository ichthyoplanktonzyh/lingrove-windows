using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Pure bounded directory admission and detached edits. No persistence or classification inference.</summary>
public static class VocabularyClassificationPolicy
{
    public static void Validate(VocabularyClassificationIndex index, IReadOnlyList<VocabularyMeaningNode> nodes)
    {
        if (nodes is null || nodes.Any(x => x is null)) throw Invalid();
        ValidateNodeIds(index, nodes.Select(x => x.Id).ToHashSet());
    }
    // Both resource versions validate the directory against explicit meaning identities.
    internal static void ValidateNodeIds(VocabularyClassificationIndex index, IReadOnlySet<Guid> nodeIds)
    {
        if (index is null || nodeIds is null || index.Frameworks.Count > 64 || index.Categories.Count > 20_000 || index.Parents.Count > 100_000
            || index.Memberships.Count > 500_000 || index.Overrides.Count > 20_000) throw Invalid();
        var frameworks = Identified(index.Frameworks, x => x.Id); var categories = Identified(index.Categories, x => x.Id);
        _ = Identified(index.Parents, x => x.Id); _ = Identified(index.Memberships, x => x.Id); _ = Identified(index.Overrides, x => x.Id);
        var frameworkKeys = new HashSet<string>(CanonicalStringComparer.Instance); var categoryKeys = new HashSet<(Guid, string)>();
        foreach (var framework in index.Frameworks)
            if (!Fits(framework.Key, 200) || framework.Key.Length == 0 || framework.Version <= 0 || !frameworkKeys.Add(framework.Key)) throw Invalid();
        foreach (var category in index.Categories)
        {
            if (!Enum.IsDefined(category.Axis)) throw Invalid();
            Labels(category.Labels); Text(category.Definition); Guidance(category.Examples, category.ScopeNote, category.References, category.Localizations); Provenance(category.Provenance);
            if (category.FrameworkId is { } frameworkId)
            {
                if (!frameworks.ContainsKey(frameworkId) || !Fits(category.Key, 200) || category.Key!.Length == 0 || !categoryKeys.Add((frameworkId, FoundationUnicodeNormalization.Normalize(category.Key)))) throw Invalid();
            }
            else if (category.Key is not null || category.IsRetired) throw Invalid();
        }
        var edges = new HashSet<(Guid, Guid)>(); var children = new Dictionary<Guid, List<Guid>>(); var indegree = categories.Keys.ToDictionary(x => x, _ => 0);
        foreach (var edge in index.Parents)
        {
            if (!categories.TryGetValue(edge.CategoryId, out var child) || !categories.TryGetValue(edge.ParentId, out var parent)
                || child.Axis != parent.Axis || child.Id == parent.Id || child.FrameworkId is not null && child.FrameworkId != parent.FrameworkId
                || !edges.Add((edge.CategoryId, edge.ParentId))) throw Invalid();
            if (!children.TryGetValue(edge.ParentId, out var list)) children[edge.ParentId] = list = [];
            list.Add(edge.CategoryId); indegree[edge.CategoryId]++;
        }
        var queue = new Queue<Guid>(indegree.Where(x => x.Value == 0).Select(x => x.Key)); var visited = 0;
        while (queue.TryDequeue(out var id)) { visited++; if (children.TryGetValue(id, out var list)) foreach (var child in list) if (--indegree[child] == 0) queue.Enqueue(child); }
        if (visited != categories.Count) throw Invalid();
        var memberships = new HashSet<(Guid, Guid)>();
        foreach (var membership in index.Memberships)
        {
            if (!nodeIds.Contains(membership.NodeId) || !categories.ContainsKey(membership.CategoryId) || !memberships.Add((membership.NodeId, membership.CategoryId))) throw Invalid();
            Provenance(membership.Provenance);
        }
        foreach (var overlay in index.Overrides)
        {
            if (!categories.ContainsKey(overlay.CategoryId)) throw Invalid();
            if (overlay.Labels is not null) Labels(overlay.Labels);
            if (overlay.Definition is not null) Text(overlay.Definition);
            Guidance(overlay.Examples ?? [], overlay.ScopeNote ?? "", overlay.References ?? [], overlay.Localizations ?? []);
        }
    }

    public static VocabularyCategory Effective(VocabularyCategory category, VocabularyClassificationIndex index)
    {
        var overlay = index.Overrides.FirstOrDefault(x => x.CategoryId == category.Id);
        return overlay is null ? category : category with { Labels = overlay.Labels ?? category.Labels, Definition = overlay.Definition ?? category.Definition, Examples = overlay.Examples ?? category.Examples, ScopeNote = overlay.ScopeNote ?? category.ScopeNote, References = overlay.References ?? category.References, Localizations = VocabularyCategoryLocalizationPolicy.Overlay(category, overlay) };
    }
    private static void Guidance(IReadOnlyList<VocabularyCategoryExample> examples, string scopeNote, IReadOnlyList<VocabularyCategoryReference> references, IReadOnlyList<VocabularyCategoryLocalization> localizations)
    {
        Text(scopeNote); if (examples.Count > 64 || references.Count > 64) throw Invalid();
        foreach (var example in examples) if (example is null || string.IsNullOrWhiteSpace(example.Text) || !Fits(example.Text, 4096) || example.Language is { } language && !VocabularyCategoryLocalizationPolicy.ValidLanguageTag(language)) throw Invalid();
        foreach (var reference in references) { if (reference is null || string.IsNullOrWhiteSpace(reference.Source) || string.IsNullOrWhiteSpace(reference.Identifier) || !Enum.IsDefined(reference.Relationship) || !Uri.TryCreate(reference.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || string.IsNullOrEmpty(url.Host)) throw Invalid(); Text(reference.Source); Text(reference.Identifier); Text(reference.Url); if (reference.Attribution is not null) Text(reference.Attribution); if (reference.License is not null) Text(reference.License); }
        VocabularyCategoryLocalizationPolicy.Validate(localizations);
    }
    public static string Title(VocabularyCategory category, VocabularyClassificationIndex index, IReadOnlyList<LearningLanguage> languages)
    {
        var labels = Effective(category, index).Labels;
        static VocabularyMeaningLabel? Preferred(IEnumerable<VocabularyMeaningLabel> source)
        {
            var values = source.ToArray(); return values.FirstOrDefault(x => x.Preferred) ?? values.FirstOrDefault();
        }
        foreach (var language in languages) { var label = Preferred(labels.Where(x => x.Language == language)); if (label is not null) return label.Text; }
        return (Preferred(labels.Where(x => x.Language is null)) ?? Preferred(labels.Where(x => x.Language == LearningLanguage.English)) ?? Preferred(labels))?.Text ?? "";
    }
    public static IReadOnlySet<Guid> Descendants(Guid id, VocabularyClassificationIndex index)
    {
        var children = index.Parents.ToLookup(x => x.ParentId); var found = new HashSet<Guid> { id }; var queue = new Queue<Guid>(); queue.Enqueue(id);
        while (queue.TryDequeue(out var current)) foreach (var edge in children[current]) if (found.Add(edge.CategoryId)) queue.Enqueue(edge.CategoryId);
        return found;
    }
    public static IReadOnlySet<Guid> MeaningIds(Guid categoryId, VocabularyClassificationIndex index)
    {
        var categories = Descendants(categoryId, index);
        return index.Memberships.Where(x => categories.Contains(x.CategoryId) && x.Provenance.Status == VocabularySemanticStatus.Confirmed).Select(x => x.NodeId).ToHashSet();
    }
    public static VocabularyClassificationIndex Selected(VocabularyClassificationIndex index, IReadOnlyList<VocabularyMeaningNode> nodes)
    {
        if (nodes.Any(x => x is null)) throw Invalid();
        var nodeIds = nodes.Select(x => x.Id).ToHashSet(); var memberships = index.Memberships.Where(x => nodeIds.Contains(x.NodeId)).ToArray();
        var parents = index.Parents.ToLookup(x => x.CategoryId); var ids = memberships.Select(x => x.CategoryId).ToHashSet(); var queue = new Queue<Guid>(ids);
        while (queue.TryDequeue(out var id)) foreach (var edge in parents[id]) if (ids.Add(edge.ParentId)) queue.Enqueue(edge.ParentId);
        var categories = index.Categories.Where(x => ids.Contains(x.Id)).ToArray(); var frameworks = categories.Where(x => x.FrameworkId.HasValue).Select(x => x.FrameworkId!.Value).ToHashSet();
        return new() { Frameworks = index.Frameworks.Where(x => frameworks.Contains(x.Id)).ToArray(), Categories = categories,
            Parents = index.Parents.Where(x => ids.Contains(x.CategoryId) && ids.Contains(x.ParentId)).ToArray(), Memberships = memberships,
            Overrides = index.Overrides.Where(x => ids.Contains(x.CategoryId)).ToArray() };
    }
    public static VocabularyClassificationIndex RemoveCategory(Guid id, VocabularyClassificationIndex index)
    {
        if (!index.Categories.Any(x => x.Id == id && x.FrameworkId is null)) throw Invalid();
        return index with { Categories = index.Categories.Where(x => x.Id != id).ToArray(), Parents = index.Parents.Where(x => x.CategoryId != id && x.ParentId != id).ToArray(),
            Memberships = index.Memberships.Where(x => x.CategoryId != id).ToArray(), Overrides = index.Overrides.Where(x => x.CategoryId != id).ToArray() };
    }
    public static VocabularyClassificationIndex Merge(VocabularyClassificationIndex edited, VocabularyClassificationIndex baseline, VocabularyClassificationIndex current)
    {
        var builtIns = baseline.Categories.Where(x => x.FrameworkId is not null).Select(x => x.Id).ToHashSet();
        if (!edited.Frameworks.SequenceEqual(baseline.Frameworks) || !edited.Categories.Where(x => x.FrameworkId is not null).SequenceEqual(baseline.Categories.Where(x => x.FrameworkId is not null))
            || !edited.Parents.Where(x => builtIns.Contains(x.CategoryId)).SequenceEqual(baseline.Parents.Where(x => builtIns.Contains(x.CategoryId))) ) throw Invalid();
        return current with { Categories = MergeRows(edited.Categories, baseline.Categories, current.Categories, x => x.Id),
            Parents = MergeRows(edited.Parents, baseline.Parents, current.Parents, x => x.Id), Memberships = MergeRows(edited.Memberships, baseline.Memberships, current.Memberships, x => x.Id),
            Overrides = MergeRows(edited.Overrides, baseline.Overrides, current.Overrides, x => x.Id) };
    }
    private static IReadOnlyList<T> MergeRows<T>(IReadOnlyList<T> edited, IReadOnlyList<T> baseline, IReadOnlyList<T> current, Func<T, Guid> id) where T : class
    {
        var result = current.ToList();
        foreach (var before in baseline.Where(x => !edited.Any(y => id(y) == id(x))))
        {
            if (!Equals(current.FirstOrDefault(x => id(x) == id(before)), before)) throw new VocabularyException(VocabularyFailure.EditConflict);
            result.RemoveAll(x => id(x) == id(before));
        }
        foreach (var after in edited.Where(x => !Equals(baseline.FirstOrDefault(y => id(y) == id(x)), x)))
        {
            var before = baseline.FirstOrDefault(x => id(x) == id(after)); var live = current.FirstOrDefault(x => id(x) == id(after));
            if (!Equals(live, before) && !Equals(live, after)) throw new VocabularyException(VocabularyFailure.EditConflict);
            var position = result.FindIndex(x => id(x) == id(after)); if (position < 0) result.Add(after); else result[position] = after;
        }
        return result;
    }
    private static Dictionary<Guid, T> Identified<T>(IReadOnlyList<T> values, Func<T, Guid> id) where T : class
    {
        var result = new Dictionary<Guid, T>(); foreach (var value in values) if (value is null || !result.TryAdd(id(value), value)) throw Invalid(); return result;
    }
    internal static bool Fits(string? value, int max) => value is not null && LearningExplanationText.Fits(value, max);
    internal static void Text(string? value) { if (!Fits(value, 16_384)) throw Invalid(); }
    internal static void Provenance(VocabularySemanticProvenance? value)
    {
        if (value is null || !Enum.IsDefined(value.Origin) || !Enum.IsDefined(value.Status)) throw Invalid(); Text(value.Source);
        if (value.Confidence is { } score && (!double.IsFinite(score) || score is < 0 or > 1)) throw Invalid();
    }
    internal static void Labels(IReadOnlyList<VocabularyMeaningLabel> labels)
    {
        if (labels is null || labels.Count is 0 or > 64 || labels.Any(x => x is null) || labels.Select(x => x.Id).Distinct().Count() != labels.Count) throw Invalid();
        var preferred = new HashSet<LearningLanguage?>();
        foreach (var label in labels)
            if (!Fits(label.Text, 4_096) || FoundationText.TrimWhitespaceAndNewlines(label.Text).Length == 0 || label.Language is { } l && !Enum.IsDefined(l)
                || label.Preferred && !preferred.Add(label.Language)) throw Invalid();
        if (preferred.Count == 0) throw Invalid();
    }
    internal static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
}
