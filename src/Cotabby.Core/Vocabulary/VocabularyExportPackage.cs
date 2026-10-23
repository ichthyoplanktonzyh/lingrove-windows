using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// One prepared export owns exact encoded bytes, preview and counts independently of a live
/// notebook. A later sheet retains its Id until controls/scope change. The archive attaches
/// captured media through WithImages, which preserves that identity and shares only private,
/// immutable data. Defensive copy methods adapt Swift Data's value ownership to mutable .NET arrays.
/// No constructor or media attachment proves image/file admission or performs IO.
/// </summary>
public sealed class VocabularyExportPackage
{
    private readonly byte[] _data;
    private readonly IReadOnlyDictionary<string, byte[]> _images;
    internal VocabularyExportPackage(VocabularyExportConfiguration configuration, ReadOnlySpan<byte> data, string preview,
        long entryCount, long senseCount, long exampleCount)
        : this(Guid.NewGuid(), configuration, data.ToArray(), preview, entryCount, senseCount, exampleCount,
            CollectionValues.Map(Array.Empty<KeyValuePair<string, byte[]>>())) { }
    private VocabularyExportPackage(Guid id, VocabularyExportConfiguration configuration, byte[] ownedData, string preview,
        long entryCount, long senseCount, long exampleCount, IReadOnlyDictionary<string, byte[]> ownedImages)
    {
        Id = id; Configuration = configuration; _data = ownedData; Preview = preview;
        EntryCount = entryCount; SenseCount = senseCount; ExampleCount = exampleCount; _images = ownedImages;
    }
    public Guid Id { get; }
    public VocabularyExportConfiguration Configuration { get; }
    public string Preview { get; }
    public long EntryCount { get; }
    public long SenseCount { get; }
    public long ExampleCount { get; }
    public int DataByteCount => _data.Length;
    public string DataFilename => "Lingrove-vocabulary." + Configuration.Format.FileExtension();
    public string Filename => Configuration.IncludesImages ? "Lingrove-vocabulary.zip" : DataFilename;
    public byte[] CopyData() => (byte[])_data.Clone();
    public IReadOnlyDictionary<string, byte[]> CopyImages() => CollectionValues.Map(_images.Select(pair =>
        new KeyValuePair<string, byte[]>(pair.Key, (byte[])pair.Value.Clone())));
    public VocabularyExportPackage WithImages(IEnumerable<KeyValuePair<string, byte[]>> images)
    {
        var owned = CollectionValues.Map(images.Select(pair => new KeyValuePair<string, byte[]>(pair.Key, (byte[])pair.Value.Clone())));
        // Source and returned snapshots cannot mutate this shared array: neither exposes it.
        return new(Id, Configuration, _data, Preview, EntryCount, SenseCount, ExampleCount, owned);
    }
}
