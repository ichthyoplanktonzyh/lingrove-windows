using System.Globalization;
using Cotabby.Core.Suggestions;

namespace Cotabby.App.Localization;

/// <summary>Formats the existing pause state for the launch interface locale. Calendar/pause
/// decisions remain in Core; neither preference values nor the user's OS culture are changed.</summary>
internal static class PausePresentation
{
    internal static string? Status(PauseState? pause, DateTimeOffset now)
    {
        if (pause is null || !pause.IsActive(now)) return null;
        if (pause.Until is not { } until) return InterfaceText.Get("Paused until enabled");
        if (until == PauseState.For(PauseDuration.UntilTomorrow, now).Until) return InterfaceText.Get("Paused until tomorrow");
        var culture = CultureInfo.GetCultureInfo(InterfaceText.Locale); var local = until.ToLocalTime();
        string time = local.Date == now.ToLocalTime().Date ? local.ToString("t", culture) : local.ToString("g", culture);
        return InterfaceText.Format("Paused until {0}", time);
    }
}
