using System.Buffers.Binary;
using System.Runtime.Versioning;
using NAudio.MediaFoundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;
using Native = Windows.Win32.SpeechNativeMethods;

namespace Cotabby.Win32.Speech;

/// <summary>Unsafe Media Foundation calls stay in this synchronous MTA-only half. CsWin32
/// generates SDK signatures and complete COM layouts; application code never guesses vtable
/// slots or projects these pointers through a second RCW. Every returned reference is owned
/// immediately, including on failed setup, and every locked native buffer unlocks before release.
/// Only copied, validated packets cross back to managed playback.</summary>
[SupportedOSPlatform("windows10.0")]
public sealed partial class WindowsSpeechAudioCodec
{
    private readonly record struct Format(int Rate, int Channels, bool Float, int Bits);
    private const uint AudioStream = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM);
    private static unsafe WindowsSpeechDecodedAudio DecodeOwned(byte[] encoded, CancellationToken cancellationToken, bool materialize)
    {
        Native.MFStartup(Native.MF_VERSION, Native.MFSTARTUP_NOSOCKET).ThrowOnFailure();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IStream* streamPointer;
            // SHCreateMemStream copies the bounded bytes. The fixed pin covers that copy only;
            // the resulting native stream owns its storage through source-reader destruction.
            fixed (byte* bytes = encoded) streamPointer = Native.SHCreateMemStream(bytes, (uint)encoded.Length);
            using var stream = new OwnedCom<IStream>(streamPointer);
            if (stream.Pointer is null) throw Invalid();
            IMFByteStream* byteStreamPointer = null;
            HRESULT hr = Native.MFCreateMFByteStreamOnStream(stream.Pointer, &byteStreamPointer);
            using var byteStream = Own(hr, byteStreamPointer);
            IMFSourceReader* readerPointer = null;
            hr = Native.MFCreateSourceReaderFromByteStream(byteStream.Pointer, null, &readerPointer);
            using var reader = Own(hr, readerPointer);
            reader.Pointer->SetStreamSelection(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS), false).ThrowOnFailure();
            reader.Pointer->SetStreamSelection(AudioStream, true).ThrowOnFailure();

            IMFMediaType* sourcePointer = null;
            hr = reader.Pointer->GetNativeMediaType(AudioStream, 0, &sourcePointer);
            using var source = Own(hr, sourcePointer);
            int rate = checked((int)GetUInt(source.Pointer, Native.MF_MT_AUDIO_SAMPLES_PER_SECOND));
            int channels = checked((int)GetUInt(source.Pointer, Native.MF_MT_AUDIO_NUM_CHANNELS));
            if (rate is < 8000 or > 192000 || channels is < 1 or > 2) throw Invalid();
            // Some Windows decoders expose PCM16 rather than Float32. Both negotiated outputs
            // retain the original channels/rate; PCM16 becomes Float32 only after a bounded read.
            if (!SetOutput(reader.Pointer, rate, channels, floating: true))
                if (!SetOutput(reader.Pointer, rate, channels, floating: false)) throw Invalid();
            Format format = CurrentFormat(reader.Pointer);
            if (format.Rate != rate || format.Channels != channels) throw Invalid();
            int frameBytes = channels * (format.Bits / 8);
            var packets = new List<byte[]>(); long frames = 0; int emptyReads = 0;
            // Packet boundaries belong to us, not to native decoder sample sizes. Carry one
            // partial packet across reads so millions of one-frame samples cannot allocate
            // millions of managed objects. Duration-only inspection validates and discards.
            int outputCapacity = 8192 * channels * 4, outputCount = 0;
            byte[] output = materialize ? new byte[outputCapacity] : [];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint flags = 0; IMFSample* samplePointer = null;
                hr = reader.Pointer->ReadSample(AudioStream, 0, null, &flags, null, &samplePointer);
                using var sample = new OwnedCom<IMFSample>(samplePointer); hr.ThrowOnFailure();
                var status = (SourceReaderFlags)flags;
                const SourceReaderFlags known = SourceReaderFlags.EndOfStream | SourceReaderFlags.NewStream | SourceReaderFlags.NativeMediaTypeChanged
                    | SourceReaderFlags.CurrentMediaTypeChanged | SourceReaderFlags.StreamTick | SourceReaderFlags.AllEffectsRemoved;
                if ((status & ~known) != 0) throw Invalid();
                if ((status & (SourceReaderFlags.CurrentMediaTypeChanged | SourceReaderFlags.NativeMediaTypeChanged | SourceReaderFlags.AllEffectsRemoved)) != 0
                    && CurrentFormat(reader.Pointer) != format) throw Invalid();
                if (sample.Pointer is not null)
                {
                    uint sampleBytes = 0; sample.Pointer->GetTotalLength(&sampleBytes).ThrowOnFailure();
                    if (sampleBytes % frameBytes != 0 || sampleBytes / frameBytes > rate * 90L - frames) throw Invalid();
                    if (sampleBytes != 0)
                    {
                        IMFMediaBuffer* bufferPointer = null;
                        hr = sample.Pointer->ConvertToContiguousBuffer(&bufferPointer);
                        using var buffer = Own(hr, bufferPointer);
                        byte* data = null; uint capacity = 0, length = 0;
                        buffer.Pointer->Lock(&data, &capacity, &length).ThrowOnFailure();
                        try
                        {
                            if (data is null || length > capacity || length != sampleBytes) throw Invalid();
                            var input = new ReadOnlySpan<byte>(data, checked((int)length));
                            for (int offset = 0; offset < input.Length;)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                int availableValues = materialize ? (outputCapacity - outputCount) / 4 : 8192 * channels;
                                var part = input.Slice(offset, Math.Min(availableValues * (format.Bits / 8), input.Length - offset));
                                int values = part.Length / (format.Bits / 8);
                                for (int index = 0; index < values; index++)
                                {
                                    float value = format.Float ? BinaryPrimitives.ReadSingleLittleEndian(part[(index * 4)..])
                                        : BinaryPrimitives.ReadInt16LittleEndian(part[(index * 2)..]) / 32768f;
                                    if (!float.IsFinite(value) || MathF.Abs(value) > 8) throw Invalid();
                                    if (materialize) BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(outputCount + index * 4), value);
                                }
                                offset += part.Length;
                                if (materialize)
                                {
                                    outputCount += values * 4;
                                    if (outputCount == outputCapacity) { packets.Add(output); output = new byte[outputCapacity]; outputCount = 0; }
                                }
                            }
                        }
                        finally { buffer.Pointer->Unlock().ThrowOnFailure(); }
                        frames += sampleBytes / frameBytes; emptyReads = 0;
                    }
                    else if (++emptyReads > 4096) throw Invalid();
                }
                else if (++emptyReads > 4096) throw Invalid();
                // EOS may accompany a final sample. Its owned data is consumed before stopping.
                if ((status & SourceReaderFlags.EndOfStream) != 0) break;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (frames == 0) throw Invalid();
            if (outputCount != 0) packets.Add(output.AsSpan(0, outputCount).ToArray());
            return new(rate, channels, frames, (double)frames / rate, packets.ToArray());
        }
        finally { Native.MFShutdown().ThrowOnFailure(); }
    }
    private static unsafe bool SetOutput(IMFSourceReader* reader, int rate, int channels, bool floating)
    {
        IMFMediaType* typePointer = null;
        HRESULT hr = Native.MFCreateMediaType(&typePointer); using var type = Own(hr, typePointer);
        SetGuid(type.Pointer, Native.MF_MT_MAJOR_TYPE, Native.MFMediaType_Audio);
        SetGuid(type.Pointer, Native.MF_MT_SUBTYPE, floating ? Native.MFAudioFormat_Float : Native.MFAudioFormat_PCM);
        SetUInt(type.Pointer, Native.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)rate);
        SetUInt(type.Pointer, Native.MF_MT_AUDIO_NUM_CHANNELS, (uint)channels);
        SetUInt(type.Pointer, Native.MF_MT_AUDIO_BITS_PER_SAMPLE, floating ? 32u : 16u);
        return reader->SetCurrentMediaType(AudioStream, null, type.Pointer).Succeeded;
    }
    private static unsafe Format CurrentFormat(IMFSourceReader* reader)
    {
        IMFMediaType* typePointer = null;
        HRESULT hr = reader->GetCurrentMediaType(AudioStream, &typePointer); using var type = Own(hr, typePointer);
        Guid major = GetGuid(type.Pointer, Native.MF_MT_MAJOR_TYPE), subtype = GetGuid(type.Pointer, Native.MF_MT_SUBTYPE);
        int bits = checked((int)GetUInt(type.Pointer, Native.MF_MT_AUDIO_BITS_PER_SAMPLE));
        bool floating = subtype == Native.MFAudioFormat_Float;
        if (major != Native.MFMediaType_Audio || !(floating && bits == 32 || subtype == Native.MFAudioFormat_PCM && bits == 16)) throw Invalid();
        return new(checked((int)GetUInt(type.Pointer, Native.MF_MT_AUDIO_SAMPLES_PER_SECOND)),
            checked((int)GetUInt(type.Pointer, Native.MF_MT_AUDIO_NUM_CHANNELS)), floating, bits);
    }
    private static unsafe uint GetUInt(IMFMediaType* type, Guid key) { uint value = 0; type->GetUINT32(&key, &value).ThrowOnFailure(); return value; }
    private static unsafe Guid GetGuid(IMFMediaType* type, Guid key) { Guid value = default; type->GetGUID(&key, &value).ThrowOnFailure(); return value; }
    private static unsafe void SetUInt(IMFMediaType* type, Guid key, uint value) => type->SetUINT32(&key, value).ThrowOnFailure();
    private static unsafe void SetGuid(IMFMediaType* type, Guid key, Guid value) => type->SetGUID(&key, &value).ThrowOnFailure();
    private static unsafe OwnedCom<T> Own<T>(HRESULT result, T* pointer) where T : unmanaged
    {
        var owned = new OwnedCom<T>(pointer);
        try { result.ThrowOnFailure(); if (pointer is null) throw Invalid(); return owned; }
        catch { owned.Dispose(); throw; }
    }
    /// <summary>Exactly one returned COM reference, released on the decoding MTA. The interface
    /// pointer's first three slots are IUnknown by COM contract. No RCW adds another reference,
    /// and no finalizer later releases an apartment-owned object on an unrelated thread.</summary>
    private sealed unsafe class OwnedCom<T>(T* pointer) : IDisposable where T : unmanaged
    {
        private nint _pointer = (nint)pointer;
        internal T* Pointer => (T*)_pointer;
        public void Dispose() { nint value = Interlocked.Exchange(ref _pointer, 0); if (value != 0) ((IUnknown*)value)->Release(); }
    }
}
