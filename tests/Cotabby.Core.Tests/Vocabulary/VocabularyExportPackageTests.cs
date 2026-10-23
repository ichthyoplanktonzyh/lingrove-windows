using System.Collections;
using System.Text;
using Cotabby.Core.Tests.Parity;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyExportPackageTests
{
    [Fact]
    public void ReplacingMediaRetainsPackageIdentityAndOtherValueOwners()
    {
        var p = VocabularyExportPolicy.Package(VocabularyExportProjectionParityTests.ExportEntries(), new());
        var first = p.WithImages(new Dictionary<string, byte[]> { ["first.png"] = [1] });
        var second = first.WithImages(new Dictionary<string, byte[]> { ["second.png"] = [2] });
        Assert.Equal(p.Id, first.Id); Assert.Equal(first.Id, second.Id); Assert.Empty(p.CopyImages());
        Assert.Equal("first.png", Assert.Single(first.CopyImages()).Key); Assert.Equal("second.png", Assert.Single(second.CopyImages()).Key);
        Assert.Equal(first.CopyData(), second.CopyData());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, byte[]>)second.CopyImages()).Clear());
    }
    [Fact]
    public void CountsConfigurationAndBytesStayCapturedAfterSourceChanges()
    {
        var entries = VocabularyExportProjectionParityTests.ExportEntries(); var options = new VocabularyExportConfiguration();
        var p = VocabularyExportPolicy.Package(entries, options); var data = p.CopyData(); entries.Clear(); options = options with { Images = true, Examples = false };
        Assert.Equal(2L, p.EntryCount); Assert.Equal(3L, p.SenseCount); Assert.Equal(1L, p.ExampleCount); Assert.False(p.Configuration.Images);
        Assert.Equal("Lingrove-vocabulary.txt", p.Filename); Assert.Equal(data, p.CopyData()); Assert.NotEqual(options, p.Configuration);
    }
    [Fact]
    public void InitialCancellationPrecedesSourceEnumeration()
    {
        Assert.Throws<OperationCanceledException>(() => VocabularyExportPolicy.Package(new PoisonList(), new(), cancellationToken: new(true)));
    }
    [Fact]
    public void FactoryForwardsTheCapturedTextLabelsToTheRenderer()
    {
        var p = VocabularyExportPolicy.Package(VocabularyExportProjectionParityTests.ExportEntries(), new(), new() { Sense = "captured-label" });
        Assert.Contains("captured-label 1:", Encoding.UTF8.GetString(p.CopyData()), StringComparison.Ordinal);
    }
    private sealed class PoisonList : IReadOnlyList<VocabularyEntry>
    {
        public int Count => throw new InvalidOperationException("Source was read before cancellation.");
        public VocabularyEntry this[int index] => throw new InvalidOperationException("Source was read before cancellation.");
        public IEnumerator<VocabularyEntry> GetEnumerator() => throw new InvalidOperationException("Source was read before cancellation.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
