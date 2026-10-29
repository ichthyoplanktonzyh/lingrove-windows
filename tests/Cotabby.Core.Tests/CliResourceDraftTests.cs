using System.Text.Json.Nodes;
using Cotabby.Core.CLI;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests;

public sealed class CliResourceDraftTests
{
    private static JsonObject Value(string text) => JsonNode.Parse(text)!.AsObject();
    [Fact]
    public void ResourceBatchCreatesLinkedRawEvidenceAndCollectionAtomically()
    {
        var entry = Guid.NewGuid(); var sense = Guid.NewGuid(); var source = Guid.NewGuid(); var occurrence = Guid.NewGuid(); var interpretation = Guid.NewGuid();
        var draft = new CliResourceDraft(new F.VocabularyDocument(), new F.SourceDate(123.25));
        draft.Put("entries", Value($$"""{"id":"{{entry:D}}","expression":"bank","kind":"word","language":"en"}"""));
        draft.Put("senses", Value($$"""{"id":"{{sense:D}}","entryID":"{{entry:D}}","meaning":"money"}"""));
        draft.Put("sources", Value($$"""{"id":"{{source:D}}","text":"bank opens","kind":"authored"}"""));
        draft.Put("occurrences", Value($$"""{"id":"{{occurrence:D}}","sourceID":"{{source:D}}","ranges":[{"location":0,"length":4}]}"""));
        draft.Put("interpretations", Value($$"""{"id":"{{interpretation:D}}","occurrenceID":"{{occurrence:D}}","senseID":"{{sense:D}}","status":"accepted"}"""));
        draft.Put("collections", Value($$"""{"senseID":"{{sense:D}}","interpretationIDs":["{{interpretation:D}}"],"isSavedWithoutContext":true}"""));
        var result = draft.Finish(); Assert.Equal(entry, result.Catalog.Single().Id); Assert.Equal(sense, result.CollectedEntries.Single().Senses.Single().Id);
        Assert.Equal(source, result.Sources.Single().Id); Assert.Equal(interpretation, result.Interpretations.Single().Id); Assert.Equal(123.25, result.Sources.Single().EncounteredAt.ReferenceSeconds);
    }
    [Fact]
    public void ImmutableSourcesConflictAndFinalValidationRejectsDanglingReferences()
    {
        var source = Guid.NewGuid(); var draft = new CliResourceDraft(new F.VocabularyDocument(), new F.SourceDate(12.5));
        draft.Put("sources", Value($$"""{"id":"{{source:D}}","text":"a","kind":"authored"}"""));
        Assert.Equal(source, draft.Put("sources", Value($$"""{"id":"{{source:D}}","text":"a"}""")));
        Assert.Equal(CliErrorCode.Conflict, Assert.Throws<CliException>(() => draft.Put("sources", Value($$"""{"id":"{{source:D}}","text":"b"}"""))).Code);
        draft.Put("occurrences", Value($$"""{"id":"{{Guid.NewGuid():D}}","sourceID":"{{Guid.NewGuid():D}}","ranges":[]}"""));
        Assert.Throws<CliException>(() => draft.Finish());
    }
    [Fact]
    public void EchoingUnixTimestampPreservesExactOriginalDouble()
    {
        var id = Guid.NewGuid(); var date = new F.SourceDate(0.123456789012345); var document = new F.VocabularyDocument { Sources = [new F.VocabularySourceSnapshot(id, "word", Cotabby.Core.Vocabulary.Resources.VocabularySourceKind.Authored, null, date)] };
        var draft = new CliResourceDraft(document, new F.SourceDate(0)); var publicRecord = CliResourceProjection.Records(document, "sources").Single();
        draft.Put("sources", publicRecord); var result = draft.Finish();
        Assert.Equal(BitConverter.DoubleToInt64Bits(date.ReferenceSeconds), BitConverter.DoubleToInt64Bits(result.Sources.Single().EncounteredAt.ReferenceSeconds));
    }

    [Fact]
    public void BuiltinCategoryAndItsEdgesRemainProtected()
    {
        var document = F.VocabularyClassificationInstaller.InstallBundled(new F.VocabularyDocument(), true).Document;
        var builtIn = document.Classification.Categories.First(value => value.FrameworkId is not null);
        var draft = new CliResourceDraft(document, new F.SourceDate(0));
        Assert.Equal(CliErrorCode.InvalidInput, Assert.Throws<CliException>(() => draft.Put("categories", Value($$"""{"id":"{{builtIn.Id:D}}","definition":"changed"}"""))).Code);
        Assert.Equal(CliErrorCode.InvalidInput, Assert.Throws<CliException>(() => draft.Delete("categories", builtIn.Id)).Code);
        var edge = document.Classification.Parents.First(); Assert.Throws<CliException>(() => draft.Delete("categoryParents", edge.Id));
        Assert.Equal(document, draft.Finish());
    }

}
