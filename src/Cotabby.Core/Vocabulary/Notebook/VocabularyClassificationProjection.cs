using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Notebook;

/// <summary>Transient category paths distinguish multiparent rows; counts deduplicate meaning IDs.
/// This projection neither creates categories nor changes collected evidence.</summary>
public static class VocabularyClassificationProjection
{
    public sealed record Row(string Id, R.VocabularyCategory Category, string Title, int Depth, bool HasChildren, int MeaningCount);

    public static IReadOnlyList<Row> Rows(R.VocabularyClassificationIndex index, IReadOnlyList<R.VocabularyMeaningNode> nodes,
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
