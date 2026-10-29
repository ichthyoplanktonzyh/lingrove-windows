using System.Diagnostics;
using System.Text.Json.Nodes;
using Cotabby.Core.CLI;
using Cotabby.Win32.CLI;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests.CLI;

public sealed class CliCommandInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cli-install-目录-%!&-" + Guid.NewGuid().ToString("N"));
    public CliCommandInstallationTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    private string App()
    {
        string appDir = Path.Combine(_root, "app"); Directory.CreateDirectory(appDir); File.WriteAllText(Path.Combine(appDir, "Cotabby.exe"), "fixture");
        foreach (string directory in new[] { "cli-launcher", "cli-helper" })
            foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, directory))) File.Copy(file, Path.Combine(appDir, Path.GetFileName(file)));
        return Path.Combine(appDir, "Cotabby.exe");
    }
    [Fact]
    public void InstallsOnlyExactOwnedCommandAndPreservesForeignFile()
    {
        string app = App(), bin = Path.Combine(_root, "bin"); string command = CliCommandInstallation.Install(app, bin);
        Assert.Equal(command, CliCommandInstallation.Install(app, bin));
        string script = File.ReadAllText(command); Assert.Contains("DisableDelayedExpansion", script);
        Assert.DoesNotContain("目录", script); Assert.Contains("%~dp0", script);
        File.WriteAllText(command, "foreign");
        Assert.Equal(CliErrorCode.Conflict, Assert.Throws<CliException>(() => CliCommandInstallation.Install(app, bin)).Code);
        Assert.Equal("foreign", File.ReadAllText(command));
    }
    [Fact]
    public async Task UnicodeCommandForwardsArgumentsStandardInputAndExitCode()
    {
        string app = App(), bin = Path.Combine(_root, "bin"); string command = CliCommandInstallation.Install(app, bin);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true };
        // cmd /s /c requires the whole command in one quoted argument; the path and the
        // argument containing '&' are independently quoted just as an interactive user must.
        start.Arguments = "/d /v:off /s /c \"\"" + command + "\" --option \"two & words\" 100% bang!\"";
        using var process = Process.Start(start)!;
        try
        {
            Assert.Equal("ready", await process.StandardError.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Throws<IOException>(() => File.Move(app, app + ".old"));
            Assert.Throws<IOException>(() => File.Move(Path.Combine(Path.GetDirectoryName(app)!, "lingrove.exe"), Path.Combine(Path.GetDirectoryName(app)!, "old.exe")));
            Assert.Throws<IOException>(() => Directory.Move(Path.GetDirectoryName(app)!, Path.Combine(_root, "moved")));
            process.StandardInput.Write("stdin 内容"); process.StandardInput.Close();
            string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd(); process.WaitForExit();
            Assert.True(process.ExitCode == 17, $"exit {process.ExitCode}: {error} {output}");
            var result = JsonNode.Parse(output)!; Assert.Equal(new[] { "--option", "two & words", "100%", "bang!" }, result["args"]!.AsArray().Select(value => value!.GetValue<string>()));
            Assert.Equal("stdin 内容", result["input"]!.GetValue<string>());
            Assert.Equal(CliApplicationIdentity.Resolve(app).Executable, result["app"]!.GetValue<string>());
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
