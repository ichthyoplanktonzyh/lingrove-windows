using System.Text;
using Cotabby.Core.CLI;
using Cotabby.Win32.CLI;

namespace Cotabby.Inference.Tests.CLI;

public sealed class CliLocalInputTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cli-local-" + Guid.NewGuid().ToString("N"));
    public CliLocalInputTests() => Directory.CreateDirectory(_root);
    [Fact]
    public async Task AttachesOnlyExplicitImageBytesAndHonoursBound()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "image.png"), [1, 2, 3]);
        var json = """{"version":1,"idempotencyKey":"test","operations":[{"add":{"expression":"test","language":"en","kind":"word","meaning":"test","content":[{"image":{"path":"image.png"}}]}}]}""";
        var batch = CliInputContract.DecodeBatch(Encoding.UTF8.GetBytes(json));
        var attached = await CliLocalInput.AttachImagesAsync(batch, _root);
        Assert.Equal("AQID", attached.CopyValue()["operations"]![0]!["add"]!["content"]![0]!["image"]!["data"]!.GetValue<string>());
        Assert.NotNull(batch.CopyValue()["operations"]![0]!["add"]!["content"]![0]!["image"]!["path"]);
        Assert.Equal(CliErrorCode.InvalidInput, (await Assert.ThrowsAsync<CliException>(() => CliLocalInput.ReadAsync(Path.Combine(_root, "image.png"), 2))).Code);
    }
    [Fact]
    public async Task DirectoryAndReparsePointAreRejected()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "file"), [1]);
        string link = Path.Combine(_root, "link"); File.CreateSymbolicLink(link, Path.Combine(_root, "file"));
        Assert.Equal(CliErrorCode.InvalidInput, (await Assert.ThrowsAsync<CliException>(() => CliLocalInput.ReadAsync(_root, 10))).Code);
        Assert.Equal(CliErrorCode.InvalidInput, (await Assert.ThrowsAsync<CliException>(() => CliLocalInput.ReadAsync(link, 10))).Code);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
