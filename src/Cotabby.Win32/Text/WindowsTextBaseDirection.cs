using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cotabby.Win32.Text;

public enum WindowsTextDirection { LeftToRight, RightToLeft, Neutral, Unavailable }

/// <summary>Lazy Windows ICU boundary for a label's Unicode first-strong base direction. It does
/// not infer a language or rewrite/reverse text. The graph renderer supplies that direction to
/// WPF shaping, adapting AppKit's natural paragraph direction while keeping OS APIs out of Core.</summary>
public static partial class WindowsTextBaseDirection
{
    public static WindowsTextDirection Detect(string value)
    {
        ArgumentNullException.ThrowIfNull(value); if (value.Length == 0) return WindowsTextDirection.Neutral;
        try
        {
            return GetBaseDirection(value, value.Length) switch
            { 0 => WindowsTextDirection.LeftToRight, 1 => WindowsTextDirection.RightToLeft, 3 => WindowsTextDirection.Neutral, _ => WindowsTextDirection.Unavailable };
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException)
        { return WindowsTextDirection.Unavailable; }
    }
    // LibraryImport pins/copies the UTF-16 string only for this synchronous call. The explicit
    // code-unit length includes embedded NULs and excludes the marshaller's terminator. ICU
    // retains no pointer and allocates no iterator; the result is its UBiDiDirection C enum.
    [LibraryImport("icu.dll", EntryPoint = "ubidi_getBaseDirection", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetBaseDirection(string value, int length);
}
