using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Windows WIC counterpart of Mac VocabularyImageStorage.prepare/validate. An archive/editor
/// worker supplies bounded bytes; this adapter owns only decode/normalization, no source path,
/// file, draft or notebook. Frame zero is bounded before pixels are copied; EXIF orientation is
/// applied, size capped and a fresh PNG frame strips source metadata. Codec bytes can differ
/// from ImageIO; the reference hashes the actual returned PNG. Call away from the UI dispatcher.
/// </summary>
internal static class WindowsVocabularyImagePreparer
{
    internal const int MaximumInputBytes = 24 * 1024 * 1024;
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    internal static VocabularyPreparedImage Prepare(ReadOnlySpan<byte> input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Length > MaximumInputBytes) throw Invalid();
        try
        {
            (byte[] Pixels, int Width, int Height) outputPixels;
            using (var frame = new WindowsVocabularyImageFrame(input))
            {
                int width = frame.Width, height = frame.Height;
                if (width <= 0 || height <= 0 || width > 30000 || height > 30000 || (long)width * height > 100000000) throw Invalid();
                cancellationToken.ThrowIfCancellationRequested();
                double scale = Math.Min(1, (double)VocabularyContentPolicy.MaximumDimension / Math.Max(width, height));
                int targetWidth = Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero));
                int targetHeight = Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero));
                outputPixels = Orient(frame.CopyPixels(targetWidth, targetHeight, cancellationToken), frame.Orientation, cancellationToken);
            }
            // Native decoder, source bytes and transforms are released before PNG output is
            // allocated. Rotate only this bounded buffer: WIC's unbuffered flip/rotate can
            // repeatedly decode scanlines and take quadratic time for a large source image.
            var source = BitmapSource.Create(outputPixels.Width, outputPixels.Height, 96, 96, PixelFormats.Bgra32, null, outputPixels.Pixels, outputPixels.Width * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = new System.IO.MemoryStream(); encoder.Save(output);
            if (output.Length > VocabularyContentPolicy.MaximumImageBytes) throw Invalid();
            cancellationToken.ThrowIfCancellationRequested(); var data = output.ToArray();
            var reference = new VocabularyImageReference(Convert.ToHexStringLower(SHA256.HashData(data)), source.PixelWidth, source.PixelHeight, data.Length);
            return new(reference, data);
        }
        catch (OperationCanceledException) { throw; }
        catch (VocabularyException) { throw; }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or NotSupportedException or InvalidOperationException
            or OverflowException or System.Runtime.InteropServices.COMException or System.IO.FileFormatException)
        { throw Invalid(); }
    }
    private static (byte[] Pixels, int Width, int Height) Orient((byte[] Pixels, int Width, int Height) input, int orientation, CancellationToken token)
    {
        if (orientation == 1) return input;
        int width = orientation >= 5 ? input.Height : input.Width, height = orientation >= 5 ? input.Width : input.Height;
        var pixels = new byte[input.Pixels.Length];
        for (int y = 0; y < input.Height; ++y)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < input.Width; ++x)
            {
                (int dx, int dy) = orientation switch
                {
                    2 => (input.Width - 1 - x, y), 3 => (input.Width - 1 - x, input.Height - 1 - y),
                    4 => (x, input.Height - 1 - y), 5 => (y, x), 6 => (input.Height - 1 - y, x),
                    7 => (input.Height - 1 - y, input.Width - 1 - x), 8 => (y, input.Width - 1 - x), _ => (x, y),
                };
                input.Pixels.AsSpan((y * input.Width + x) * 4, 4).CopyTo(pixels.AsSpan((dy * width + dx) * 4, 4));
            }
        }
        return (pixels, width, height);
    }

    internal static void Validate(ReadOnlySpan<byte> data, VocabularyImageReference reference)
    {
        if (!VocabularyContentPolicy.IsValid(reference) || data.Length != reference.ByteCount || !data.StartsWith(PngSignature)
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), reference.Digest, StringComparison.Ordinal)) throw Invalid();
        try
        {
            using var frame = new WindowsVocabularyImageFrame(data);
            if (frame.Container != new Guid("1b7cfaf4-713f-473c-bbcd-6137425faeaf") || frame.Width != reference.Width || frame.Height != reference.Height) throw Invalid();
        }
        catch (VocabularyException) { throw; }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or NotSupportedException or InvalidOperationException
            or OverflowException or System.Runtime.InteropServices.COMException or System.IO.FileFormatException)
        { throw Invalid(); }
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
}
