using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Decodes only a verified bounded notebook PNG, never the original imported source. A visible
/// App image view owns the returned frozen bitmap; freezing permits transfer from this worker to
/// the dispatcher without a live stream, decoder or COM pointer escaping its synchronous scope.
/// </summary>
public static class WindowsVocabularyImageDecoder
{
    public static BitmapSource Decode(ReadOnlySpan<byte> data, VocabularyImageReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!VocabularyContentPolicy.IsValid(reference) || data.Length != reference.ByteCount) throw new VocabularyException(VocabularyFailure.InvalidContent);
        // Own the bytes across validation and decode: a ReadOnlySpan does not prevent its array
        // owner mutating concurrently after the digest was checked.
        byte[] copy = data.ToArray(); WindowsVocabularyImagePreparer.Validate(copy, reference);
        using var frame = new WindowsVocabularyImageFrame(copy);
        int width = checked((int)reference.Width), height = checked((int)reference.Height);
        var pixels = frame.CopyPixels(width, height, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels.Pixels, width * 4);
        bitmap.Freeze(); return bitmap;
    }
}
