using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;

namespace Cotabby.Core.Tests.Vocabulary;
public sealed class VocabularyFrozenEntrySearchTests
{
    private sealed class Search : IVocabularyTextSearching
    { public bool Contains(string field, string term) => field.Contains(term, StringComparison.OrdinalIgnoreCase); }
    [Fact]
    public void AllTermsMayMatchDifferentFieldsAndSenses()
    {
        var plan = VocabularyEntryImportPolicy.Plan([new(Expression: "bank", Meaning: "shore", Note: "water", Tags: ["nature"], Example: "river bank", ExampleTranslation: "river side"),
            new(Expression: "bank", Meaning: "finance")], new(), new SourceDate(42));
        var entry = plan.Document.CollectedEntries.Single(); var search = new Search();
        Assert.True(VocabularyFrozenEntrySearch.Matches(entry, "FINANCE river nature", search));
        Assert.True(VocabularyFrozenEntrySearch.Matches(entry, "", search));
        Assert.False(VocabularyFrozenEntrySearch.Matches(entry, "shore missing", search));
    }
}
