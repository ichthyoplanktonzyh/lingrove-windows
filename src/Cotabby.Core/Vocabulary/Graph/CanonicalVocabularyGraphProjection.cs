using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Notebook;
using F = Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary;

/// <summary>3b graph over the canonical collected graph. No legacy entry/date conversion or inferred edges.</summary>
public static class CanonicalVocabularyGraphProjection
{
    public static VocabularyGraphSnapshot Entries(IReadOnlyList<F.VocabularyEntry> entries, IReadOnlySet<Guid> expanded, string query, IVocabularyGraphTextSearching search, Func<F.VocabularySense, string> senseTitle, Func<LearningLanguage?, string> languageLabel)
    {
        query = LearningExplanationText.Trim(query); var nodes = new List<VocabularyGraphNode>(); var edges = new List<VocabularyGraphEdge>(); var matches = new HashSet<VocabularyGraphNodeId>(); bool mixed = VocabularyMeaningLabelPolicy.HasMultipleLanguages(entries);
        foreach (var entry in entries.OrderBy(x => x.Id.ToString("D"), StringComparer.Ordinal))
        {
            var parent = VocabularyGraphNodeId.Headword(entry.Id); nodes.Add(new(parent, VocabularyMeaningLabelPolicy.ExpressionTitle(entry, mixed, languageLabel)));
            bool headMatches = query.Length == 0 || search.HeadwordContains(entry.Expression, query) || search.HeadwordContains(entry.DictionaryForm, query); if (query.Length > 0 && headMatches) matches.Add(parent); if (!expanded.Contains(entry.Id) && headMatches) continue;
            foreach (var sense in entry.Senses) { var id = VocabularyGraphNodeId.Sense(new(entry.Id, sense.Id)); nodes.Add(new(id, senseTitle(sense))); edges.Add(new(parent, id)); if (query.Length > 0 && !headMatches && VocabularyFrozenEntrySearch.Matches(entry with { Senses = [sense] }, query, search)) matches.Add(id); }
        }
        return new() { Nodes = nodes, Edges = edges, HeadwordCount = entries.Count, Matches = matches, SearchQuery = query };
    }

