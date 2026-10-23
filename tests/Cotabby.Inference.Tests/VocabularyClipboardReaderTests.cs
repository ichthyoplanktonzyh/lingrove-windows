using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests;

/// <summary>
/// The raw reader uses app-owned unmanaged buffers and scripted Win32 effects. These tests never
/// open or change the runner clipboard, nor pretend to exercise a real foreign delayed renderer.
/// </summary>
public sealed class VocabularyClipboardReaderTests
{
    [Theory]
    [InlineData("PNG")]
    [InlineData("image/png")]
    [InlineData("TIFF")]
    [InlineData("JFIF")]
    [InlineData("JPEG")]
    [InlineData("image/jpeg")]
    public void UsesTheFirstAvailableEncodedRepresentation(string first)
    {
        using var native = new Clipboard(); var reader = new VocabularyClipboardImageReader(native);
        string[] order = ["PNG", "image/png", "TIFF", "JFIF", "JPEG", "image/jpeg"];
        foreach (string name in order.Skip(Array.IndexOf(order, first))) native.Add(name, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.Read(7)); Assert.Equal(new[] { native.Format(first) }, native.ReadFormats);
        Assert.Equal(1, native.Closes); Assert.Equal(1, native.Unlocks);
    }

    [Fact]
    public void NullRepresentationTriesNextButNonnullInvalidBytesDoNotFallThrough()
    {
        using var native = new Clipboard(); var reader = new VocabularyClipboardImageReader(native);
        native.Add("PNG", null); native.Add("image/png", [0xFF]); native.Add("TIFF", [1, 2]);
        Assert.Equal(new byte[] { 0xFF }, reader.Read(7)); Assert.Equal(new[] { native.Format("PNG"), native.Format("image/png") }, native.ReadFormats);
    }
    [Fact]
    public void EmptyNonnullRepresentationIsReturnedWithoutLockOrFallback()
    {
        using var native = new Clipboard(); var reader = new VocabularyClipboardImageReader(native); native.Add("PNG", []); native.Add("TIFF", [1]);
        Assert.Empty(reader.Read(7)!); Assert.Single(native.ReadFormats); Assert.Equal(0, native.Locks); Assert.Equal(1, native.Closes);
    }
    [Theory]
    [InlineData(0u)]
    [InlineData(6u)]
    public void MissingOrStaleSequenceCannotOpenClipboard(uint expected)
    {
        using var native = new Clipboard(); var reader = new VocabularyClipboardImageReader(native);
        Assert.Null(reader.Read(expected)); Assert.Equal(0, native.Opens); Assert.Equal(0, native.Closes);
    }
    [Fact]
    public void BusyClipboardRetriesOnlyThreeTimesAndDoesNotCloseAnUnownedOpen()
    {
        using var native = new Clipboard { OpenResult = false }; var reader = new VocabularyClipboardImageReader(native);
        Assert.Null(reader.Read(7)); Assert.Equal(3, native.Opens); Assert.Equal(0, native.Closes);
    }
    [Fact]
    public void ChangeWhileOpeningIsRejectedAndTheAcquiredOpenIsClosed()
    {
        using var native = new Clipboard(); native.AfterOpen = () => native.Sequence = 8;
        Assert.Null(new VocabularyClipboardImageReader(native).Read(7)); Assert.Equal(1, native.Closes); Assert.Empty(native.ReadFormats);
    }
    [Fact]
    public void SizeIsBoundedBeforeAllocationOrLockAndStillCloses()
    {
        using var native = new Clipboard(); var reader = new VocabularyClipboardImageReader(native); native.Add("PNG", [1]); native.ReportedSize = VocabularyClipboardImageReader.MaximumBytes + 1;
        Assert.Throws<InvalidDataException>(() => reader.Read(7)); Assert.Equal(0, native.Locks); Assert.Equal(1, native.Closes);
    }
    [Fact]
    public void FailedLockDoesNotUnlockOrReadAnotherRepresentation()
    {
        using var native = new Clipboard { LockFails = true }; var reader = new VocabularyClipboardImageReader(native); native.Add("PNG", [1]); native.Add("TIFF", [2]);
        Assert.Throws<InvalidDataException>(() => reader.Read(7)); Assert.Equal(0, native.Unlocks); Assert.Equal(1, native.Closes); Assert.Single(native.ReadFormats);
    }
    [Fact]
    public void DelayedRenderingMayAdvanceSequenceBeforeTheStableCopy()
    {
        using var native = new Clipboard(); var reader = new VocabularyClipboardImageReader(native); native.Add("PNG", [5]); native.AfterData = () => native.Sequence = 8;
        Assert.Equal(new byte[] { 5 }, reader.Read(7)); Assert.Equal(1, native.Unlocks); Assert.Equal(1, native.Closes);
    }
    [Theory]
    [InlineData("owner-at-data")]
    [InlineData("owner-at-unlock")]
    [InlineData("sequence-at-unlock")]
    public void OwnerOrCopySequenceChangeDiscardsTheDetachedResult(string change)
    {
        using var native = new Clipboard(); var reader = new VocabularyClipboardImageReader(native); native.Add("PNG", [5]);
        if (change == "owner-at-data") native.AfterData = () => native.Owner = new(20);
        else if (change == "owner-at-unlock") native.AfterUnlock = () => native.Owner = new(20);
        else native.AfterUnlock = () => native.Sequence = 8;
        Assert.Null(reader.Read(7)); Assert.Equal(1, native.Closes); Assert.Equal(change == "owner-at-data" ? 0 : 1, native.Unlocks);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public async Task ReadsIndependentLiteralProtocolFrames(int length)
    {
        byte[] frame = Frame(length, length == 3 ? [7, 8, 9] : []);
        var bytes = await VocabularyImageTransferProtocol.ReadAsync(new MemoryStream(frame), default);
        if (length == -1) Assert.Null(bytes); else Assert.Equal(frame[8..], bytes);
    }
    [Theory]
    [InlineData("magic")]
    [InlineData("failure")]
    [InlineData("negative")]
    [InlineData("oversized")]
    [InlineData("header-truncated")]
    [InlineData("body-truncated")]
    [InlineData("trailing")]
    public async Task RejectsUntrustedProtocolFramesBeforeReturningAnyBytes(string mode)
    {
        byte[] frame = Frame(1, [4]);
        switch (mode)
        {
            case "magic": frame[0] = 0; break;
            case "failure": frame = Frame(-2, []); break;
            case "negative": frame = Frame(int.MinValue, []); break;
            case "oversized": frame = Frame(VocabularyClipboardImageReader.MaximumBytes + 1, []); break;
            case "header-truncated": frame = frame[..7]; break;
            case "body-truncated": frame = frame[..8]; break;
            case "trailing": frame = [.. frame, 2]; break;
        }
        if (mode.EndsWith("truncated", StringComparison.Ordinal))
            await Assert.ThrowsAsync<EndOfStreamException>(() => VocabularyImageTransferProtocol.ReadAsync(new MemoryStream(frame), default));
        else await Assert.ThrowsAsync<InvalidDataException>(() => VocabularyImageTransferProtocol.ReadAsync(new MemoryStream(frame), default));
    }
    [Fact]
    public void WriterUsesTheSpecifiedWireBytesForDataUnavailableAndFailure()
    {
        foreach (var item in new (byte[]? Bytes, bool Failed, byte[] Expected)[]
        { ([4, 5], false, Frame(2, [4, 5])), (null, false, Frame(-1, [])), ([], false, Frame(0, [])), ([1], true, Frame(-2, [])) })
        {
            using var stream = new MemoryStream(); VocabularyImageTransferProtocol.Write(stream, item.Bytes, item.Failed); Assert.Equal(item.Expected, stream.ToArray());
        }
    }
    internal static byte[] Frame(int count, byte[] bytes)
    {
        byte[] result = [0x4C, 0x47, 0x43, 0x31, 0, 0, 0, 0, .. bytes]; BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), count); return result;
    }
    private sealed class Clipboard : IVocabularyImageClipboard, IDisposable
    {
        private readonly Dictionary<string, uint> _formats = new() { ["TIFF"] = 6 };
        private readonly Dictionary<uint, (IntPtr Pointer, int Length)> _buffers = [];
        public uint Register(string name) { if (!_formats.TryGetValue(name, out uint value)) _formats[name] = value = (uint)(_formats.Count + 100); return value; }
        internal uint Format(string name) => _formats[name];
        internal void Add(string name, byte[]? bytes)
        {
            IntPtr buffer = bytes is null ? IntPtr.Zero : Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
            if (bytes is { Length: > 0 }) Marshal.Copy(bytes, 0, buffer, bytes.Length);
            _buffers.Add(Format(name), (buffer, bytes?.Length ?? 0));
        }
        public uint Sequence { get; set; } = 7;
        public IntPtr Owner { get; set; } = new(10);
        internal bool OpenResult = true, LockFails;
        internal int Opens, Closes, Locks, Unlocks;
        internal int? ReportedSize;
        internal Action? AfterOpen, AfterData, AfterUnlock;
        internal List<uint> ReadFormats { get; } = [];
        public bool Open() { ++Opens; AfterOpen?.Invoke(); return OpenResult; }
        public void Close() => ++Closes;
        public bool Available(uint format) => _buffers.ContainsKey(format);
        public IntPtr Data(uint format) { ReadFormats.Add(format); var data = _buffers[format].Pointer; AfterData?.Invoke(); return data; }
        public nuint Size(IntPtr handle) => (nuint)(ReportedSize ?? _buffers.Values.Single(value => value.Pointer == handle).Length);
        public IntPtr Lock(IntPtr handle) { ++Locks; return LockFails ? IntPtr.Zero : handle; }
        public void Unlock(IntPtr handle) { ++Unlocks; AfterUnlock?.Invoke(); }
        public void Dispose() { foreach (var value in _buffers.Values) if (value.Pointer != IntPtr.Zero) Marshal.FreeHGlobal(value.Pointer); }
    }
}
