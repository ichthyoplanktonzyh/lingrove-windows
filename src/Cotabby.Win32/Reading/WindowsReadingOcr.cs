using System.Runtime.Versioning;
using System.Text;
using Cotabby.Core.LanguageLearning.Reading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Cotabby.Win32.Reading;

/// <summary>Explicit request-owned Windows OCR. Construction performs no COM or language lookup;
/// this adapter neither captures the screen nor owns a model, endpoint or persistent image.</summary>
[SupportedOSPlatform("windows10.0.19041")]
public sealed class WindowsReadingOcr : IReadingOcr
{
    private const long MaximumPixels = 16_000_000;
    public async Task<IReadOnlyList<ReadingOcrLine>> RecognizeAsync(ReadingBitmap bitmap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap); cancellationToken.ThrowIfCancellationRequested();
        long pixels = (long)bitmap.Width * bitmap.Height;
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || pixels > MaximumPixels || bitmap.Bgra is null
            || pixels * 4 != bitmap.Bgra.LongLength) throw new ArgumentException("The selected screen region is too large.");
        OcrEngine engine;
        try { engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new ReadingOcrUnavailableException(); }
        catch (Exception error) when (error is not OperationCanceledException and not ReadingOcrUnavailableException) { throw new ReadingOcrUnavailableException(); }
        if ((uint)bitmap.Width > OcrEngine.MaxImageDimension || (uint)bitmap.Height > OcrEngine.MaxImageDimension)
            throw new ArgumentException("The selected screen region is too large for Windows OCR.");
        using var writer = new DataWriter(); writer.WriteBytes(bitmap.Bgra); var buffer = writer.DetachBuffer();
        using var image = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Ignore);
        cancellationToken.ThrowIfCancellationRequested();
        var work = engine.RecognizeAsync(image).AsTask();
        try
        {
            var result = await work.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var lines = new List<ReadingOcrLine>(); int bytes = 0, wordBytes = 0, words = 0;
            var utf8 = new UTF8Encoding(false, true);
            foreach (var line in result.Lines)
            {
                string text = line.Text; bytes = checked(bytes + utf8.GetByteCount(text) + 1);
                if (bytes > 262_144 || lines.Count >= 4096) throw new ArgumentException("Recognized text is too large.");
                var lineWords = new List<ReadingOcrWord>();
                foreach (var word in line.Words)
                {
                    if (++words > 8192) throw new ArgumentException("Recognized text is too large.");
                    wordBytes = checked(wordBytes + utf8.GetByteCount(word.Text));
                    if (wordBytes > 262_144) throw new ArgumentException("Recognized text is too large.");
                    var b = word.BoundingRect;
                    if (!double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Width) || !double.IsFinite(b.Height)
                        || b.Width <= 0 || b.Height <= 0 || b.X < 0 || b.Y < 0 || b.Right > bitmap.Width || b.Bottom > bitmap.Height)
                        throw new ArgumentException("Windows OCR returned invalid text bounds.");
                    lineWords.Add(new(word.Text, new(b.X, b.Y, b.Width, b.Height)));
                }
                lines.Add(new(text, lineWords));
            }
            return lines;
        }
        finally { try { await work.ConfigureAwait(false); } catch { } }
    }
}
