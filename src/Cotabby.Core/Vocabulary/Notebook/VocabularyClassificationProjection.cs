using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Vocabulary.Notebook;

/// <summary>Transient category paths distinguish multiparent rows; counts deduplicate meaning IDs.
/// This projection neither creates categories nor changes collected evidence.</summary>
public static class VocabularyClassificationProjection
{
    public sealed record Row(string Id, R.VocabularyCategory Category, string Title, int Depth, bool HasChildren, int MeaningCount);
    public sealed record CurrentRow(string Id, R.VocabularyCategory Category, string Title, int Depth, bool HasChildren, int MeaningCount, IReadOnlyList<string> Breadcrumb, bool IsHidden);
    public sealed record OutlineRow(string Id, R.VocabularyCategoryAxis Axis, R.VocabularyCategory? Category, VocabularyMeaningBrowserProjection.Item? Meaning, string Title, string Subtitle, int Depth, bool HasChildren, int ItemCount, int PendingCount, IReadOnlyList<string> Breadcrumb);
    public static IReadOnlyList<R.VocabularyCategory> Breadcrumb(Guid id, R.VocabularyClassificationIndex index)
    {
        var byId = index.Categories.ToDictionary(x => x.Id); if (!byId.TryGetValue(id, out var target)) return []; var hidden = index.Overrides.Where(x => x.IsHidden).Select(x => x.CategoryId).ToHashSet(); var parents = index.Parents.ToLookup(x => x.CategoryId); var chain = new List<R.VocabularyCategory> { target }; var seen = new HashSet<Guid> { id };
        while (true) { var candidates = parents[chain[^1].Id].Where(x => byId.ContainsKey(x.ParentId)).Select(x => byId[x.ParentId]).Where(x => !x.IsRetired).ToArray(); var parent = candidates.FirstOrDefault(x => !hidden.Contains(x.Id)) ?? candidates.FirstOrDefault(); if (parent is null || !seen.Add(parent.Id)) break; chain.Add(parent); } chain.Reverse(); return chain;
    }
    public static IReadOnlyList<OutlineRow> Outline(R.VocabularyCategoryAxis axis, R.VocabularyClassificationIndex classification, IReadOnlyList<VocabularyMeaningBrowserProjection.Item> items, IReadOnlyList<F.VocabularyEntry> entries, IReadOnlySet<string> expanded, IVocabularyTextSearching search, IReadOnlyList<string> interfaceLanguages, string query = "", bool showsUnexplored = false, IReadOnlySet<Guid>? revealed = null)
    {
        var rows = CurrentRows(classification, items.Select(x => x.Node).ToArray(), expanded, search, interfaceLanguages, query).Where(x => x.Category.Axis == axis); var itemsById = items.ToDictionary(x => x.Id); var memberships = classification.Memberships.Where(x => x.Provenance.Status == R.VocabularySemanticStatus.Confirmed).ToLookup(x => x.CategoryId); var entriesById = entries.ToDictionary(x => x.Id); var result = new List<OutlineRow>();
        foreach (var row in rows)
        {
            var meanings = memberships[row.Category.Id].Where(m => itemsById.ContainsKey(m.NodeId)).Select(m => itemsById[m.NodeId]).OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            if (!showsUnexplored && string.IsNullOrWhiteSpace(query) && row.MeaningCount == 0 && revealed?.Contains(row.Category.Id) != true) continue;
            result.Add(new(row.Id, axis, row.Category, null, row.Title, R.VocabularyCategoryLocalizationPolicy.Resolve(R.VocabularyClassificationPolicy.Effective(row.Category, classification), interfaceLanguages).Definition, row.Depth, row.HasChildren || meanings.Length > 0, row.MeaningCount, 0, row.Breadcrumb));
            if (!expanded.Contains(row.Id) && string.IsNullOrWhiteSpace(query)) continue;
            foreach (var item in meanings)
            {
                var names = item.Selections.Where(s => entriesById.ContainsKey(s.EntryId)).Select(s => entriesById[s.EntryId].Expression).Distinct(StringComparer.CurrentCulture).ToArray();
                result.Add(new(row.Id + "/meaning/" + item.Id.ToString("D").ToUpperInvariant(), axis, null, item, item.Title, names.Length == 0 ? item.Node.Definition : string.Join(" · ", names), row.Depth + 1, false, item.Selections.Count, item.PendingCount, row.Breadcrumb.Append(item.Title).ToArray()));
            }
        }
        return result;
    }
    public static IReadOnlyList<VocabularyMeaningBrowserProjection.Item> Uncategorized(R.VocabularyClassificationIndex classification, IReadOnlyList<VocabularyMeaningBrowserProjection.Item> items)
    { var ids = classification.Memberships.Where(x => x.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(x => x.NodeId).ToHashSet(); return items.Where(item => !ids.Contains(item.Id)).ToArray(); }
    public static (int Explored, int Total) AxisCoverage(R.VocabularyCategoryAxis axis, R.VocabularyClassificationIndex index, IReadOnlyList<F.VocabularyMeaningNode> nodes)
    {
        var hidden = index.Overrides.Where(x => x.IsHidden).Select(x => x.CategoryId).ToHashSet(); var categories = index.Categories.Where(x => x.Axis == axis && !x.IsRetired && !hidden.Contains(x.Id)).ToArray(); var visible = categories.Select(x => x.Id).ToHashSet(); var parented = index.Parents.Where(x => visible.Contains(x.CategoryId) && visible.Contains(x.ParentId)).Select(x => x.CategoryId).ToHashSet(); var roots = categories.Where(x => !parented.Contains(x.Id)).ToArray(); var ids = nodes.Select(x => x.Id).ToHashSet(); return (roots.Count(x => R.VocabularyClassificationPolicy.MeaningIds(x.Id, index).Overlaps(ids)), roots.Length);
    }
    public static IReadOnlySet<string> ExpansionPaths(IReadOnlySet<Guid> ids, R.VocabularyClassificationIndex index, bool includeHidden = false, bool includeTarget = false)
    {
        var hidden = index.Overrides.Where(x => x.IsHidden).Select(x => x.CategoryId).ToHashSet(); var visible = index.Categories.Where(x => !x.IsRetired && (includeHidden || !hidden.Contains(x.Id))).Select(x => x.Id).ToHashSet(); var parents = index.Parents.Where(x => visible.Contains(x.ParentId)).ToLookup(x => x.CategoryId); var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids.Where(visible.Contains)) { var chain = new List<Guid> { id }; var seen = new HashSet<Guid> { id }; while (parents[chain[^1]].FirstOrDefault() is { } edge && seen.Add(edge.ParentId)) chain.Add(edge.ParentId); string key = ""; var reversed = chain.AsEnumerable().Reverse(); foreach (var parent in includeTarget ? reversed : reversed.SkipLast(1)) { key += "/" + parent.ToString("D").ToUpperInvariant(); result.Add(key); } } return result;
    }

