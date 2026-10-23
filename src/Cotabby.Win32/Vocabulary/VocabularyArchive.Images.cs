using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

public sealed partial class VocabularyArchive
{
    private const long MaximumMediaBytes = 128L * 1024 * 1024;
    private IVocabularyImageStorage ImageStorage => _images ?? throw new VocabularyException(VocabularyFailure.InvalidContent);

    /// <summary>Preparation owns a bounded detached input; it never stages a file or changes SQL.</summary>
    public Task<VocabularyPreparedImage> PrepareImageAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<VocabularyPreparedImage>(cancellationToken);
        if (data.Length > 24 * 1024 * 1024) return Task.FromException<VocabularyPreparedImage>(new VocabularyException(VocabularyFailure.InvalidContent));
        byte[] copy = data.ToArray(); return Run(() => ImageStorage.Prepare(copy, cancellationToken));
    }
    public Task<byte[]> ReadImageAsync(VocabularyImageReference image) => Run(() => Persistence().Access(false, () => ImageStorage.Read(image)));
    public Task<VocabularyPreparedImage> PrepareImageFileAsync(string path, CancellationToken cancellationToken = default) =>
        Run(() => ImageStorage.PrepareFile(path, cancellationToken));

    private VocabularySnapshot CommitWithImages(VocabularyDocument next, IReadOnlyDictionary<string, VocabularyPreparedImage> supplied,
        CancellationToken cancellationToken) => Persistence().Access(true, () =>
    {
        // Mac validates media first, not the whole graph: an edited sentence can still carry a
        // stale explanation until Commit prunes it. Full validation here would reject valid edits.
        foreach (var sense in next.Entries.SelectMany(entry => entry.Senses))
        {
            VocabularyContentPolicy.Validate(sense.Content ?? []);
            foreach (var layout in sense.ReviewLayouts) ReviewLayoutPolicy.Validate(layout);
        }
        VocabularyArchiveValidation.ValidateTemplates(next.ReviewTemplates ?? []);
        var references = Images(next).ToHashSet(); long total = 0;
        foreach (var image in references)
        {
            if (image.ByteCount > MaximumMediaBytes - total) throw new VocabularyException(VocabularyFailure.OversizedArchive);
            total += image.ByteCount;
        }
        // Retain the exact cached revision from candidate construction. A newer external writer
        // must trigger SQL's revision conflict, not be silently adopted as this candidate's base.
        var baseline = _document ?? Load().Document; var existing = Images(baseline).ToHashSet();
        var written = new List<VocabularyImageReference>();
        try
        {
            foreach (var image in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (supplied.TryGetValue(image.Filename, out var prepared))
                {
                    if (prepared is null) throw new VocabularyException(VocabularyFailure.InvalidContent);
                    if (ImageStorage.Write(prepared.CopyBytes(), image)) written.Add(image);
                }
                else if (!existing.Contains(image)) _ = ImageStorage.Read(image);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // The outer exclusive lock covers staging, nested SQL commit and cleanup/rollback.
            // Once SQL begins, cancellation cannot change an acknowledged durable edit to failure.
            return Commit(next);
        }
        catch
        {
            foreach (var image in written) RemoveImage(image);
            throw;
        }
    });
    private void RemoveImage(VocabularyImageReference image)
    { try { _images?.Remove(image); } catch { /* Cleanup cannot undo committed SQL or mask the original transaction failure. */ } }
}
