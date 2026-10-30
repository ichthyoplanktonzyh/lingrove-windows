using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Cotabby.Tests.Shared;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests;

public sealed class VocabularyBitmapReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCopyIsTopDownRgbWithZeroPaddingAndKeepsTheBorrowedBitmapAlive(bool transferable)
    {
        using var bitmap = new VocabularyBitmapFixture(transferable: transferable); byte[] bmp = VocabularyClipboardBitmap.Read(bitmap.Handle);
        Assert.True(VocabularyBitmapFixture.Alive(bitmap.Handle));
        Assert.Equal(54, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(10)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18)));
        Assert.Equal(-2, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22)));
        Assert.Equal(24, BinaryPrimitives.ReadUInt16LittleEndian(bmp.AsSpan(28)));
        Assert.Equal(VocabularyBitmapFixture.Pixels, bmp[54..]);
    }
    [Fact]
    public void InvalidAndOverBudgetNativeImagesFailWithoutReleasingTheOwner()
    {
        Assert.Throws<InvalidDataException>(() => VocabularyClipboardBitmap.Read(0));
        Assert.Throws<InvalidDataException>(() => VocabularyClipboardBitmap.Read(-1));
        using var bitmap = new VocabularyBitmapFixture(4096, 2160);
        Assert.Throws<InvalidDataException>(() => VocabularyClipboardBitmap.Read(bitmap.Handle));
        Assert.True(VocabularyBitmapFixture.Alive(bitmap.Handle));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipboardCopiesBeforeCloseAndRejectsOwnerChangeWithoutGlobalMemoryCalls(bool changed)
    {
        using var bitmap = new VocabularyBitmapFixture(); var native = new BitmapClipboard(bitmap);
        var reader = new VocabularyClipboardImageReader(native);
        Assert.True(reader.HasAvailableImage()); Assert.Equal(0, native.Reads);
        native.ChangeOwner = changed; native.AfterClose = bitmap.Dispose;
        var result = reader.Read(7);
        Assert.Equal(1, native.Reads); Assert.Equal(1, native.Closes); Assert.Equal(0, bitmap.Handle);
        if (changed) Assert.Null(result); else Assert.Equal(VocabularyBitmapFixture.Pixels, result![54..]);
    }
    [Fact]
    public void OleGdiMediumIsCopiedAndReleasedOnceWhileWrongTymedIsRejected()
    {
        using var bitmap = new VocabularyBitmapFixture(); nint handle = bitmap.Handle;
        var source = new BitmapData(bitmap, TYMED.TYMED_GDI);
        Assert.True(VocabularyOleImageReader.CanRead(source)); Assert.Equal(0, source.Reads);
        byte[] result = VocabularyOleImageReader.Read(source)!;
        Assert.Equal(VocabularyBitmapFixture.Pixels, result[54..]); Assert.Equal(1, source.Reads);
        Assert.Equal(0, bitmap.Handle); Assert.False(VocabularyBitmapFixture.Alive(handle));
        using var other = new VocabularyBitmapFixture(); var wrong = new BitmapData(other, TYMED.TYMED_HGLOBAL);
        Assert.Throws<InvalidDataException>(() => VocabularyOleImageReader.Read(wrong)); Assert.Equal(1, wrong.Reads);
        Assert.True(VocabularyBitmapFixture.Alive(other.Handle));
    }
    [Fact]
    public void CompositeMediumTagIsRejectedWithoutTreatingItsUnionAsEitherHandleKind()
    {
        using var bitmap = new VocabularyBitmapFixture();
        var source = new BitmapData(bitmap, TYMED.TYMED_GDI | TYMED.TYMED_HGLOBAL);
        Assert.Throws<InvalidDataException>(() => VocabularyOleImageReader.Read(source));
        Assert.Equal(1, source.Reads); Assert.True(VocabularyBitmapFixture.Alive(bitmap.Handle));
    }
    private sealed class BitmapClipboard(VocabularyBitmapFixture bitmap) : IVocabularyImageClipboard
    {
        public uint Register(string name) => 100;
        public uint Sequence => 7;
        public nint Owner { get; private set; } = 10;
        internal int Reads, Closes; internal bool ChangeOwner; internal Action? AfterClose;
        public bool Open() => true;
        public void Close() { Closes++; AfterClose?.Invoke(); }
        public bool Available(uint format) => format == 2;
        public nint Data(uint format) => format == 2 ? bitmap.Handle : throw new InvalidOperationException();
        public byte[] ReadBitmap(nint handle) { Reads++; byte[] bytes = VocabularyClipboardBitmap.Read(handle); if (ChangeOwner) Owner++; return bytes; }
        public nuint Size(nint handle) => throw new InvalidOperationException("A bitmap is not HGLOBAL.");
        public nint Lock(nint handle) => throw new InvalidOperationException("A bitmap cannot be GlobalLocked.");
        public void Unlock(nint handle) => throw new InvalidOperationException();
    }
    private sealed class BitmapData(VocabularyBitmapFixture bitmap, TYMED returnedMedium) : IDataObject
    {
        internal int Reads;
        public int QueryGetData(ref FORMATETC format) => format.cfFormat == 2 && format.tymed == TYMED.TYMED_GDI ? 0 : unchecked((int)0x80040064);
        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            Reads++;
            medium = returnedMedium switch
            {
                TYMED.TYMED_GDI => new() { tymed = returnedMedium, unionmember = bitmap.Transfer() },
                TYMED.TYMED_HGLOBAL => new() { tymed = returnedMedium, unionmember = ClipboardNativeMethods.GlobalAlloc(2, 40) },
                _ => new() { tymed = returnedMedium, unionmember = 0 },
            };
        }
        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new NotImplementedException();
        public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output) { output = default; return 1; }
        public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release) => throw new NotImplementedException();
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction) => throw new NotImplementedException();
        public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) { connection = 0; return 1; }
        public void DUnadvise(int connection) => throw new NotImplementedException();
        public int EnumDAdvise(out IEnumSTATDATA? value) { value = null; return 1; }
    }
}
