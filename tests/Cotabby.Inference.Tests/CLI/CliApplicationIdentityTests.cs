using Cotabby.Core.CLI;
using Cotabby.Win32.CLI;

namespace Cotabby.Inference.Tests.CLI;

public sealed class CliApplicationIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cli-identity-" + Guid.NewGuid().ToString("N"));
    public CliApplicationIdentityTests() => Directory.CreateDirectory(_root);
    [Fact]
    public void DistinctExecutablePathsHaveDistinctMailboxes()
    {
        string one = Path.Combine(_root, "one"), two = Path.Combine(_root, "two"); Directory.CreateDirectory(one); Directory.CreateDirectory(two);
        string first = Path.Combine(one, "Cotabby.exe"), second = Path.Combine(two, "Cotabby.exe"); File.WriteAllText(first, "fixture"); File.WriteAllText(second, "fixture");
        var a = CliApplicationIdentity.Resolve(first, _root); var b = CliApplicationIdentity.Resolve(second, _root);
        Assert.NotEqual(a.MailboxRoot, b.MailboxRoot); Assert.Equal(a.MailboxRoot, CliApplicationIdentity.Resolve(first.ToUpperInvariant(), _root).MailboxRoot);
        string junction = Path.Combine(_root, "alias");
        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            { ArgumentList = { "/c", "mklink", "/J", junction, one }, UseShellExecute = false, CreateNoWindow = true })!)
        { process.WaitForExit(); Assert.Equal(0, process.ExitCode); }
        Assert.Equal(a.MailboxRoot, CliApplicationIdentity.Resolve(Path.Combine(junction, "Cotabby.exe"), _root).MailboxRoot);
        using (var selection = CliApplicationSelection.Open(first, _root))
        {
            selection.Verify();
            Assert.Throws<IOException>(() => File.Move(first, Path.Combine(one, "old.exe")));
            Assert.Throws<IOException>(() => Directory.Move(one, Path.Combine(_root, "moved")));
        }
        File.Move(first, Path.Combine(one, "old.exe")); File.WriteAllText(first, "fixture");
        Assert.Equal(a.MailboxRoot, CliApplicationIdentity.Resolve(first, _root).MailboxRoot);
        Assert.Throws<CliException>(() => CliApplicationIdentity.Resolve(Path.Combine(one, "other.exe"), _root));
    }
    public void Dispose()
    {
        string junction = Path.Combine(_root, "alias");
        if (Directory.Exists(junction)) Directory.Delete(junction);
        Directory.Delete(_root, true);
    }
}
