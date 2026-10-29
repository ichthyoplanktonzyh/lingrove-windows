using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Text;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Notebook;

/// <summary>Read-only semantic browser projection over the caller's collected entry snapshot.
/// Search and natural ordering are borrowed platform facts, never new semantic relationships.</summary>
public static class VocabularyMeaningBrowserProjection
{
    public sealed record Item
    {
        public Item(R.VocabularyMeaningNode node, string title, IReadOnlyList<VocabularySelection> selections, int pendingCount)
        { Node = node; Title = title; Selections = selections; PendingCount = pendingCount; }
        private IReadOnlyList<VocabularySelection> _selections = [];
        public R.VocabularyMeaningNode Node { get; }
        public Guid Id => Node.Id;
        public string Title { get; }
        public IReadOnlyList<VocabularySelection> Selections { get => _selections; init => _selections = CollectionValues.List(value); }
        public int PendingCount { get; }
        public bool Equals(Item? other) => other is not null && Node == other.Node && CollectionValues.Same(Title, other.Title)
            && CollectionValues.SequenceEqual(Selections, other.Selections) && PendingCount == other.PendingCount;
        public override int GetHashCode() => HashCode.Combine(Node, CollectionValues.StringHash(Title), CollectionValues.SequenceHash(Selections), PendingCount);
    }

    public static IReadOnlyList<Item> Items(R.VocabularySemanticIndex index, IReadOnlyList<R.VocabularyEntry> entries,
        IVocabularyTextSearching search, IComparer<string> titleComparer, string query = "", R.VocabularyMeaningNodeKind? kind = null,
        bool includeEmpty = true, IReadOnlyList<LearningLanguage>? preferredLanguages = null)
    {
        ArgumentNullException.ThrowIfNull(search); ArgumentNullException.ThrowIfNull(titleComparer);
        var senses = entries.SelectMany(entry => entry.Senses.Select(sense => (sense.Id, Selection: new VocabularySelection(entry.Id, sense.Id))))
            .ToDictionary(pair => pair.Id, pair => pair.Selection);
        var mappings = index.Mappings.Where(mapping => senses.ContainsKey(mapping.SenseId)).ToLookup(mapping => mapping.NodeId);
        var cueIds = index.Cues.Where(cue => Matches(cue.Text, query, search)).Select(cue => cue.Id).ToHashSet();
        var cueNodes = index.CueTargets.Where(target => target.Provenance.Status == R.VocabularySemanticStatus.Confirmed && cueIds.Contains(target.CueId)).Select(target => target.NodeId).ToHashSet();
        var matchingSenses = entries.SelectMany(entry => entry.Senses.Where(sense => MatchesSense(entry, sense, query, search)).Select(sense => sense.Id)).ToHashSet();
        var matchingPatterns = index.Patterns.Where(pattern => pattern.Provenance.Status == R.VocabularySemanticStatus.Confirmed
            && Matches(pattern.Template + " " + pattern.Note + " " + string.Join(" ", pattern.Collocations), query, search)).Select(pattern => pattern.SenseId).ToHashSet();
        var result = new List<Item>();
        foreach (var node in index.Nodes)
        {
            if (kind is not null && node.Kind != kind) continue;
            var links = mappings[node.Id].ToArray();
            if (!includeEmpty && links.Length == 0) continue;
            if (FoundationText.TrimWhitespaceAndNewlines(query).Length != 0
                && !node.Labels.Any(label => Matches(label.Text, query, search)) && !Matches(node.Definition, query, search)
                && !Matches(node.ScopeNote, query, search) && !cueNodes.Contains(node.Id)
                && !links.Any(link => link.Provenance.Status == R.VocabularySemanticStatus.Confirmed
                    && (matchingSenses.Contains(link.SenseId) || matchingPatterns.Contains(link.SenseId) || Matches(link.Conditions + " " + link.Nuance, query, search)))) continue;
            var seen = new HashSet<Guid>();
            var selected = links.Where(link => link.Provenance.Status == R.VocabularySemanticStatus.Confirmed && seen.Add(link.SenseId))
                .Select(link => senses[link.SenseId]).ToArray();
            result.Add(new(node, VocabularyMeaningLabelPolicy.Title(node, preferredLanguages ?? []), selected,
                links.Count(link => link.Provenance.Status == R.VocabularySemanticStatus.Proposed)));
        }
        return Array.AsReadOnly(result.OrderBy(item => item.Title, titleComparer).ToArray());
    }

    public static IReadOnlyList<VocabularySelection> Unclassified(R.VocabularySemanticIndex index, IReadOnlyList<R.VocabularyEntry> entries)
    {
        var linked = index.Mappings.Where(mapping => mapping.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(mapping => mapping.SenseId).ToHashSet();
        return Array.AsReadOnly(entries.SelectMany(entry => entry.Senses.Where(sense => !linked.Contains(sense.Id)).Select(sense => new VocabularySelection(entry.Id, sense.Id))).ToArray());
    }

    public static IReadOnlyList<R.VocabularyMeaningRelation> ConfirmedRelations(R.VocabularySemanticIndex index) =>
        Array.AsReadOnly(index.Relations.Where(relation => relation.Provenance.Status == R.VocabularySemanticStatus.Confirmed).ToArray());

    private static bool Matches(string field, string query, IVocabularyTextSearching search)
    {
        string term = FoundationText.TrimWhitespaceAndNewlines(query);
        return term.Length == 0 || search.Contains(field, term);
    }

    private static bool MatchesSense(R.VocabularyEntry entry, R.VocabularySense sense, string query, IVocabularyTextSearching search)
    {
        var fields = new List<string> { entry.Expression, entry.DictionaryForm, sense.Meaning, sense.Note };
        fields.AddRange(sense.Tags); fields.AddRange(sense.StandaloneMeanings.Select(m => m.Text)); fields.AddRange(sense.AlternateMeanings.Select(m => m.Text));
        foreach (var block in sense.Content ?? []) fields.Add(block.Content switch { VocabularyBlockContent.Text text => text.Value.Text, VocabularyBlockContent.Image image => image.Caption, _ => "" });
        foreach (var example in sense.Examples)
        {
            fields.Add(example.Text); fields.Add(example.Translation); fields.Add(example.Meaning);
            fields.AddRange(example.TranslationVariants.Select(m => m.Text));
        }
        return GraphemeString.Words(query).All(term => fields.Any(field => search.Contains(field, term)));
    }
}
