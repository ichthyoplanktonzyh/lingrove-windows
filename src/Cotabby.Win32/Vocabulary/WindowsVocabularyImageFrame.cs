using System.Runtime.InteropServices;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Owns a WIC decoder, exactly frame zero and its transform objects on one synchronous worker.
/// WPF BitmapDecoder.Frames allocates wrappers for every frame, even with DelayCreation. Direct
/// WIC GetFrame(0) preserves Mac ImageIO's frame-zero boundary before dimension admission.
/// Slots/signatures follow Microsoft's Windows SDK wincodec.h; every acquired COM reference is
/// released in reverse order before the memory stream and this call's COM apartment are retired.
/// No pointer escapes this owner and no asynchronous continuation crosses its lifetime.
/// </summary>
internal sealed unsafe class WindowsVocabularyImageFrame : IDisposable
{
    private readonly List<nint> _owned = [];
    private nint _factory, _frame;
    private bool _uninitialize;
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal int Orientation { get; private set; } = 1;
    internal Guid Container { get; private set; }
    internal uint ColorContextCount { get; private set; }

    internal WindowsVocabularyImageFrame(ReadOnlySpan<byte> data)
    {
        try
        {
            int status = CoInitializeEx(0, 0); // COINIT_MULTITHREADED, or borrow an existing STA.
            if (status >= 0) _uninitialize = true;
            else if (status != unchecked((int)0x80010106)) Check(status); // RPC_E_CHANGED_MODE
            nint stream;
            fixed (byte* bytes = data) stream = Own(SHCreateMemStream(bytes, checked((uint)data.Length)));
            Guid factoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a"), factoryInterface = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
            nint factory = 0; Check(CoCreateInstance(&factoryClass, 0, 1, &factoryInterface, &factory)); _factory = Own(factory);
            nint decoder = 0;
            // IWICImagingFactory::CreateDecoderFromStream, WICDecodeMetadataCacheOnDemand = 0.
            Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, uint, nint*, int>)Slots(_factory)[4])(_factory, stream, null, 0, &decoder)); Own(decoder);
            Guid container; Check(((delegate* unmanaged[Stdcall]<nint, Guid*, int>)Slots(decoder)[5])(decoder, &container)); Container = container;
            nint frame = 0; Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slots(decoder)[13])(decoder, 0, &frame)); _frame = Own(frame);
            (Width, Height) = Size(_frame); Orientation = ReadOrientation();
            uint contexts = 0;
            int colors = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, uint*, int>)Slots(_frame)[9])(_frame, 0, null, &contexts);
            // Success with zero contexts means unprofiled. Unsupported is not that evidence:
            // stripping unknown profile metadata after a failed query would change colors.
            Check(colors); ColorContextCount = contexts;
        }
        catch { Dispose(); throw; }
    }
    internal (byte[] Pixels, int Width, int Height) CopyPixels(int width, int height, CancellationToken cancellationToken)
    {
        if (width is <= 0 or > VocabularyContentPolicy.MaximumDimension || height is <= 0 or > VocabularyContentPolicy.MaximumDimension) throw new COMException();
        nint source = _frame;
        if (width != Width || height != Height)
        {
            nint scaler = Create(11); // IWICBitmapScaler::Initialize is after IWICBitmapSource.
            Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, int>)Slots(scaler)[8])(scaler, source, (uint)width, (uint)height, 3)); source = scaler;
        }
        cancellationToken.ThrowIfCancellationRequested();
        Guid format = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");
        if (ColorContextCount > 0)
        {
            // A profile cannot simply be dropped after reinterpreting its pixel values. WIC
            // converts the frame's primary source context to explicit sRGB before metadata is
            // discarded. Unsupported profile/format combinations fail instead of guessing.
            nint originalContext = Create(15), context = originalContext; uint actual = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, uint*, int>)Slots(_frame)[9])(_frame, 1, &context, &actual));
            if (context != originalContext) Own(context);
            nint srgb = Create(15); Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slots(srgb)[5])(srgb, 1));
            nint colors = Create(16);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, Guid*, int>)Slots(colors)[8])(colors, source, context, srgb, &format)); source = colors;
        }
        nint converter = Create(10);
        Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, uint, nint, double, uint, int>)Slots(converter)[8])(converter, source, &format, 0, 0, 0, 0));
        (int outputWidth, int outputHeight) = Size(converter);
        if (outputWidth != width || outputHeight != height) throw new COMException();
        int stride = checked(outputWidth * 4); var pixels = new byte[checked(stride * outputHeight)];
        fixed (byte* destination = pixels)
            Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, byte*, int>)Slots(converter)[7])(converter, 0, (uint)stride, (uint)pixels.Length, destination));
        cancellationToken.ThrowIfCancellationRequested(); return (pixels, outputWidth, outputHeight);
    }
    private int ReadOrientation()
    {
        nint reader = 0;
        int status = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slots(_frame)[8])(_frame, &reader);
        if (status < 0 || reader == 0) return 1;
        Own(reader);
        foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            PropVariant value = default;
            try
            {
                fixed (char* name = query) status = ((delegate* unmanaged[Stdcall]<nint, char*, PropVariant*, int>)Slots(reader)[5])(reader, name, &value);
                if (status >= 0 && value.Type == 18 && value.UnsignedShort is >= 1 and <= 8) return value.UnsignedShort; // VT_UI2
            }
            finally { _ = PropVariantClear(ref value); }
        }
        return 1;
    }
    private nint Create(int slot)
    { nint value = 0; Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slots(_factory)[slot])(_factory, &value)); return Own(value); }
    private nint Own(nint value)
    { if (value == 0) throw new COMException(); _owned.Add(value); return value; }
    private static (int Width, int Height) Size(nint source)
    {
        uint width, height; Check(((delegate* unmanaged[Stdcall]<nint, uint*, uint*, int>)Slots(source)[3])(source, &width, &height));
        return (checked((int)width), checked((int)height));
    }
    private static void** Slots(nint value) => *(void***)value;
    private static void Check(int status) { if (status < 0) Marshal.ThrowExceptionForHR(status); }
    public void Dispose()
    {
        for (int index = _owned.Count - 1; index >= 0; --index) _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Slots(_owned[index])[2])(_owned[index]);
        _owned.Clear(); _factory = _frame = 0;
        if (_uninitialize) { _uninitialize = false; CoUninitialize(); }
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    { [FieldOffset(0)] internal ushort Type; [FieldOffset(8)] internal ushort UnsignedShort; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int PropVariantClear(ref PropVariant value);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shlwapi.dll", ExactSpelling = true)] private static extern nint SHCreateMemStream(byte* initial, uint count);
}
