using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Win32.Vocabulary;

internal sealed partial class FrozenVocabularyImportStore
{
    private const long MaximumMediaBytes = 128L * 1024 * 1024;
    private IVocabularyImageStorage ImageStorage => _images ?? throw new VocabularyException(VocabularyFailure.InvalidContent);
    private IOwnedVocabularyImageStorage OwnedImageStorage => _images as IOwnedVocabularyImageStorage ?? throw new VocabularyException(VocabularyFailure.InvalidContent);

    public Task<VocabularyPreparedImage> PrepareImageAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.Length > 24 * 1024 * 1024) return Task.FromException<VocabularyPreparedImage>(new VocabularyException(VocabularyFailure.InvalidContent));
        var retained = data.ToArray();
        return Run(() => ImageStorage.Prepare(retained, cancellationToken), cancellationToken);
    }
    public Task<VocabularyPreparedImage> PrepareImageFileAsync(string path, CancellationToken cancellationToken = default) =>
        Run(() => ImageStorage.PrepareFile(path, cancellationToken), cancellationToken);
    public Task<byte[]> ReadImageAsync(VocabularyImageReference reference, CancellationToken cancellationToken = default) =>
        Run(() => Database.Access(false, () => { cancellationToken.ThrowIfCancellationRequested(); return ImageStorage.Read(reference); }), cancellationToken);
    public Task<CanonicalVocabularyMediaSnapshot> ReadMediaSnapshotAsync(CancellationToken cancellationToken = default) =>
        Run(() => Database.Access(true, () =>
        {
            var snapshot = LoadCurrent(cancellationToken);
            var references = ImageReferences(snapshot.Document); ValidateMediaBudget(references);
            var images = new List<VocabularyPreparedImage>(references.Count);
            foreach (var reference in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                images.Add(new VocabularyPreparedImage(reference, ImageStorage.Read(reference)));
            }
            return new CanonicalVocabularyMediaSnapshot(new(snapshot.Revision, snapshot.Document), images);
        }), cancellationToken);
    private static void ValidateMediaBudget(IEnumerable<VocabularyImageReference> references)
    {
        long total = 0;
        foreach (var image in references)
        {
            if (!VocabularyContentPolicy.IsValid(image)) throw new VocabularyException(VocabularyFailure.InvalidContent);
            if (image.ByteCount > MaximumMediaBytes - total) throw new VocabularyException(VocabularyFailure.OversizedArchive);
            total += image.ByteCount;
        }
    }
    private static void ValidateMediaContent(Cotabby.Core.Vocabulary.Frozen.VocabularyDocument document)
    {
        foreach (var sense in document.Entries.SelectMany(entry => entry.Senses))
        {
            VocabularyContentPolicy.Validate(sense.Content ?? []);
            foreach (var review in (sense.Review is null ? Enumerable.Empty<Cotabby.Core.Vocabulary.Frozen.VocabularyReview>() : [sense.Review]).Concat(sense.ReviewCards ?? []))
                foreach (var layout in new[] { review.Plan }.Concat(review.PreservedPlans).Select(plan => plan.Layout).OfType<ReviewLayout>()) ReviewLayoutPolicy.Validate(layout);
        }
        Cotabby.Core.Vocabulary.VocabularyArchiveValidation.ValidateTemplates(document.ReviewTemplates ?? []);
    }
    private void RemoveOwnedImage(VocabularyImageReference reference, VocabularyWindowsFile.FileIdentity identity)
    { try { OwnedImageStorage.RemoveOwned(reference, identity); } catch { /* Best effort; never undo a commit or mask failure. */ } }
    private VocabularyWindowsFile.FileIdentity? CaptureCleanupIdentity(VocabularyImageReference reference)
    { try { return OwnedImageStorage.CaptureIdentity(reference); } catch (VocabularyException) { return null; } }
}
