using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.ImageTransferHost;

/// <summary>A one-drop window whose process, rather than the editor, owns every foreign OLE call.
/// This window and protocol are not wired to the product until the parent can bound and join it.</summary>
public static class OleDropWindow
{
    private const string WindowClass = "CotabbyImageDropSurface";
    private static readonly WindowProcedure Procedure = PaintWindow;
    internal static int Run(string parentText, bool test = false, string label = "Drop image here")
    {
        if (!long.TryParse(parentText, NumberStyles.None, CultureInfo.InvariantCulture, out long parent) || parent <= 0) return 2;
        if (string.IsNullOrWhiteSpace(label) || label.Length > 120 || label.Any(char.IsControl)) return 2;
        // The parent attaches the process to its kill-on-close job before admitting OLE work.
        using var control = Console.OpenStandardInput();
        if (control.ReadByte() != 'G' || control.ReadByte() != 'O') return 2;
        int newline = control.ReadByte(); if (newline == '\r') newline = control.ReadByte(); if (newline != '\n') return 2;
        int initialized = OleInitialize(IntPtr.Zero); if (initialized < 0) return 2;
        IntPtr hwnd = IntPtr.Zero, targetPointer = IntPtr.Zero; bool registered = false;
        try
        {
            var windowClass = new WindowClassInfo { Size = (uint)Marshal.SizeOf<WindowClassInfo>(), Procedure = Marshal.GetFunctionPointerForDelegate(Procedure), ClassName = WindowClass };
            if (RegisterClassExW(ref windowClass) == 0) return 2;
            hwnd = CreateWindowExW(0, WindowClass, label, 0x50000000, 0, 0, 1, 1, new IntPtr(parent), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return 2;
            using var output = Console.OpenStandardOutput(); var target = new Target(output, GetCurrentThreadId(), test);
            targetPointer = Marshal.GetComInterfaceForObject(target, typeof(IDropTarget));
            if (RegisterDragDrop(hwnd, targetPointer) < 0) return 2; registered = true;
            byte[] ready = Encoding.ASCII.GetBytes("READY " + hwnd.ToInt64().ToString(CultureInfo.InvariantCulture) + "\n"); output.Write(ready); output.Flush();
            if (test) { byte[] wire = Encoding.ASCII.GetBytes("TARGET " + MarshalTarget(targetPointer) + "\n"); output.Write(wire); output.Flush(); }
            while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessageW(ref message); }
            GC.KeepAlive(target); return 0;
        }
        finally
        {
            if (registered) _ = RevokeDragDrop(hwnd);
            if (targetPointer != IntPtr.Zero) Marshal.Release(targetPointer);
            if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
            OleUninitialize();
        }
    }
    private static IntPtr PaintWindow(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam)
    {
        if (message == 0x0014) return new IntPtr(1); // WM_ERASEBKGND
        if (message != 0x000F) return DefWindowProcW(hwnd, message, wparam, lparam);
        IntPtr dc = BeginPaint(hwnd, out var paint);
        try
        {
            GetClientRect(hwnd, out var bounds);
            IntPtr background = CreateSolidBrush(0x00FFF5ED), border = CreateSolidBrush(0x00E6D2BB);
            try { FillRect(dc, ref bounds, background); FrameRect(dc, ref bounds, border); }
            finally { DeleteObject(background); DeleteObject(border); }
            _ = SetBkMode(dc, 1); _ = SetTextColor(dc, 0x00755638);
            int fontHeight = -Math.Max(1, (int)Math.Round(13.0 * GetDpiForWindow(hwnd) / 96));
            IntPtr font = CreateFontW(fontHeight, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
            IntPtr old = SelectObject(dc, font);
            try { var text = new StringBuilder(121); _ = GetWindowTextW(hwnd, text, text.Capacity); _ = DrawTextW(dc, text.ToString(), -1, ref bounds, 0x00000001 | 0x00000004 | 0x00000020); }
            finally { SelectObject(dc, old); DeleteObject(font); }
        }
        finally { EndPaint(hwnd, ref paint); }
        return IntPtr.Zero;
    }
    private static string MarshalTarget(IntPtr pointer)
    {
        if (CreateStreamOnHGlobal(IntPtr.Zero, true, out IntPtr stream) < 0) throw new InvalidOperationException();
        try
        {
            Guid iid = new("00000122-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(CoMarshalInterface(stream, ref iid, pointer, 0, IntPtr.Zero, 0));
            Marshal.ThrowExceptionForHR(GetHGlobalFromStream(stream, out IntPtr memory));
            var managed = (IStream)Marshal.GetObjectForIUnknown(stream);
            try
            {
                managed.Stat(out STATSTG stat, 1); if (stat.cbSize <= 0 || stat.cbSize > 65536) throw new InvalidOperationException();
                IntPtr data = Cotabby.Win32.Clipboard.ClipboardNativeMethods.GlobalLock(memory); if (data == IntPtr.Zero) throw new InvalidOperationException();
                try { byte[] bytes = new byte[(int)stat.cbSize]; Marshal.Copy(data, bytes, 0, bytes.Length); return Convert.ToBase64String(bytes); }
                finally { Cotabby.Win32.Clipboard.ClipboardNativeMethods.GlobalUnlock(memory); }
            }
            finally { Marshal.ReleaseComObject(managed); }
        }
        finally { Marshal.Release(stream); }
    }
    [ComVisible(true), Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig] int DragEnter([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keys, Point point, ref uint effect);
        [PreserveSig] int DragOver(uint keys, Point point, ref uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keys, Point point, ref uint effect);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Target(Stream output, uint ownerThread, bool diagnostic = false) : IDropTarget
    {
        private int _admitted;
        private int _accepted;
        public int DragEnter(IDataObject data, uint keys, Point point, ref uint effect)
        { try { _accepted = VocabularyOleImageReader.CanRead(data) ? 1 : 0; } catch { _accepted = 0; } effect &= (uint)_accepted; return 0; }
        public int DragOver(uint keys, Point point, ref uint effect) { effect &= (uint)Volatile.Read(ref _accepted); return 0; }
        public int DragLeave() { _accepted = 0; return 0; }
        public int Drop(IDataObject data, uint keys, Point point, ref uint effect)
        {
            if (Interlocked.Exchange(ref _admitted, 1) != 0) { effect = 0; return 0; }
            effect &= 1;
            try
            {
                output.Write("DROP\n"u8); output.Flush();
                byte[]? bytes = VocabularyOleImageReader.Read(data);
                VocabularyImageTransferProtocol.Write(output, bytes); if (bytes is null) effect = 0;
            }
            catch (Exception error)
            {
                effect = 0;
                if (diagnostic) Console.Error.WriteLine(error.GetType().Name + " " + error.HResult.ToString("X8", CultureInfo.InvariantCulture));
                try { VocabularyImageTransferProtocol.Write(output, null, failed: true); } catch { }
            }
            finally { _ = PostThreadMessageW(ownerThread, 0x0012, UIntPtr.Zero, IntPtr.Zero); }
            return 0;
        }
    }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Paint { internal IntPtr Dc; [MarshalAs(UnmanagedType.Bool)] internal bool Erase; internal Rect Rect; [MarshalAs(UnmanagedType.Bool)] internal bool Restore, Incremental; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] internal byte[] Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClassInfo
    { internal uint Size, Style; internal IntPtr Procedure; internal int ClassExtra, WindowExtra; internal IntPtr Instance, Icon, Cursor, Background; internal string? MenuName; internal string ClassName; internal IntPtr SmallIcon; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Hwnd; public uint Id; public UIntPtr Wparam; public IntPtr Lparam; public uint Time; public Point Point; public uint Private; }
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void OleUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CreateStreamOnHGlobal(IntPtr memory, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out IntPtr stream);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int GetHGlobalFromStream(IntPtr stream, out IntPtr memory);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoMarshalInterface(IntPtr stream, ref Guid iid, IntPtr value, uint context, IntPtr destination, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int RegisterDragDrop(IntPtr hwnd, IntPtr target);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int RevokeDragDrop(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClassInfo cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr BeginPaint(IntPtr hwnd, out Paint paint);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EndPaint(IntPtr hwnd, ref Paint paint);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int FrameRect(IntPtr dc, ref Rect rect, IntPtr brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int DrawTextW(IntPtr dc, string text, int count, ref Rect rect, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strike, uint charset, uint precision, uint clip, uint quality, uint pitch, string family);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetMessageW(out Message message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr DispatchMessageW(ref Message message);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessageW(uint thread, uint message, UIntPtr wparam, IntPtr lparam);
}
