namespace Cotabby.App.Branding;

/// <summary>User-visible product identity. Runtime, IPC, settings and storage identifiers remain
/// independently stable; presentation branding must never rename those effects implicitly.</summary>
internal static class AppBrand
{
    internal const string Name = "Lingrove";
    internal const string WindowsRepository = "https://github.com/ichthyoplanktonzyh/lingrove-windows";
    internal const string MacRepository = "https://github.com/ichthyoplanktonzyh/lingrove";
    internal const string WindowsAncestorRepository = "https://github.com/Msde-7/cotabby-windows";
    internal const string OriginalRepository = "https://github.com/FuJacob/cotabby";
    internal const string UserAgent = "Lingrove-Windows/0.6.2 (+https://github.com/ichthyoplanktonzyh/lingrove-windows)";
}
