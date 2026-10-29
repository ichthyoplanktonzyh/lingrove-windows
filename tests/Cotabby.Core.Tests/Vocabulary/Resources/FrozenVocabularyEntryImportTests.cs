using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests.Vocabulary.Resources;
public sealed class FrozenVocabularyEntryImportTests
{
    private static readonly F.SourceDate Time = new(814759445.12345672);
    [Fact]
    public void ImportCreatesThenReusesStableSelectionWithoutASecondChange()
    {
        var input = new VocabularyEntryInput(Expression: "hello", Meaning: "greeting");
        var first = F.VocabularyEntryImportPolicy.Plan([input], new(), Time);
        Assert.True(first.Result.HasChanges); Assert.Equal(1, first.Result.AddedEntries); Assert.Equal(1, first.Result.AddedSenses);
        F.VocabularyArchiveValidation.Validate(first.Document);
        var duplicate = F.VocabularyEntryImportPolicy.Plan([input], first.Document, Time);
        Assert.False(duplicate.Result.HasChanges); Assert.Equal(first.Result.Selection, duplicate.Result.Selection);
        Assert.Equal(first.Document, duplicate.Document);
    }
    [Fact]
    public void ExampleTranslationAndConflictRetainIdentity()
    {
        var input = new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello there");
        var first = F.VocabularyEntryImportPolicy.Plan([input], new(), Time);
        var next = F.VocabularyEntryImportPolicy.Plan([input with { ExampleTranslation = "hola" }], first.Document, Time);
        Assert.Equal(1, next.Result.AddedTranslations); Assert.Equal(first.Result.Selection, next.Result.Selection);
        Assert.Equal(first.Document.Interpretations[0].Id, next.Document.Interpretations[0].Id);
        F.VocabularyArchiveValidation.Validate(next.Document);
        var failure = Assert.Throws<VocabularyWordListException>(() => F.VocabularyEntryImportPolicy.Plan([input with { Meaning = "different" }], next.Document, Time));
        Assert.Equal(VocabularyWordListError.ExampleConflict, failure.Error);
    }
    [Fact]
    public void RecollectsExistingUncollectedKnowledgeWithoutRewritingEvidence()
    {
        var input = new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello there");
        var first = F.VocabularyEntryImportPolicy.Plan([input], new(), Time);
        var uncollected = first.Document with { Collections = [] };
        var result = F.VocabularyEntryImportPolicy.Plan([input], uncollected, Time);
        Assert.Equal(1, result.Result.ChangedCollections); Assert.Equal(first.Document.Sources, result.Document.Sources);
        Assert.Equal(first.Document.Interpretations[0].Id, result.Document.Interpretations[0].Id);
        F.VocabularyArchiveValidation.Validate(result.Document);
    }
    [Fact]
    public void BatchMatchesRepeatedExamplesAndKeepsAmbiguousContextsSeparate()
    {
        var grouped = new VocabularyEntryInput(Expression: "go", Meaning: "move", Example: "go home");
        var rows = new[] { grouped, grouped with { Example = "go away" }, grouped with { ExampleTranslation = "»Ø¼Ò" } };
        var result = F.VocabularyEntryImportPolicy.Plan(rows, new(), Time);
        Assert.Equal(1, result.Result.AddedSenses); Assert.Equal(2, result.Result.AddedExamples); Assert.Equal(1, result.Result.AddedTranslations);
        Assert.Equal(2, result.Document.CollectedEntries[0].Senses[0].Examples.Count);
        F.VocabularyArchiveValidation.Validate(result.Document);
        var ambiguous = F.VocabularyEntryImportPolicy.Plan([new(Expression: "go", Example: "go home"), new(Expression: "go", Example: "go away")], new(), Time);
        Assert.Equal(2, ambiguous.Result.AddedSenses); Assert.Equal(2, ambiguous.Result.AddedExamples);
        F.VocabularyArchiveValidation.Validate(ambiguous.Document);
    }

}
