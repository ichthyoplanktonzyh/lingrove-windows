using System.Runtime.InteropServices;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Win32.Text;

/// <summary>Windows ICU script-property adapter for automatic writing eligibility. It is inert
/// until an already-authorized local capture is checked; missing ICU fails closed.</summary>
public static partial class IcuWritingScriptEvidence
{
    public static bool Accepts(string text, LearningLanguage language)
    {
        try { return WritingAutomaticLanguagePolicy.Accepts(text, language, Script); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return false; }
    }
    private static unsafe int? Script(int scalar)
    {
        int status = 0; int result = uscript_getScript(scalar, &status); return status <= 0 && result >= 0 ? result : null;
    }
    [LibraryImport("icu.dll", EntryPoint = "uscript_getScript")]
    private static unsafe partial int uscript_getScript(int scalar, int* status);
}
