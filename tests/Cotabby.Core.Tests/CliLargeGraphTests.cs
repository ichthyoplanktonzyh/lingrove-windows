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
        var (document, capture) = Corpus();
        var input = capture.CopyValue();
        input["operations"]!.AsArray().Insert(0, new JsonObject
        {
            ["add"] = new JsonObject { ["expression"] = "first", ["language"] = "en", ["kind"] = "word", ["meaning"] = "initial" }
        });
        var batch = CliInputContract.DecodeBatch(Encoding.UTF8.GetBytes(input.ToJsonString()));
        string fingerprint = CliIdentity.Fingerprint(batch);
        using var cancellation = new CancellationTokenSource();
        // Enumerating prepared media happens after the first add has been planned. Cancel at
        // this real collaborator boundary, before the remaining thousand-example capture;
        // a 50ms timer can fire after completion or be starved by the planning worker.
        var images = new CancellingImages(cancellation.Cancel);
        var task = Task.Run(() => CliBatchPlan.Create(batch, document, 0, fingerprint, new F.SourceDate(2), images, cancellation.Token));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(images.Enumerated);
        Assert.Equal(5000, document.Sources.Count); Assert.Empty(document.Catalog);
    }

    private sealed class CancellingImages(Action cancel) : IReadOnlyDictionary<string, VocabularyPreparedImage>
    {
        internal bool Enumerated { get; private set; }
        public int Count => 0;
        public IEnumerable<string> Keys => [];
        public IEnumerable<VocabularyPreparedImage> Values => [];
        public VocabularyPreparedImage this[string key] => throw new KeyNotFoundException();
        public bool ContainsKey(string key) => false;
        public bool TryGetValue(string key, out VocabularyPreparedImage value) { value = null!; return false; }
        public IEnumerator<KeyValuePair<string, VocabularyPreparedImage>> GetEnumerator()
        {
            Enumerated = true; cancel();
            return Enumerable.Empty<KeyValuePair<string, VocabularyPreparedImage>>().GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
