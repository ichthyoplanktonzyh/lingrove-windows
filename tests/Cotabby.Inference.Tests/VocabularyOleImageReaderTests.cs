using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests;

public sealed class VocabularyOleImageReaderTests
{
    [Fact]
    public void SyntheticPngIsDetachedAndSelectedOnce()
    {
        short png = unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("PNG"));
        var source = new Data(png, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, VocabularyOleImageReader.Read(source));
        Assert.Equal(1, source.Reads);
    }
    [Fact]
    public void OversizedGlobalDataFailsBeforeCopy()
    {
        short png = unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("PNG"));
        var source = new Data(png, new byte[VocabularyClipboardImageReader.MaximumBytes + 1]);
        Assert.Throws<InvalidDataException>(() => VocabularyOleImageReader.Read(source)); Assert.Equal(1, source.Reads);
    }
    [Fact]
    public void VirtualImageReadsOnlyFirstDescriptorAndFirstContents()
    {
        short descriptor = unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("FileGroupDescriptorW"));
        short contents = unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("FileContents"));
        byte[] group = new byte[596]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(group, 1);
        System.Text.Encoding.Unicode.GetBytes("image.png\0").CopyTo(group, 76);
        var source = new VirtualData(descriptor, contents, group);
        Assert.Equal(new byte[] { 9, 8, 7 }, VocabularyOleImageReader.Read(source)); Assert.Equal(0, source.LastIndex);
        group[76] = 0; Assert.Throws<InvalidDataException>(() => VocabularyOleImageReader.Read(source));
    }
    [Fact]
    public void ExistingFileOfferReadsOnlyTheFirstBoundedRegularLeaf()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ole-drop-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string first = Path.Combine(folder, "first.png"), second = Path.Combine(folder, "second.png"); File.WriteAllBytes(first, [3, 2, 1]); File.WriteAllBytes(second, [7]);
            byte[] paths = System.Text.Encoding.Unicode.GetBytes(first + "\0" + second + "\0\0"), drop = new byte[20 + paths.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(drop, 20); System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(drop.AsSpan(16), 1); paths.CopyTo(drop, 20);
            Assert.Equal(new byte[] { 3, 2, 1 }, VocabularyOleImageReader.Read(new Data(15, drop)));
            byte[] dir = System.Text.Encoding.Unicode.GetBytes(folder + "\0\0"), invalid = new byte[20 + dir.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(invalid, 20); System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), 1); dir.CopyTo(invalid, 20);
            Assert.Throws<InvalidDataException>(() => VocabularyOleImageReader.Read(new Data(15, invalid)));
        }
        finally { Directory.Delete(folder, true); }
    }
    private sealed class VirtualData(short descriptor, short contents, byte[] group) : IDataObject
    {
        internal int LastIndex = -1;
        public int QueryGetData(ref FORMATETC format) => format.cfFormat == descriptor || format.cfFormat == contents ? 0 : unchecked((int)0x80040064);
        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            byte[] bytes = format.cfFormat == descriptor ? group : [9, 8, 7]; if (format.cfFormat == contents) LastIndex = format.lindex;
            IntPtr memory = ClipboardNativeMethods.GlobalAlloc(2, (nuint)bytes.Length); IntPtr pointer = ClipboardNativeMethods.GlobalLock(memory);
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); } finally { ClipboardNativeMethods.GlobalUnlock(memory); }
            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
        }
        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new NotImplementedException();
        public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output) { output = default; return 1; }
        public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release) => throw new NotImplementedException();
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction) => throw new NotImplementedException();
        public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) { connection = 0; return 1; }
        public void DUnadvise(int connection) => throw new NotImplementedException();
        public int EnumDAdvise(out IEnumSTATDATA? value) { value = null; return 1; }
    }
    private sealed class Data(short offered, byte[] bytes) : IDataObject
    {
        internal int Reads;
        public int QueryGetData(ref FORMATETC format) => format.cfFormat == offered ? 0 : unchecked((int)0x80040064);
        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            Reads++; IntPtr memory = ClipboardNativeMethods.GlobalAlloc(2, (nuint)bytes.Length);
            IntPtr pointer = ClipboardNativeMethods.GlobalLock(memory);
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); } finally { ClipboardNativeMethods.GlobalUnlock(memory); }
            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
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
