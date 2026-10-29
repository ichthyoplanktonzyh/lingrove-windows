using System.Text;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests;

public sealed class CliBatchPlanTests
{
    private static CliBatch Decode(string json) => CliInputContract.DecodeBatch(Encoding.UTF8.GetBytes(json));
    [Fact]
    public void TraditionalAddRetainsSourceIdentityAndUnknownCompleteness()
    {
        var batch = Decode("""{"version":1,"idempotencyKey":"add","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"money","examples":[{"text":"the bank opens","completeness":"unknown"}]}}]}""");
        var result = CliBatchPlan.Create(batch, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(batch), new F.SourceDate(12.25), new Dictionary<string, VocabularyPreparedImage>());
        var example = result.Document.CollectedEntries.Single().Senses.Single().Examples.Single();
        Assert.Equal(R.VocabularyExampleCompleteness.Unknown, example.Completeness); Assert.Equal(new VocabularyTextRange(4, 4), example.Ranges.Single());
        Assert.Equal(example.Id, result.Document.Sources.Single().Id); Assert.Equal(example.Id, result.Document.Occurrences.Single().Id);
        Assert.Equal(12.25, result.Document.Sources.Single().EncounteredAt.ReferenceSeconds);
    }
    [Fact]
    public void OverlappingAmbiguityRejectsWholeBatch()
    {
        var batch = Decode("""{"version":1,"idempotencyKey":"add","operations":[{"add":{"expression":"ana","language":"en","kind":"word","meaning":"x","examples":[{"text":"banana"}]}}]}""");
        Assert.Equal(CliErrorCode.InvalidInput, Assert.Throws<CliException>(() => CliBatchPlan.Create(batch, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(batch), new F.SourceDate(0), new Dictionary<string, VocabularyPreparedImage>())).Code);
    }
    [Fact]
    public void TraditionalOperationKeepsTemporarilyUnresolvedRawEvidence()
    {
        var source = Guid.NewGuid(); var occurrence = Guid.NewGuid();
        string json = """{"version":2,"idempotencyKey":"mixed","expectedRevision":0,"operations":[{"put":{"resource":"occurrences","value":{"id":"OCC","sourceID":"SRC","ranges":[]}}},{"add":{"expression":"bank","language":"en","kind":"word","meaning":"money"}},{"put":{"resource":"sources","value":{"id":"SRC","text":"raw","kind":"authored"}}}]}""".Replace("OCC", occurrence.ToString("D")).Replace("SRC", source.ToString("D"));
        var batch = Decode(json);
        var result = CliBatchPlan.Create(batch, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(batch), new F.SourceDate(3.5), new Dictionary<string, VocabularyPreparedImage>());
        Assert.Equal(source, result.Document.Sources.Single().Id);
        Assert.Equal(occurrence, result.Document.Occurrences.Single().Id);
        Assert.Equal("bank", result.Document.CollectedEntries.Single().Expression);
    }
    [Fact]
    public void CaptureKeepsProposedGraphClaimsAndRawUnknownEvidence()
    {
        var batch = CliInputContract.DecodeCapture(Encoding.UTF8.GetBytes("""{"version":1,"idempotencyKey":"capture","defaults":{"language":"en","status":"proposed"},"meanings":[{"ref":"delay","title":"delay","cues":["later"]}],"entries":[{"expression":"defer","senses":[{"meaning":"delay","examples":[{"text":"we defer it","completeness":"unknown"}],"associations":[{"meaning":"delay","usage":"defer + noun"}]}]}]}"""));
        var result = CliBatchPlan.Create(batch, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(batch), new F.SourceDate(10.25), new Dictionary<string, VocabularyPreparedImage>());
        Assert.Equal(R.VocabularySemanticStatus.Proposed, result.Document.Semantics.Nodes.Single().Provenance.Status);
        Assert.Equal(R.VocabularySemanticStatus.Proposed, result.Document.Semantics.Mappings.Single().Provenance.Status);
        Assert.Equal(R.VocabularySemanticStatus.Proposed, result.Document.Semantics.Patterns.Single().Provenance.Status);
        Assert.Equal(R.VocabularyExampleCompleteness.Unknown, result.Document.Interpretations.Single().Completeness);
        Assert.Equal(result.Document.Interpretations.Single().Id, result.Document.Collections.Single().InterpretationIds.Single());
        Assert.Equal(10.25, result.Document.Sources.Single().EncounteredAt.ReferenceSeconds);
    }
    [Theory]
    [InlineData("""{"defaults":{"language":"en"},"entries":[{"expression":"bank","senses":[{"meaning":"money","associations":[{"meaning":"missing"}]}]}]}""")]
    [InlineData("""{"defaults":{"language":"en"},"meanings":[{"ref":"x","title":"A"},{"ref":"x","title":"B"}],"entries":[{"expression":"bank","senses":[{"meaning":"money"}]}]}""")]
    [InlineData("""{"defaults":{"language":"en"},"entries":[{"expression":"bank","senses":[{"meaning":"money","examples":[{"text":"bank bank"}]}]}]}""")]
    [InlineData("""{"defaults":{"language":"en"},"meanings":[{"ref":"x","title":"A"}],"relations":[{"from":"x","to":"x","kind":"narrower"}],"entries":[{"expression":"bank","senses":[{"meaning":"money"}]}]}""")]
    public void InvalidCaptureDoesNotChangeBaseline(string material)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(material)!.AsObject(); json["version"] = 1; json["idempotencyKey"] = "invalid";
        var batch = CliInputContract.DecodeCapture(Encoding.UTF8.GetBytes(json.ToJsonString())); var baseline = new F.VocabularyDocument();
        Assert.Throws<CliException>(() => CliBatchPlan.Create(batch, baseline, 0, CliIdentity.Fingerprint(batch), new F.SourceDate(0), new Dictionary<string, VocabularyPreparedImage>()));
        Assert.Empty(baseline.Catalog); Assert.Empty(baseline.Sources);
    }
    [Fact]
    public void UnsignedRevisionOutsideStoredRangeIsAConflictRatherThanRounding()
    {
        var batch = Decode("""{"version":1,"idempotencyKey":"future","expectedRevision":18446744073709551615,"operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"money"}}]}""");
        var error = Assert.Throws<CliException>(() => CliBatchPlan.Create(batch, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(batch), new F.SourceDate(0), new Dictionary<string, VocabularyPreparedImage>()));
        Assert.Equal(CliErrorCode.Conflict, error.Code);
    }
    [Fact]
    public void AppendReusesExactEvidenceAndRetainsTranslationVariant()
    {
        var first = Decode("""{"version":1,"idempotencyKey":"first","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"money","examples":[{"text":"the bank","translation":"one"}]}}]}""");
        var saved = CliBatchPlan.Create(first, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(first), new F.SourceDate(1), new Dictionary<string, VocabularyPreparedImage>()).Document;
        var entry = saved.CollectedEntries.Single(); var sense = entry.Senses.Single(); var prior = sense.Examples.Single();
        string append = """{"version":1,"idempotencyKey":"second","operations":[{"append":{"entryID":"ENTRY","senseID":"SENSE","examples":[{"text":"the bank","translation":"two"}]}}]}""".Replace("ENTRY", entry.Id.ToString("D")).Replace("SENSE", sense.Id.ToString("D"));
        var second = Decode(append); var result = CliBatchPlan.Create(second, saved, 1, CliIdentity.Fingerprint(second), new F.SourceDate(2), new Dictionary<string, VocabularyPreparedImage>()).Document;
        var after = result.CollectedEntries.Single().Senses.Single().Examples.Single();
        Assert.Equal(prior.Id, after.Id); Assert.Equal(prior.SourceId, after.SourceId); Assert.Equal("one", after.Translation);
        Assert.Equal("two", after.TranslationVariants.Single().Text); Assert.Single(result.Sources); Assert.Single(result.Occurrences);
    }
    [Fact]
    public void CaptureCannotStealAnExistingSenseUsage()
    {
        var first = Decode("""{"version":1,"idempotencyKey":"first","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"money","examples":[{"text":"the bank"}]}}]}""");
        var saved = CliBatchPlan.Create(first, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(first), new F.SourceDate(1), new Dictionary<string, VocabularyPreparedImage>()).Document;
        var capture = CliInputContract.DecodeCapture(Encoding.UTF8.GetBytes("""{"version":1,"idempotencyKey":"second","defaults":{"language":"en"},"entries":[{"expression":"bank","senses":[{"meaning":"river","examples":[{"text":"the bank"}]}]}]}"""));
        var error = Assert.Throws<CliException>(() => CliBatchPlan.Create(capture, saved, 1, CliIdentity.Fingerprint(capture), new F.SourceDate(2), new Dictionary<string, VocabularyPreparedImage>()));
        Assert.Equal(CliErrorCode.Conflict, error.Code); Assert.Single(saved.CollectedEntries.Single().Senses);
    }
    [Fact]
    public void CancelledPlanningNeverPublishesAnIntermediateGraph()
    {
        var batch = Decode("""{"version":1,"idempotencyKey":"cancel","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"money"}}]}""");
        var baseline = new F.VocabularyDocument(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => CliBatchPlan.Create(batch, baseline, 0, CliIdentity.Fingerprint(batch), new F.SourceDate(0), new Dictionary<string, VocabularyPreparedImage>(), cancellation.Token));
        Assert.Empty(baseline.Catalog);
    }
    [Fact]
    public void MixedResourceCaptureAndAppendSeeIncrementalTypedChanges()
    {
        var entry = Guid.NewGuid(); var sense = Guid.NewGuid();
        string json = """{"version":2,"idempotencyKey":"mixed-index","expectedRevision":0,"operations":[{"put":{"resource":"entries","value":{"id":"ENTRY","expression":"bank","language":"en","kind":"word"}}},{"put":{"resource":"senses","value":{"id":"SENSE","entryID":"ENTRY","meaning":"first"}}},{"capture":{"entries":[{"expression":"bank","language":"en","senses":[{"id":"SENSE","examples":[{"text":"the bank opens"}]}]}]}},{"put":{"resource":"senses","value":{"id":"SENSE","meaning":"edited"}}},{"append":{"entryID":"ENTRY","senseID":"SENSE","examples":[{"text":"the bank closes"}]}}]}""".Replace("ENTRY", entry.ToString("D")).Replace("SENSE", sense.ToString("D"));
        var batch = Decode(json); var result = CliBatchPlan.Create(batch, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(batch), new F.SourceDate(3), new Dictionary<string, VocabularyPreparedImage>()).Document;
        var saved = result.CollectedEntries.Single().Senses.Single(); Assert.Equal(sense, saved.Id); Assert.Equal("edited", saved.Meaning);
        Assert.Equal(2, saved.Examples.Count); Assert.Equal(2, result.Sources.Count); Assert.Equal(2, result.Interpretations.Count);
    }
    [Fact]
    public void CaptureCanReferenceCategoryCreatedEarlierInSameBatch()
    {
        var category = Guid.NewGuid(); var label = Guid.NewGuid();
        string json = """{"version":2,"idempotencyKey":"category","expectedRevision":0,"operations":[{"put":{"resource":"categories","value":{"id":"CATEGORY","axis":"content","labels":[{"id":"LABEL","text":"custom"}]}}},{"capture":{"meanings":[{"ref":"m","title":"meaning","categories":["CATEGORY"]}],"entries":[{"expression":"bank","language":"en","senses":[{"meaning":"money","associations":[{"meaning":"m"}]}]}]}}]}""".Replace("CATEGORY", category.ToString("D")).Replace("LABEL", label.ToString("D"));
        var batch = Decode(json); var result = CliBatchPlan.Create(batch, new F.VocabularyDocument(), 0, CliIdentity.Fingerprint(batch), new F.SourceDate(3), new Dictionary<string, VocabularyPreparedImage>()).Document;
        Assert.Equal(category, result.Classification.Categories.Single().Id); Assert.Equal(category, result.Classification.Memberships.Single().CategoryId);
        Assert.Equal(result.Semantics.Nodes.Single().Id, result.Classification.Memberships.Single().NodeId);
    }
}
