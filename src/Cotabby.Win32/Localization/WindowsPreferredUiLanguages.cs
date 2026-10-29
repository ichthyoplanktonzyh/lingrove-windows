using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Cotabby.Win32.Localization;

/// <summary>Reads the ordered Windows user display-language list. No app or system preference is
/// changed. Failure or a racing size change returns an empty list for the caller's fallback.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsPreferredUiLanguages
{
    private const uint MuiLanguageName = 8;
    private const uint MaximumCharacters = 65_536;
    public static IReadOnlyList<string> Read()
    {
        uint size = 0;
        if (!GetUserPreferredUILanguages(MuiLanguageName, out _, null, ref size) || size is < 2 or > MaximumCharacters) return [];
        var buffer = new char[size];
        if (!GetUserPreferredUILanguages(MuiLanguageName, out uint count, buffer, ref size) || size > buffer.Length || count > 256) return [];
        return new string(buffer, 0, (int)size).Split('\0', StringSplitOptions.RemoveEmptyEntries).Take((int)count).ToArray();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserPreferredUILanguages(uint flags, out uint count, [Out] char[]? buffer, ref uint size);
}
