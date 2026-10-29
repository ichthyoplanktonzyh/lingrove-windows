using System.Text;
using System.Text.Json;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Owned full canonical graph and detached media, without file or Store ownership.</summary>
public sealed class FrozenVocabularyBackup
{
    private readonly IReadOnlyDictionary<string, byte[]> _images;
    public FrozenVocabularyBackup(VocabularyDocument document, IReadOnlyDictionary<string, byte[]> images, long version = 1)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(images); Document = document; Version = version;
        _images = CollectionValues.Map(images.Select(pair => new KeyValuePair<string, byte[]>(pair.Key, (pair.Value ?? throw new ArgumentException("Image bytes required.")).ToArray())));
    }
    public VocabularyDocument Document { get; }
    public long Version { get; }
    public IReadOnlyDictionary<string, byte[]> CopyImages() => CollectionValues.Map(_images.Select(pair => new KeyValuePair<string, byte[]>(pair.Key, pair.Value.ToArray())));
    internal IReadOnlyDictionary<string, byte[]> ImageBytes => _images;
}

/// <summary>The v8 document codec owns graph serialization; this envelope only embeds its JSON
/// value and the original Foundation base64 media map. Admission is a separate operation.</summary>
public static class FrozenVocabularyBackupCodec
{
    private sealed record Wire(long Version, JsonElement Document, IReadOnlyDictionary<string, byte[]> Images);
    private sealed record SchemaProbe(long SchemaVersion);
    public const int MaximumBackupBytes = 192 * 1024 * 1024;
    public static byte[] Encode(FrozenVocabularyBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        using var document = JsonDocument.Parse(VocabularyDocumentCodec.Encode(backup.Document), new JsonDocumentOptions { MaxDepth = 512 });
        return VocabularyPayloadCodec.EncodePortableValue(new Wire(backup.Version, document.RootElement, backup.ImageBytes), MaximumBackupBytes);
    }
    public static FrozenVocabularyBackup Decode(ReadOnlyMemory<byte> input)
    {
        if (input.Length > MaximumBackupBytes) throw new VocabularyException(VocabularyFailure.OversizedArchive);
        try
        {
            var wire = VocabularyPayloadCodec.DecodePortableValue<Wire>(input);
            if (wire.Version != 1) throw new VocabularyException(VocabularyFailure.UnsupportedVersion);
            if (wire.Document.ValueKind != JsonValueKind.Object) throw new VocabularyException(VocabularyFailure.InvalidArchive);
            byte[] documentBytes = Encoding.UTF8.GetBytes(wire.Document.GetRawText());
            // This campaign accepts only current-format backups, without legacy conversion.
            if (VocabularyPayloadCodec.DecodePortableValue<SchemaProbe>(documentBytes).SchemaVersion != VocabularyDocument.CurrentVersion)
                throw new VocabularyException(VocabularyFailure.UnsupportedVersion);
            return new(VocabularyDocumentCodec.Decode(documentBytes), wire.Images, wire.Version);
        }
        catch (VocabularyException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        { throw new VocabularyException(VocabularyFailure.InvalidArchive); }
    }
}

/// <summary>Source restore admission: graph, exact image-key set and aggregate byte limit.
/// The caller must additionally validate each native PNG before any staging.</summary>
public static class FrozenVocabularyRestorePolicy
{
    public const long MaximumMediaBytes = 128L * 1024 * 1024;
    public static IReadOnlyList<VocabularyImageReference> References(VocabularyDocument document) => CollectionValues.List(
        document.Entries.SelectMany(entry => entry.Senses).SelectMany(sense =>
            VocabularyContentPolicy.Images(sense.Content ?? []).Concat(
                (sense.Review is null ? Enumerable.Empty<VocabularyReview>() : [sense.Review]).Concat(sense.ReviewCards ?? [])
                    .SelectMany(review => new[] { review.Plan }.Concat(review.PreservedPlans)).Where(plan => plan.Layout is not null)
                    .SelectMany(plan => VocabularyContentPolicy.Images(plan.Layout!.CustomContent))))
        .Concat((document.ReviewTemplates ?? []).SelectMany(template => VocabularyContentPolicy.Images(template.Layout.CustomContent))).Distinct());
    public static IReadOnlyList<VocabularyImageReference> Validate(FrozenVocabularyBackup backup)
    {
        if (backup.Version != 1) throw new VocabularyException(VocabularyFailure.UnsupportedVersion);
        VocabularyArchiveValidation.Validate(backup.Document);
        var references = References(backup.Document);
        long bytes = 0; foreach (var image in backup.ImageBytes.Values) { if (image.Length > MaximumMediaBytes - bytes) throw new VocabularyException(VocabularyFailure.InvalidArchive); bytes += image.Length; }
        if (!references.Select(image => image.Filename).ToHashSet(StringComparer.Ordinal).SetEquals(backup.ImageBytes.Keys)) throw new VocabularyException(VocabularyFailure.InvalidArchive);
        return references;
    }
}
