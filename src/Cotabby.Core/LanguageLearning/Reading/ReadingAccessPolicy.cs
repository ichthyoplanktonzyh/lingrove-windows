using Cotabby.Core.Input;

namespace Cotabby.Core.LanguageLearning.Reading;

public enum ReadingSourceKind { Selection, Pointer, Screenshot }
/// <summary>The current Mac source requires an explicit per-app grant for every shortcut;
/// only the passive icon inherits its global setting. Unknown process names grant nothing.</summary>
public static class ReadingAccessPolicy
{
    private static PerAppShortcutOverride? Find(string? process, IReadOnlyList<PerAppShortcutOverride> rows) =>
        string.IsNullOrWhiteSpace(process) ? null : rows.FirstOrDefault(row => string.Equals(row.ProcessName, process, StringComparison.OrdinalIgnoreCase));
    public static bool Allowed(ReadingSourceKind source, string? process, IReadOnlyList<PerAppShortcutOverride> rows)
    {
        var row = Find(process, rows); return source switch
        {
            ReadingSourceKind.Selection => row?.SelectionReadingAllowed == true,
            ReadingSourceKind.Pointer => row?.PointerReadingAllowed == true,
            ReadingSourceKind.Screenshot => row?.ScreenshotReadingAllowed == true,
            _ => false,
        };
    }
    public static bool IconAllowed(string? process, bool global, IReadOnlyList<PerAppShortcutOverride> rows) =>
        !string.IsNullOrWhiteSpace(process) && (Find(process, rows)?.SelectionIconEnabled ?? global);
}
