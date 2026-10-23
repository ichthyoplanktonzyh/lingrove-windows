using System.Diagnostics;
using System.IO;

namespace Cotabby.App.Wpf.Tests;

public sealed class VocabularyImageTransferHostTests
{
    [Fact]
    public async Task OrdinaryAppBuildCarriesAWorkingHostWithNoWpfRuntimeDependency()
    {
        foreach (string suffix in new[] { ".exe", ".dll", ".deps.json", ".runtimeconfig.json" })
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Cotabby.ImageTransferHost" + suffix)));
        string runtime = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cotabby.ImageTransferHost.runtimeconfig.json"));
        Assert.DoesNotContain("Microsoft.WindowsDesktop.App", runtime);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Cotabby.ImageTransferHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // Invalid arguments return before registering or reading any clipboard format.
        start.ArgumentList.Add("--invalid"); using var child = Process.Start(start)!;
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(2, child.ExitCode);
            Assert.Empty(await child.StandardOutput.ReadToEndAsync()); Assert.Empty(await child.StandardError.ReadToEndAsync());
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
    }
}