    public static IReadOnlyList<CurrentRow> CurrentRows(R.VocabularyClassificationIndex index, IReadOnlyList<F.VocabularyMeaningNode> nodes,
        IReadOnlySet<string> expanded, IVocabularyTextSearching search, IReadOnlyList<string> interfaceLanguages, string query = "", bool includeHidden = false, IReadOnlySet<Guid>? excludedIds = null)
    {
        var hidden = index.Overrides.Where(x => x.IsHidden).Select(x => x.CategoryId).ToHashSet();
        var categories = index.Categories.Where(x => !x.IsRetired && (excludedIds is null || !excludedIds.Contains(x.Id)) && (includeHidden || !hidden.Contains(x.Id))).ToArray();
        var visible = categories.Select(x => x.Id).ToHashSet(); var nodeIds = nodes.Select(x => x.Id).ToHashSet(); var edges = index.Parents.Where(x => visible.Contains(x.CategoryId) && visible.Contains(x.ParentId)).ToArray();
        var children = edges.ToLookup(x => x.ParentId); var parented = edges.Select(x => x.CategoryId).ToHashSet(); var byId = categories.ToDictionary(x => x.Id); var terms = GraphemeString.Words(query);
        var matches = categories.Where(category => { var value = R.VocabularyClassificationPolicy.Effective(category, index); return terms.All(term => value.Labels.Any(x => search.Contains(x.Text, term)) || search.Contains(value.Definition, term) || value.Localizations.Any(x => x.Name is { } n && search.Contains(n, term) || x.Definition is { } d && search.Contains(d, term))); }).Select(x => x.Id).ToHashSet();
        if (terms.Count != 0) { var parents = edges.ToLookup(x => x.CategoryId); var queue = new Queue<Guid>(matches); while (queue.TryDequeue(out var id)) foreach (var edge in parents[id]) if (matches.Add(edge.ParentId)) queue.Enqueue(edge.ParentId); }
        var rows = new List<CurrentRow>(); var stack = new Stack<(Guid Id, string ParentPath, int Depth, HashSet<Guid> Ancestors, IReadOnlyList<string> Trail)>();
        foreach (var category in categories.Where(x => !parented.Contains(x.Id)).Reverse()) stack.Push((category.Id, "", 0, [], []));
        while (stack.TryPop(out var current) && rows.Count < 10_000)
        {
            if (!byId.TryGetValue(current.Id, out var category) || current.Ancestors.Contains(current.Id) || terms.Count != 0 && !matches.Contains(current.Id)) continue;
            string path = current.ParentPath + "/" + current.Id.ToString("D").ToUpperInvariant(); var childIds = children[current.Id].Select(x => x.CategoryId).ToArray();
            string title = R.VocabularyCategoryLocalizationPolicy.Resolve(R.VocabularyClassificationPolicy.Effective(category, index), interfaceLanguages).Name; var trail = current.Trail.Append(title).ToArray();
            rows.Add(new(path, category, title, current.Depth, childIds.Length != 0, R.VocabularyClassificationPolicy.MeaningIds(current.Id, index).Count(nodeIds.Contains), trail, hidden.Contains(current.Id)));
            if (expanded.Contains(path) || terms.Count != 0) { var ancestors = new HashSet<Guid>(current.Ancestors) { current.Id }; foreach (var child in childIds.Reverse()) stack.Push((child, path, current.Depth + 1, ancestors, trail)); }
        }
        return rows;
    }

