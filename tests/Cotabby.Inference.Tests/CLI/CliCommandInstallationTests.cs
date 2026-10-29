using System.Diagnostics;
using Cotabby.Core.CLI;
using Cotabby.Win32.CLI;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests.CLI;

public sealed class CliCommandInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cli-install-%!&-" + Guid.NewGuid().ToString("N"));
    public CliCommandInstallationTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    [Fact]
    public void InstallsOnlyExactOwnedCommandAndPreservesForeignFile()
    {
        string appDir = Path.Combine(_root, "app"); Directory.CreateDirectory(appDir);
        foreach (var name in new[] { "Cotabby.exe", "lingrove.exe", "lingrove.dll", "lingrove.deps.json", "lingrove.runtimeconfig.json" }) File.WriteAllText(Path.Combine(appDir, name), "fixture");
        string app = Path.Combine(appDir, "Cotabby.exe"), bin = Path.Combine(_root, "bin");
        string command = CliCommandInstallation.Install(app, bin);
        Assert.Equal(command, CliCommandInstallation.Install(app, bin));
        string script = File.ReadAllText(command); Assert.Contains("DisableDelayedExpansion", script); Assert.Contains("%%", script);
        File.WriteAllText(command, "foreign");
        Assert.Equal(CliErrorCode.Conflict, Assert.Throws<CliException>(() => CliCommandInstallation.Install(app, bin)).Code);
        Assert.Equal("foreign", File.ReadAllText(command));
    }
    [Fact]
    public void BatchEscapingKeepsPercentAndExclamationUnderDelayedExpansion()
    {
        string app = Path.Combine(_root, "Cotabby.exe"), script = Path.Combine(Path.GetTempPath(), "cli-script-" + Guid.NewGuid().ToString("N") + ".cmd");
        string shell = Environment.GetEnvironmentVariable("ComSpec")!;
        File.WriteAllText(script, CliCommandInstallation.BuildScript(shell, app));
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("/d"); start.ArgumentList.Add("/v:on"); start.ArgumentList.Add("/c"); start.ArgumentList.Add(script);
        start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("set"); start.ArgumentList.Add("LINGROVE_CLI_APP");
        try
        {
            using var process = Process.Start(start)!; string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd(); process.WaitForExit();
            Assert.True(process.ExitCode == 0, error); Assert.Contains("LINGROVE_CLI_APP=" + app, output, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(script); }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