    public static VocabularyGraphSnapshot Meanings(F.VocabularySemanticIndex index, IReadOnlyList<F.VocabularyEntry> entries, IReadOnlySet<Guid> expanded, string query, IVocabularyGraphTextSearching search, IComparer<string> titleComparer, IReadOnlyList<LearningLanguage> preferredLanguages, IReadOnlyList<string> interfaceLanguages, Func<F.VocabularySense, string> senseTitle, Func<LearningLanguage?, string> languageLabel, R.VocabularyClassificationIndex? classification = null, R.VocabularyMeaningNodeKind? kind = null, bool includeEmpty = true, bool showsUnexplored = false)
    {
        query = LearningExplanationText.Trim(query); var items = VocabularyMeaningBrowserProjection.Items(index, entries, search, titleComparer, query, kind, includeEmpty, preferredLanguages); var nodes = new List<VocabularyGraphNode>(); var edges = new List<VocabularyGraphEdge>(); var matches = new HashSet<VocabularyGraphNodeId>(); var included = new HashSet<VocabularyGraphNodeId>(); var visibleMeanings = new HashSet<Guid>(); var byEntry = entries.ToDictionary(e => e.Id); bool mixed = VocabularyMeaningLabelPolicy.HasMultipleLanguages(entries); int count = 0;
        void Append(VocabularyGraphNode node) { if (included.Add(node.Id)) nodes.Add(node); }
        void AppendMeanings(IEnumerable<VocabularyMeaningBrowserProjection.Item> values, VocabularyGraphNodeId? parent, R.VocabularyCategoryAxis axis, int depth)
        {
            foreach (var item in values)
            {
                var id = VocabularyGraphNodeId.Meaning(item.Id); bool isExpanded = expanded.Contains(item.Id) || query.Length != 0; visibleMeanings.Add(item.Id);
                if (included.Add(id)) { nodes.Add(new(id, item.Title) { MeaningKind = item.Node.Kind, Axis = axis, Depth = depth, ItemCount = item.Selections.Count, IsUnexplored = item.Selections.Count == 0, HasChildren = item.Selections.Count != 0, IsExpanded = isExpanded }); count++; if (query.Length != 0) matches.Add(id); }
                if (parent is { } parentId) edges.Add(new(parentId, id) { Kind = VocabularyGraphEdgeKind.Taxonomy });
                if (!isExpanded) continue;
                foreach (var selection in item.Selections)
                {
                    if (!byEntry.TryGetValue(selection.EntryId, out var entry) || entry.Senses.FirstOrDefault(s => s.Id == selection.SenseId) is not { } sense) continue;
                    var child = VocabularyGraphNodeId.Sense(selection); Append(new(child, VocabularyMeaningLabelPolicy.ExpressionTitle(entry, mixed, languageLabel) + " · " + senseTitle(sense)) { Axis = axis, Depth = depth + 1 });
                    edges.Add(new(id, child) { Kind = item.Node.Kind == R.VocabularyMeaningNodeKind.Concept ? VocabularyGraphEdgeKind.ExpressesContent : VocabularyGraphEdgeKind.RealizesFunction });
                }
            }
        }
        var directory = classification ?? new R.VocabularyClassificationIndex(); var active = directory.Categories.Where(c => !c.IsRetired).ToArray();
        if (active.Length == 0) foreach (var item in items) AppendMeanings([item], null, item.Node.Kind == R.VocabularyMeaningNodeKind.Function ? R.VocabularyCategoryAxis.Function : R.VocabularyCategoryAxis.Content, 0);
        else
        {
            var hidden = directory.Overrides.Where(o => o.IsHidden).Select(o => o.CategoryId).ToHashSet(); var categories = active.Where(c => !hidden.Contains(c.Id)).ToArray(); var byCategory = categories.ToDictionary(c => c.Id); var categoryIds = byCategory.Keys.ToHashSet(); var parent = new Dictionary<Guid, Guid>();
            foreach (var edge in directory.Parents.Where(e => categoryIds.Contains(e.CategoryId) && categoryIds.Contains(e.ParentId))) parent.TryAdd(edge.CategoryId, edge.ParentId);
            var children = parent.ToLookup(p => p.Value, p => byCategory[p.Key]); var itemById = items.ToDictionary(x => x.Id); var direct = directory.Memberships.Where(m => m.Provenance.Status == R.VocabularySemanticStatus.Confirmed && categoryIds.Contains(m.CategoryId) && itemById.ContainsKey(m.NodeId)).ToLookup(m => m.CategoryId, m => itemById[m.NodeId]);
            var categorized = directory.Memberships.Where(m => m.Provenance.Status == R.VocabularySemanticStatus.Confirmed && categoryIds.Contains(m.CategoryId) && itemById.ContainsKey(m.NodeId)).Select(m => m.NodeId).ToHashSet(); var matched = itemById.Keys.ToHashSet();
            var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); var matching = query.Length == 0 ? new HashSet<Guid>() : categories.Where(c => { var e = R.VocabularyClassificationPolicy.Effective(c, directory); return terms.All(t => e.Labels.Any(l => search.Contains(l.Text, t)) || search.Contains(e.Definition, t) || e.Localizations.Any(l => l.Name is { } n && search.Contains(n, t) || l.Definition is { } d && search.Contains(d, t))); }).Select(c => c.Id).ToHashSet();
            int SubtreeCount(Guid id) => R.VocabularyClassificationPolicy.MeaningIds(id, directory).Count(matched.Contains);
            bool CategoryMatch(Guid id) => R.VocabularyClassificationPolicy.Descendants(id, directory).Overlaps(matching);
            bool Visible(R.VocabularyCategory c) => showsUnexplored || SubtreeCount(c.Id) > 0 || CategoryMatch(c.Id) || expanded.Contains(c.Id);
            void Traverse(R.VocabularyCategory category, VocabularyGraphNodeId parentId, int depth, HashSet<Guid> ancestors)
            {
                if (ancestors.Contains(category.Id) || !Visible(category)) return; int amount = SubtreeCount(category.Id); bool directOrMatch = amount > 0 || CategoryMatch(category.Id); var childCategories = children[category.Id].Where(Visible).ToArray(); var directItems = direct[category.Id].OrderBy(x => x.Title, titleComparer).ToArray(); bool open = expanded.Contains(category.Id) || query.Length != 0 && directOrMatch || amount > 0 && depth <= 2;
                var id = VocabularyGraphNodeId.Category(category.Id); var title = R.VocabularyCategoryLocalizationPolicy.Resolve(R.VocabularyClassificationPolicy.Effective(category, directory), interfaceLanguages).Name; Append(new(id, title) { Axis = category.Axis, Depth = depth, ItemCount = amount, IsUnexplored = amount == 0, HasChildren = childCategories.Length != 0 || directItems.Length != 0, IsExpanded = open }); if (matching.Contains(category.Id)) matches.Add(id); edges.Add(new(parentId, id) { Kind = VocabularyGraphEdgeKind.Taxonomy, IsUnexplored = amount == 0 }); if (!open) return;
                var next = new HashSet<Guid>(ancestors) { category.Id }; foreach (var child in childCategories) Traverse(child, id, depth + 1, next); AppendMeanings(directItems, id, category.Axis, depth + 1);
            }
            foreach (var axis in Enum.GetValues<R.VocabularyCategoryAxis>())
            {
                if (kind is not null && (kind == R.VocabularyMeaningNodeKind.Function) != (axis == R.VocabularyCategoryAxis.Function)) continue; var roots = categories.Where(c => c.Axis == axis && !parent.ContainsKey(c.Id) && Visible(c)).ToArray(); var unclassified = items.Where(item => !categorized.Contains(item.Id) && (item.Node.Kind == R.VocabularyMeaningNodeKind.Function) == (axis == R.VocabularyCategoryAxis.Function)).ToArray(); if (roots.Length == 0 && unclassified.Length == 0) continue;
                var axisId = VocabularyGraphNodeId.Axis(axis); var axisIds = roots.SelectMany(root => R.VocabularyClassificationPolicy.MeaningIds(root.Id, directory).Where(matched.Contains)).ToHashSet(); int axisCount = axisIds.Count + unclassified.Length;
                Append(new(axisId, axis == R.VocabularyCategoryAxis.Content ? "Content meanings" : "Communicative functions") { Axis = axis, Depth = 0, ItemCount = axisCount, IsUnexplored = axisCount == 0, HasChildren = true, IsExpanded = true }); foreach (var root in roots) Traverse(root, axisId, 1, []); AppendMeanings(unclassified, axisId, axis, 1);
            }
        }
        foreach (var relation in VocabularyMeaningBrowserProjection.ConfirmedRelations(index).Where(r => visibleMeanings.Contains(r.SourceId) && visibleMeanings.Contains(r.TargetId))) edges.Add(new(VocabularyGraphNodeId.Meaning(relation.SourceId), VocabularyGraphNodeId.Meaning(relation.TargetId)) { Kind = relation.Kind switch { R.VocabularyMeaningRelationKind.Narrower => VocabularyGraphEdgeKind.Narrower, R.VocabularyMeaningRelationKind.Opposite => VocabularyGraphEdgeKind.Opposite, _ => VocabularyGraphEdgeKind.Related } });
        var positions = TreePositions(nodes, edges, active.Length == 0 ? 240 : 260, active.Length == 0 ? 52 : 54); return new() { Nodes = nodes, Edges = edges, HeadwordCount = count, Matches = matches, SearchQuery = query, IsMeaningGraph = true, TreePositions = positions };
    }

    private static IReadOnlyDictionary<VocabularyGraphNodeId, VocabularyGraphPoint> TreePositions(IReadOnlyList<VocabularyGraphNode> nodes, IReadOnlyList<VocabularyGraphEdge> edges, double column, double row)
    {
        var ids = nodes.Select(n => n.Id).ToHashSet(); var parents = new HashSet<VocabularyGraphNodeId>(); var children = new Dictionary<VocabularyGraphNodeId, List<VocabularyGraphNodeId>>();
        foreach (var edge in edges.Where(e => e.Kind is VocabularyGraphEdgeKind.Taxonomy or VocabularyGraphEdgeKind.ExpressesContent or VocabularyGraphEdgeKind.RealizesFunction or VocabularyGraphEdgeKind.Ownership)) if (ids.Contains(edge.Source) && ids.Contains(edge.Target) && parents.Add(edge.Target)) { if (!children.TryGetValue(edge.Source, out var list)) children[edge.Source] = list = []; list.Add(edge.Target); }
        var positions = new Dictionary<VocabularyGraphNodeId, VocabularyGraphPoint>(); var visited = new HashSet<VocabularyGraphNodeId>(); double nextY = 0;
        double Layout(VocabularyGraphNodeId id, int depth) { if (!visited.Add(id)) return positions.TryGetValue(id, out var prior) ? prior.Y : nextY; var list = children.GetValueOrDefault(id, []).Where(c => !visited.Contains(c)).ToArray(); if (list.Length == 0) { double y = nextY; nextY += row; positions[id] = new(depth * column, y); return y; } var ys = list.Select(child => Layout(child, depth + 1)).ToArray(); double middle = (ys[0] + ys[^1]) / 2; positions[id] = new(depth * column, middle); return middle; }
        var roots = nodes.Where(n => !parents.Contains(n.Id)).ToArray(); for (int i = 0; i < roots.Length; i++) { if (i > 0) nextY += row * .85; Layout(roots[i].Id, 0); }
        foreach (var node in nodes.Where(n => !positions.ContainsKey(n.Id))) { positions[node.Id] = new(node.Depth * column, nextY); nextY += row; }
        if (positions.Count != 0) { double cx = (positions.Values.Min(p => p.X) + positions.Values.Max(p => p.X)) / 2, cy = (positions.Values.Min(p => p.Y) + positions.Values.Max(p => p.Y)) / 2; foreach (var id in positions.Keys.ToArray()) positions[id] = new(positions[id].X - cx, positions[id].Y - cy); } return positions;
    }
}