    public static IReadOnlyList<Row> Rows(R.VocabularyClassificationIndex index, IReadOnlyList<F.VocabularyMeaningNode> nodes,
        IReadOnlyList<LearningLanguage> languages, IReadOnlySet<string> expanded, IVocabularyTextSearching search, string query = "")
    {
        ArgumentNullException.ThrowIfNull(search);
        var hidden = index.Overrides.Where(value => value.IsHidden).Select(value => value.CategoryId).ToHashSet();
        var categories = index.Categories.Where(category => !category.IsRetired && !hidden.Contains(category.Id)).ToArray();
        var visible = categories.Select(category => category.Id).ToHashSet(); var nodeIds = nodes.Select(node => node.Id).ToHashSet();
        var edges = index.Parents.Where(edge => visible.Contains(edge.CategoryId) && visible.Contains(edge.ParentId)).ToArray();
        var children = edges.ToLookup(edge => edge.ParentId); var parented = edges.Select(edge => edge.CategoryId).ToHashSet();
        var byId = categories.ToDictionary(category => category.Id); var terms = GraphemeString.Words(query);
        var matches = categories.Where(category =>
        {
            var effective = R.VocabularyClassificationPolicy.Effective(category, index);
            return terms.All(term => effective.Labels.Any(label => search.Contains(label.Text, term)) || search.Contains(effective.Definition, term));
        }).Select(category => category.Id).ToHashSet();
        if (terms.Count != 0)
        {
            var parents = edges.ToLookup(edge => edge.CategoryId); var queue = new Queue<Guid>(matches);
            while (queue.TryDequeue(out var id)) foreach (var edge in parents[id]) if (matches.Add(edge.ParentId)) queue.Enqueue(edge.ParentId);
        }
        var rows = new List<Row>();
        var stack = new Stack<(Guid Id, string ParentPath, int Depth, HashSet<Guid> Ancestors)>();
        foreach (var category in categories.Where(category => !parented.Contains(category.Id)).Reverse()) stack.Push((category.Id, "", 0, []));
        while (stack.TryPop(out var current) && rows.Count < 10_000)
        {
            if (!byId.TryGetValue(current.Id, out var category) || current.Ancestors.Contains(current.Id) || terms.Count != 0 && !matches.Contains(current.Id)) continue;
            string path = current.ParentPath + "/" + current.Id.ToString("D").ToUpperInvariant();
            var childIds = children[current.Id].Select(edge => edge.CategoryId).ToArray();
            int count = R.VocabularyClassificationPolicy.MeaningIds(current.Id, index).Count(nodeIds.Contains);
            rows.Add(new(path, category, R.VocabularyClassificationPolicy.Title(category, index, languages), current.Depth, childIds.Length != 0, count));
            if (expanded.Contains(path) || terms.Count != 0)
            {
                var ancestors = new HashSet<Guid>(current.Ancestors) { current.Id };
                foreach (Guid child in childIds.Reverse()) stack.Push((child, path, current.Depth + 1, ancestors));
            }
        }
        return Array.AsReadOnly(rows.ToArray());
    }
}
