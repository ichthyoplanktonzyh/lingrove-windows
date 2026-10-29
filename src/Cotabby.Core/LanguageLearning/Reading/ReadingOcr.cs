namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>One explicit bounded BGRA screenshot; pixel bytes remain request-owned and in memory.</summary>
public sealed record ReadingBitmap(int Width, int Height, byte[] Bgra);
public readonly record struct ReadingOcrRect(double X, double Y, double Width, double Height);
public sealed record ReadingOcrWord(string Text, ReadingOcrRect Bounds);
public sealed record ReadingOcrLine(string Text, IReadOnlyList<ReadingOcrWord> Words);
public interface IReadingOcr
{
    Task<IReadOnlyList<ReadingOcrLine>> RecognizeAsync(ReadingBitmap bitmap, CancellationToken cancellationToken);
}
public sealed class ReadingOcrUnavailableException() : Exception("Windows OCR is unavailable. Install a supported OCR language in Windows settings.");
