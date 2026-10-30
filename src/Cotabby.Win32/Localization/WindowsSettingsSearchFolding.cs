using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cotabby.Core.Settings;

namespace Cotabby.Win32.Localization;

public sealed class WindowsSettingsSearchUnavailableException() : Exception("Windows search text services are unavailable.");

/// <summary>One locale-frozen settings-search adapter. ICU supplies full Unicode case folding;
/// the portable projection owns Foundation's accent/width and canonical-comparison semantics.
/// No query cache, settings mutation, credential or text transmission is introduced.</summary>
public sealed partial class WindowsSettingsSearchFolding : ISettingsSearchFolding
{
    private readonly bool _turkic;
    public WindowsSettingsSearchFolding(string locale)
    {
        string supported = InterfaceLanguagePolicy.Normalize(locale) ?? throw new ArgumentException("Unsupported interface locale.", nameof(locale));
        _turkic = supported == "tr";
    }
    public string Fold(string value) => SettingsSearchNormalization.Fold(value, _turkic, CaseFold);
    private static unsafe string CaseFold(string value, bool turkic)
    {
        fixed (char* source = value)
        {
            char* target = stackalloc char[16]; int status = 0;
            int length = u_strFoldCase(target, 16, source, value.Length, turkic ? 1u : 0u, &status);
            if (status > 0 || length is < 1 or > 15) throw new WindowsSettingsSearchUnavailableException();
            return new string(target, 0, length);
        }
    }
    [LibraryImport("icu.dll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int u_strFoldCase(char* target, int capacity, char* source, int length, uint options, int* status);
}
