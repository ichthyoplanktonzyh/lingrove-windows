using System.Runtime.InteropServices;

namespace Cotabby.Win32.Dictionary;

/// <summary>Mac uses Finder's localized-standard companion ordering. Windows borrows Shell's
/// numeric filename comparison for this import offer only, never canonical IDs/keys or hashing.</summary>
internal static partial class WindowsDictionaryFileOrder
{
    internal static IComparer<string> Instance { get; } = Comparer<string>.Create((left, right) =>
    {
        int result = StrCmpLogicalW(left, right);
        return result != 0 ? result : StringComparer.Ordinal.Compare(left, right);
    });
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int StrCmpLogicalW(string left, string right);
}
