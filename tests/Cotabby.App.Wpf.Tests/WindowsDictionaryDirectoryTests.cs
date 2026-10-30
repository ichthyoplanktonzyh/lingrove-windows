using System.IO;
using Cotabby.Win32.Dictionary;

namespace Cotabby.App.Wpf.Tests;

public sealed class WindowsDictionaryDirectoryTests
{
    [Fact]
    public void HandleRootEnumeratesAndPinsAReadOnlyFileLease()
    {
        string folder = Path.Combine(Path.GetTempPath(), "dictionary-root-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "Book.css"); File.WriteAllText(file, ".word{color:red}");
        try
        {
            using var root = WindowsDictionaryDirectory.Open(folder);
            Assert.Contains(root.Entries(default), entry => entry.Name == "Book.css" && !entry.Directory);
            using (var input = root.OpenFile("Book.css")!)
            {
                Assert.NotNull(input); var stamp = WindowsDictionaryDirectory.Stamp(input); Assert.True(stamp.Size > 0);
                Assert.Throws<IOException>(() => File.WriteAllText(file, "changed"));
                using var text = new StreamReader(input); Assert.Equal(".word{color:red}", text.ReadToEnd());
            }
            File.WriteAllText(file, "changed");
            using var read = root.OpenFile("Book.css")!; Assert.Equal(7, WindowsDictionaryDirectory.Stamp(read).Size);
        }
        catch (DictionaryFileException error) { throw new InvalidOperationException($"Native dictionary root failure 0x{error.NativeError:X8}", error); }
        finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public void LeafAndIntermediateLinksCannotEscapeTheAcquiredRoot()
    {
        string folder = Path.Combine(Path.GetTempPath(), "dictionary-root-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(folder, "book"), outside = Path.Combine(folder, "outside"); Directory.CreateDirectory(source); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "outside.css"), "outside");
        File.CreateSymbolicLink(Path.Combine(source, "leaf.css"), Path.Combine(outside, "outside.css"));
        Directory.CreateSymbolicLink(Path.Combine(source, "alias"), outside);
        try
        {
            using var root = WindowsDictionaryDirectory.Open(source);
            Assert.Throws<DictionaryFileException>(() => root.OpenFile("leaf.css"));
            Assert.Throws<DictionaryFileException>(() => root.OpenFile("alias/outside.css"));
            Assert.Throws<DictionaryFileException>(() => root.OpenFile("../outside/outside.css"));
            Assert.Throws<DictionaryFileException>(() => WindowsDictionaryDirectory.Open(Path.Combine(source, "alias")));
            Assert.Throws<DictionaryFileException>(() => WindowsDictionaryDirectory.Open(Path.Combine(source, "alias", "nested")));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Theory]
    [InlineData(@"\\owned.invalid\share\Book.mdx")]
    [InlineData(@"\\?\UNC\owned.invalid\share\Book.mdx")]
    [InlineData(@"\\.\pipe\Book.mdx")]
    public void NetworkAndDeviceNamesAreRejectedBeforeFilesystemAdmission(string path) =>
        Assert.Throws<DictionaryFileException>(() => WindowsDictionaryDirectory.Open(path));
}
