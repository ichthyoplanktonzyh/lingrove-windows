using System.IO;
using System.Windows.Threading;
using Cotabby.App.Speech;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class SpeechModelFilesTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ImportedSpeechFileCannotBeDeletedWhileProtected() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "speech-model-test-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"), destination = Path.Combine(root, "speech"); Directory.CreateDirectory(source);
        string input = Path.Combine(source, "fixture.gguf"); await File.WriteAllBytesAsync(input, [0x47, 0x47, 0x55, 0x46, 1, 2, 3]);
        try
        {
            await using var files = new SpeechModelFiles(destination, Dispatcher.CurrentDispatcher);
            var imported = await files.ImportAsync([input], CancellationToken.None); Assert.Equal(1, imported.Imported);
            Assert.Single(files.InstalledModelOptions()); files.ProtectedModelFilenames = () => new HashSet<string> { "FIXTURE.GGUF" };
            Assert.False(await files.DeleteAsync("fixture.gguf", CancellationToken.None)); Assert.True(File.Exists(Path.Combine(destination, "fixture.gguf")));
            files.ProtectedModelFilenames = () => new HashSet<string>(); Assert.True(await files.DeleteAsync("fixture.gguf", CancellationToken.None));
            Assert.Empty(files.InstalledModelOptions());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
