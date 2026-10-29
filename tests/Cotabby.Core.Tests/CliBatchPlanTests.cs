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
}
