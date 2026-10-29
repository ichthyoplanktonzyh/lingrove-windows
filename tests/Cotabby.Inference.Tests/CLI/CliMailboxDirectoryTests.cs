using System.Text;
using Cotabby.Core.CLI;
using Cotabby.Win32.CLI;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests.CLI;

public sealed class CliMailboxDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-cli-test-" + Guid.NewGuid().ToString("N"));
    public CliMailboxDirectoryTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    [Fact]
    public void ExclusivePublishClaimReadAndRemoveKeepMessageBytes()
    {
        using var pending = new CliMailboxDirectory(Path.Combine(_root, "requests")); using var working = new CliMailboxDirectory(Path.Combine(_root, "working"));
        string name = Guid.NewGuid().ToString("D") + ".json"; byte[] first = Encoding.UTF8.GetBytes("first"); pending.Publish(name, first);
        Assert.Throws<CliException>(() => pending.Publish(name, Encoding.UTF8.GetBytes("replacement"))); Assert.Equal(first, pending.Read(name));
        Assert.True(pending.Claim(name, working)); Assert.Null(pending.Read(name)); Assert.Equal(first, working.Read(name));
        Assert.True(working.Claim(name, pending)); Assert.Equal(first, pending.Read(name)); pending.Remove(name); Assert.Null(pending.Read(name));
    }
    [Fact]
    public void InvalidLeavesAndOversizeNeverReplaceExistingMessages()
    {
        using var directory = new CliMailboxDirectory(Path.Combine(_root, "requests")); string name = Guid.NewGuid().ToString("D") + ".json";
        Assert.Throws<CliException>(() => directory.Publish("../x.json", [])); Assert.Throws<CliException>(() => directory.Publish(name, new byte[CliInputContract.MaximumBytes + 1]));
        Assert.Null(directory.Read(name)); directory.Publish(name, [1,2,3]); Assert.Throws<CliException>(() => directory.Read(name, maximum: 2)); Assert.Equal(new byte[] { 1,2,3 }, directory.Read(name));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
