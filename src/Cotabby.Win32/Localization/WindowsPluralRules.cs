using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cotabby.Core.Settings;

namespace Cotabby.Win32.Localization;

/// <summary>Windows' installed ICU owns locale cardinal grammar. This adapter accepts only
/// supported interface locales and nonnegative UI counts; it owns and closes one native rules
/// value per call. Missing platform capability returns no category, never a guessed translation.</summary>
public static partial class WindowsPluralRules
{
    public static string? Cardinal(string locale, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (InterfaceLanguagePolicy.Normalize(locale) is not { } supported) return null;
        try { return Read(supported, count); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return null; }
    }
    private static unsafe string? Read(string locale, int count)
    {
        int status = 0;
        nint rules = uplrules_open(locale, &status);
        if (rules == 0) return null;
        try
        {
            if (status > 0) return null; // ICU negative warning values remain successful.
            char* buffer = stackalloc char[16];
            int length = uplrules_select(rules, count, buffer, 16, &status);
            if (status > 0 || length is < 1 or > 15) return null;
            string category = new(buffer, 0, length);
            return category is "zero" or "one" or "two" or "few" or "many" or "other" ? category : null;
        }
        finally { uplrules_close(rules); }
    }
    [LibraryImport("icu.dll", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial nint uplrules_open(string locale, int* status);
    [LibraryImport("icu.dll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int uplrules_select(nint rules, double number, char* keyword, int capacity, int* status);
    [LibraryImport("icu.dll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void uplrules_close(nint rules);
}
