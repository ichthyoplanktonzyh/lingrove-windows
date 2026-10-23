using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Vocabulary;

/// <summary>Graph headword search is case-insensitive, while a sense's full notebook search is
/// case/diacritic-insensitive. The Windows locale adapter owns these two distinct string facts.</summary>
public interface IVocabularyGraphTextSearching : IVocabularyTextSearching
{
    bool HeadwordContains(string field, string query);
}

/// <summary>Original filtered-notebook-to-graph projection: stable UUID order, headword dots and
/// explicitly owned sense branches. Search can reveal matching senses; proximity or shared labels
/// never create edges. This rule owns no window, data store, simulation or linguistic provider.</summary>
public static class VocabularyGraphProjection
{
    public static VocabularyGraphSnapshot Make(IReadOnlyList<VocabularyEntry> entries, IReadOnlySet<Guid> expanded, string query, IVocabularyGraphTextSearching textSearch)
    {
        query = LearningExplanationText.Trim(query);
        var nodes = new List<VocabularyGraphNode>(); var edges = new List<VocabularyGraphEdge>(); var matches = new HashSet<VocabularyGraphNodeId>();
        foreach (var entry in entries.OrderBy(entry => entry.Id.ToString("D"), StringComparer.Ordinal))
        {
            var parent = VocabularyGraphNodeId.Headword(entry.Id); nodes.Add(new(parent, entry.Expression));
            bool headMatches = query.Length == 0 || textSearch.HeadwordContains(entry.Expression, query) || textSearch.HeadwordContains(entry.DictionaryForm, query);
            if (query.Length > 0 && headMatches) matches.Add(parent);
            if (!expanded.Contains(entry.Id) && headMatches) continue;
            foreach (var sense in entry.Senses)
            {
                var id = VocabularyGraphNodeId.Sense(new(entry.Id, sense.Id)); nodes.Add(new(id, VocabularyNotebookPolicy.DisplayTitle(sense))); edges.Add(new(parent, id));
                if (query.Length > 0 && !headMatches && VocabularyNotebookPolicy.Matches(entry with { Senses = [sense] }, query, textSearch)) matches.Add(id);
            }
        }
        return new() { Nodes = nodes, Edges = edges, HeadwordCount = entries.Count, Matches = matches, SearchQuery = query };
    }
}
