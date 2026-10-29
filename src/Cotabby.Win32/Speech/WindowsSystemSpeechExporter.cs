using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Speech;
using Windows.Win32.System.Com;
using Native = Windows.Win32.SpeechNativeMethods;

namespace Cotabby.Win32.Speech;

/// <summary>Explicit system recording uses a per-request SAPI stream backed by a bounded COM
/// IStream. It opens no output device or temporary file. Cancellation and deadline purge the
/// utterance and join its MTA before returning detached canonical PCM16 WAV.</summary>
[SupportedOSPlatform("windows10.0")]
public sealed class WindowsSystemSpeechExporter : ISystemSpeechAudioExporting
{
    private const int Rate = 24000, MaximumBytes = Rate * 2 * 90;
    private readonly Func<string, string, CancellationToken, SpeechExportAudio> _record;
    private readonly TimeSpan _deadline;
    public WindowsSystemSpeechExporter() : this(Record, TimeSpan.FromSeconds(120)) { }
    internal WindowsSystemSpeechExporter(Func<string, string, CancellationToken, SpeechExportAudio> record, TimeSpan deadline)
    { _record = record; _deadline = deadline; }
    public async Task<SpeechExportAudio> AudioAsync(string text, string voiceId, CancellationToken cancellationToken)
    {
        if (!SpeechTextPolicy.WithinRequestLimit(text)) throw new SpeechException(SpeechFailure.TextTooLong);
        using var deadline = new CancellationTokenSource(_deadline);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try { var audio = await WindowsSpeechMta.RunAsync(() => _record(text, voiceId, lifetime.Token), CancellationToken.None).ConfigureAwait(false); lifetime.Token.ThrowIfCancellationRequested(); return audio; }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (Exception) when (deadline.IsCancellationRequested) { throw new TimeoutException("System speech recording timed out."); }
    }
    private static unsafe SpeechExportAudio Record(string text, string voiceId, CancellationToken cancellation)
    {
        var sink = new BoundedSpeechStream(MaximumBytes, cancellation); dynamic? synth = null, voices = null, selected = null;
        ISpStream* stream = null; ISpVoice* voice = null; IntPtr unknown = IntPtr.Zero, baseStream = IntPtr.Zero;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            synth = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice", true)!)!;
            voices = synth.GetVoices(); int count = voices.Count;
            for (int i = 0; i < count; i++) { dynamic item = voices.Item(i); if (string.Equals((string)item.Id, voiceId, StringComparison.Ordinal)) { selected = item; break; } Release(item); }
            if (selected is null) throw new InvalidOperationException("The selected speech voice is no longer available.");
            synth.Voice = selected;
            var streamClass = new Guid("715d9c59-4442-11d2-9605-00c04f8ee628"); var streamId = new Guid("12e3cca9-7518-44c5-a5e7-ba5a79cb929e");
            Native.CoCreateInstance(&streamClass, null, CLSCTX.CLSCTX_INPROC_SERVER, &streamId, (void**)&stream).ThrowOnFailure();
            baseStream = Marshal.GetComInterfaceForObject(sink, typeof(System.Runtime.InteropServices.ComTypes.IStream));
            var formatId = new Guid("c31adbae-527f-4ff5-a230-f62bb61ff70c");
            var format = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = Rate, nAvgBytesPerSec = Rate * 2, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0 };
            stream->SetBaseStream((IStream*)baseStream, &formatId, &format).ThrowOnFailure();
            unknown = Marshal.GetIUnknownForObject((object)synth); var voiceIdNative = new Guid("6c44df74-72b9-4992-a1ec-ef996e0422d4"); IntPtr voicePointer;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in voiceIdNative, out voicePointer)); voice = (ISpVoice*)voicePointer;
            voice->SetOutput((IUnknown*)stream, false).ThrowOnFailure();
            cancellation.ThrowIfCancellationRequested(); synth.Speak(text, 17);
            while (!(bool)synth.WaitUntilDone(50)) { if (cancellation.IsCancellationRequested) { synth.Speak("", 3); throw new OperationCanceledException(cancellation); } }
            cancellation.ThrowIfCancellationRequested(); if (sink.Overflowed) throw new SpeechException(SpeechFailure.InvalidAudio);
            byte[] pcm = sink.Bytes(); if (pcm.Length == 0 || pcm.Length % 2 != 0) throw new SpeechException(SpeechFailure.InvalidAudio);
            byte[] wav = new byte[44 + pcm.Length]; "RIFF"u8.CopyTo(wav); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), pcm.Length + 36); "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
            BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
            BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), Rate); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), Rate * 2); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
            "data"u8.CopyTo(wav.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), pcm.Length); pcm.CopyTo(wav, 44); return SpeechExportPolicy.Encoded(wav);
        }
        finally
        {
            if (voice is not null) voice->Release(); if (unknown != IntPtr.Zero) Marshal.Release(unknown);
            if (stream is not null) stream->Release(); if (baseStream != IntPtr.Zero) Marshal.Release(baseStream);
            Release(selected); Release(voices); Release(synth); GC.KeepAlive(sink);
        }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
