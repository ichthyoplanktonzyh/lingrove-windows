using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;
using Xunit.Abstractions;

namespace Cotabby.Core.Tests;

public sealed class CliLargeGraphTests(ITestOutputHelper output)
{
    private static (F.VocabularyDocument Document, CliBatch Batch) Corpus()
    {
        string evidence = new('z', 4096); var now = new F.SourceDate(1.25);
        var sources = Enumerable.Range(0, 5000).Select(_ => new F.VocabularySourceSnapshot(Guid.NewGuid(), evidence, R.VocabularySourceKind.Authored, null, now)).ToArray();
        var senses = new JsonArray();
        for (int s = 0; s < 10; s++)
        {
            var examples = new JsonArray(); for (int i = 0; i < 100; i++) examples.Add(new JsonObject { ["text"] = $"bank example {s}-{i}", ["completeness"] = "unknown" });
            senses.Add(new JsonObject { ["meaning"] = "meaning " + s, ["examples"] = examples });
        }
        var capture = new JsonObject { ["version"] = 1, ["idempotencyKey"] = "large-capture", ["defaults"] = new JsonObject { ["language"] = "en" },
            ["entries"] = new JsonArray(new JsonObject { ["expression"] = "bank", ["senses"] = senses }) };
        return (new F.VocabularyDocument { Sources = sources }, CliInputContract.DecodeCapture(Encoding.UTF8.GetBytes(capture.ToJsonString())));
    }
    [Fact]
    public void LargeRawGraphAndThousandExamplesRetainEvidence()
    {
        var (document, batch) = Corpus(); string fingerprint = CliIdentity.Fingerprint(batch);
        long allocated = GC.GetTotalAllocatedBytes(true); var watch = Stopwatch.StartNew();
        var result = CliBatchPlan.Create(batch, document, 0, fingerprint, new F.SourceDate(2.25), new Dictionary<string, VocabularyPreparedImage>());
        watch.Stop(); long delta = GC.GetTotalAllocatedBytes(true) - allocated;
        output.WriteLine($"20 MiB raw evidence, 1000 examples: elapsed={watch.Elapsed.TotalSeconds:F2}s allocated={delta / 1048576d:F1} MiB peakWorkingSet={Process.GetCurrentProcess().PeakWorkingSet64 / 1048576d:F1} MiB");
        Assert.Equal(6000, result.Document.Sources.Count); Assert.Equal(1000, result.Document.Interpretations.Count);
        Assert.Equal(1000, result.Document.Collections.Sum(value => value.InterpretationIds.Count));
        Assert.Equal(1.25, result.Document.Sources[0].EncounteredAt.ReferenceSeconds);
    }
    [Fact]
    public async Task LargeBatchCanBeCancelledBeforePublication()
    {
        var (document, batch) = Corpus(); string fingerprint = CliIdentity.Fingerprint(batch); using var cancellation = new CancellationTokenSource();
        var task = Task.Run(() => CliBatchPlan.Create(batch, document, 0, fingerprint, new F.SourceDate(2), new Dictionary<string, VocabularyPreparedImage>(), cancellation.Token));
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(5000, document.Sources.Count); Assert.Empty(document.Catalog);
    }
    [Fact]
    public void ManyOperationsReuseTheTypedGraph()
    {
        var (document, _) = Corpus(); var operations = new JsonArray();
        for (int index = 0; index < 100; index++)
            operations.Add(new JsonObject { ["add"] = new JsonObject { ["expression"] = "word" + index, ["language"] = "en", ["kind"] = "word", ["meaning"] = "meaning" } });
        var raw = new JsonObject { ["version"] = 1, ["idempotencyKey"] = "many-operations", ["operations"] = operations };
        var batch = CliInputContract.DecodeBatch(Encoding.UTF8.GetBytes(raw.ToJsonString())); var watch = Stopwatch.StartNew();
        long allocated = GC.GetTotalAllocatedBytes(true);
        var result = CliBatchPlan.Create(batch, document, 0, CliIdentity.Fingerprint(batch), new F.SourceDate(2), new Dictionary<string, VocabularyPreparedImage>());
        watch.Stop(); output.WriteLine($"20 MiB raw evidence, 100 operations: elapsed={watch.Elapsed.TotalSeconds:F2}s allocated={(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d:F1} MiB");
        Assert.Equal(100, result.Document.CollectedEntries.Count); Assert.Equal(5000, result.Document.Sources.Count);
    }
}
