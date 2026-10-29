using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyMeaningBrowserProjectionTests
{
    private sealed class Search : IVocabularyTextSearching { public bool Contains(string field, string term) => field.Contains(term, StringComparison.OrdinalIgnoreCase); }
    private static readonly Search Finder = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:x12}");
    private static R.VocabularyMeaningNode Node(int id, string title) => new(Id(id), [new(title)]);
    private static R.VocabularyEntry Entry(int id, int sense, string word, string meaning) => new(Id(id), word, word, LearningLanguage.English,
        VocabularyEntryKind.Word, [new R.VocabularySense(Id(sense), meaning, Now, Now)], Now, Now);
    private static readonly R.VocabularySemanticProvenance Proposed = new() { Status = R.VocabularySemanticStatus.Proposed };

    [Fact]
    public void MeaningProjectionPreservesConfirmedMappingsPendingCountsAndFilteredSenses()
    {
        var first = Entry(1, 2, "bank", "shore"); var second = Entry(3, 4, "bank", "institution"); var other = Entry(5, 6, "loose", "unused");
        var a = Node(10, "River"); var b = Node(11, "Finance"); var empty = Node(12, "Unlinked");
        var index = new R.VocabularySemanticIndex { Nodes = [a, b, empty], Mappings = [new(a.Id, Id(2)), new(a.Id, Id(2)), new(b.Id, Id(4)) { Provenance = Proposed }] };
        var items = VocabularyMeaningBrowserProjection.Items(index, [first, second, other], Finder, StringComparer.Ordinal);
        Assert.Equal(new[] { "Finance", "River", "Unlinked" }, items.Select(x => x.Title));
        Assert.Equal(new VocabularySelection(Id(1), Id(2)), Assert.Single(items[1].Selections));
        Assert.Empty(items[0].Selections); Assert.Equal(1, items[0].PendingCount);
        Assert.Equal(new[] { Id(4), Id(6) }, VocabularyMeaningBrowserProjection.Unclassified(index, [first, second, other]).Select(x => x.SenseId));
        Assert.Equal(new[] { "Finance", "River" }, VocabularyMeaningBrowserProjection.Items(index, [first, second], Finder, StringComparer.Ordinal, includeEmpty: false).Select(x => x.Title));
        Assert.Empty(VocabularyMeaningBrowserProjection.Items(index, [second], Finder, StringComparer.Ordinal, query: "shore", includeEmpty: false));
    }

    [Fact]
    public void SearchUsesConfirmedCuePatternAndMappingFieldsWithoutCreatingLinks()
    {
        var entry = Entry(1, 2, "postpone", "delay"); var node = Node(3, "Postponement");
        var cue = new R.VocabularyRetrievalCue("later please") { Id = Id(4) };
        var index = new R.VocabularySemanticIndex { Nodes = [node], Cues = [cue],
            CueTargets = [new(cue.Id, node.Id)], Mappings = [new(node.Id, Id(2)) { Conditions = "polite request" }],
            Patterns = [new(Id(2), "put off") { Note = "informal" }] };
        foreach (var query in new[] { "later please", "put off", "polite request", "postpone delay" })
            Assert.Single(VocabularyMeaningBrowserProjection.Items(index, [entry], Finder, StringComparer.Ordinal, query));
        var pendingOnly = index with { CueTargets = [new(cue.Id, node.Id) { Provenance = Proposed }],
            Mappings = [new(node.Id, Id(2)) { Conditions = "polite request", Provenance = Proposed }],
            Patterns = [new(Id(2), "put off") { Provenance = Proposed }] };
        foreach (var query in new[] { "later please", "put off", "polite request", "postpone delay" })
            Assert.Empty(VocabularyMeaningBrowserProjection.Items(pendingOnly, [entry], Finder, StringComparer.Ordinal, query));
        Assert.Single(VocabularyMeaningBrowserProjection.Unclassified(pendingOnly, [entry]));
    }

    [Fact]
    public void CategorySearchRetainsBothParentPathsAndDeduplicatesCounts()
    {
        var root = Category(1, "Root"); var left = Category(2, "Left"); var right = Category(3, "Right"); var child = Category(4, "Child");
        var node = Node(10, "Meaning");
        var index = new R.VocabularyClassificationIndex { Categories = [root, left, right, child], Parents = [
            new(Id(20), left.Id, root.Id), new(Id(21), right.Id, root.Id), new(Id(22), child.Id, left.Id), new(Id(23), child.Id, right.Id)],
            Memberships = [new(Id(30), node.Id, child.Id), new(Id(31), node.Id, left.Id)] };
        var rows = VocabularyClassificationProjection.Rows(index, [node], [], new HashSet<string>(), Finder, "Child");
        Assert.Equal(2, rows.Count(row => row.Category.Id == child.Id)); Assert.Equal(rows.Count, rows.Select(row => row.Id).Distinct().Count());
        Assert.Equal(1, rows[0].MeaningCount); Assert.Equal(new[] { 0, 1, 2, 1, 2 }, rows.Select(row => row.Depth));
        var collapsed = VocabularyClassificationProjection.Rows(index, [node], [], new HashSet<string>(), Finder);
        Assert.Single(collapsed); Assert.True(collapsed[0].HasChildren);
        var hidden = index with { Overrides = [new(left.Id) { IsHidden = true }] };
        Assert.DoesNotContain(VocabularyClassificationProjection.Rows(hidden, [node], [], new HashSet<string>(), Finder, "Child"), row => row.Category.Id == left.Id);
    }
    [Fact]
    public void BrowserItemsOwnSelectionsAndCompareAsValues()
    {
        var node = Node(1, "Café"); var selected = new List<VocabularySelection> { new(Id(2), Id(3)) };
        var first = new VocabularyMeaningBrowserProjection.Item(node, "Café", selected, 1);
        var second = new VocabularyMeaningBrowserProjection.Item(node, "Cafe\u0301", [new(Id(2), Id(3))], 1);
        selected.Clear(); Assert.Single(first.Selections); Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Throws<NotSupportedException>(() => ((IList<VocabularySelection>)first.Selections).Clear());
    }
    private static R.VocabularyCategory Category(int id, string label) => new(Id(id), R.VocabularyCategoryAxis.Content, [new(label)]);
}
