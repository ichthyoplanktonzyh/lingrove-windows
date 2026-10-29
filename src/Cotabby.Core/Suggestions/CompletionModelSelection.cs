using Cotabby.Core.Models;

namespace Cotabby.Core.Suggestions;

/// <summary>Frozen bootstrap reconciliation over discovered installed candidates. Retained
/// incompatible or unrecognized identities are not replaced; a compatible vanished choice falls
/// back to the first installed candidate, or clears if none exist.</summary>
public static class CompletionModelSelection
{
    public readonly record struct PowerProfileState(bool Active, bool OnBattery, string BatteryModelId, string PluggedInModelId)
    {
        public string ModelId => OnBattery ? BatteryModelId : PluggedInModelId;
    }
    /// <summary>Either profile edit triggers resolution of the current power source.</summary>
    public static bool PowerChanged(PowerProfileState? previous, PowerProfileState next) =>
        previous is null || previous.Value.Active != next.Active || next.Active &&
        (previous.Value.OnBattery != next.OnBattery || previous.Value.BatteryModelId != next.BatteryModelId
            || previous.Value.PluggedInModelId != next.PluggedInModelId);

    /// <summary>The resolved model and retained ID together define local admission identity.</summary>
    public readonly record struct SelectionIdentity(string? SelectedId, string? ResolvedId);

    private static readonly string[] Preferred = ["tabby-2-base", "tabby-2-mini", "tabby-2-nano", "tabby-2-pro"];

    /// <summary>Mac's preferred installed models precede alphabetically ordered custom files.</summary>
    public static IReadOnlyList<CotabbyModel> Ordered(IEnumerable<CotabbyModel> installed) => installed
        .OrderBy(model => { int rank = Array.IndexOf(Preferred, model.Id); return rank < 0 ? Preferred.Length : rank; })
        .ThenBy(model => model.FileName, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public static string? Reconcile(string? retainedId, IReadOnlyList<CotabbyModel> installed)
    {
        var ordered = Ordered(installed);
        if (string.IsNullOrEmpty(retainedId)) return ordered.FirstOrDefault()?.Id;
        string? filename = retainedId.StartsWith("custom:", StringComparison.Ordinal)
            ? retainedId["custom:".Length..]
            : ModelCatalog.All.FirstOrDefault(model => model.Id == retainedId)?.FileName;
        if (string.IsNullOrEmpty(filename) || filename.IndexOfAny(['/', '\\', ':', '\0']) >= 0
            || !LocalModelTaskPolicy.Supports(filename, LocalModelTask.Completion)) return retainedId;
        return ordered.FirstOrDefault(model => ModelCatalog.MatchesId(retainedId, model.Id)) is not null
            ? retainedId : ordered.FirstOrDefault()?.Id;
    }

    /// <summary>A changed power profile selects its installed model; unchanged, empty or
    /// unavailable profiles preserve the current explicit selection.</summary>
    public static string? ApplyPower(PowerProfileState? previous, PowerProfileState next, string? selectedId, string? installedProfileId, bool discoveryChanged = false) =>
        (discoveryChanged || PowerChanged(previous, next)) && next.Active && next.ModelId.Length > 0 && installedProfileId is not null
            ? installedProfileId : selectedId;
}
