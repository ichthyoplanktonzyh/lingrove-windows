namespace Cotabby.Core.Speech;

/// <summary>Detached encoded audio borrowed by review preparation. Construction copies bytes but
/// does not validate a codec; SpeechExportPolicy admits/normalizes them. No player, file handle or
/// credential crosses this value boundary, and callers cannot mutate its content identity.</summary>
public sealed class SpeechExportAudio
{
    private readonly byte[] _data;
    public SpeechExportAudio(ReadOnlySpan<byte> data, string fileExtension) { _data = data.ToArray(); FileExtension = fileExtension; }
    public string FileExtension { get; }
    public int ByteCount => _data.Length;
    public byte[] CopyBytes() => (byte[])_data.Clone();
    internal ReadOnlySpan<byte> Bytes => _data;
}

/// <summary>The two original SpeechError cases emitted by pure export admission. This boundary
/// retains their actionable source wording without transporting native diagnostics or audio bytes.</summary>
public enum SpeechExportFailure { InvalidAudio, ResponseTooLarge }
/// <summary>Fixed export admission failure; it retains no native error, audio bytes or keys.</summary>
public sealed class SpeechExportException : Exception
{
    public SpeechExportException(SpeechExportFailure failure) : base(failure switch
    {
        SpeechExportFailure.InvalidAudio => "The speech provider returned audio that could not be played.",
        SpeechExportFailure.ResponseTooLarge => "The speech audio exceeded the size limit. Try a shorter passage.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    }) { Failure = failure; }
    public SpeechExportFailure Failure { get; }
}
