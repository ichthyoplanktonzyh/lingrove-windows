using System.IO;
using System.IO.Pipes;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

public sealed class WindowsVocabularyWordListReaderTests
{
    [Fact]
    public async Task ReadsRegularFileAndFollowsPermittedSymbolicLink()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "words.csv"); await File.WriteAllTextAsync(path, "expression,meaning\nbank,shore\n");
            var reader = new WindowsVocabularyWordListReader();
            Assert.Equal("bank", (await reader.ReadAsync(path, default)).Rows[1].Cells[0]);
            string link = Path.Combine(dir, "alias.csv");
            try { File.CreateSymbolicLink(link, path); }
            catch (UnauthorizedAccessException) { return; } // Host does not grant symlink creation.
            Assert.Equal("bank", (await reader.ReadAsync(link, default)).Rows[1].Cells[0]);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task RefusesDirectoryDeviceAndPipe()
    {
        var reader = new WindowsVocabularyWordListReader();
        async Task AssertRejected(string path)
        {
            var error = await Assert.ThrowsAsync<VocabularyWordListException>(() => reader.ReadAsync(path, default).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(VocabularyWordListError.TooLarge, error.Error);
        }
        await AssertRejected(Path.GetTempPath()); await AssertRejected("NUL"); await AssertRejected(@"\\.\NUL");
        string name = "lingrove-test-" + Guid.NewGuid().ToString("N") + ".txt";
        using var server = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await AssertRejected(@"\\?\pipe\" + name);
    }
    [Fact]
    public async Task PathReplacementDoesNotReplaceOpenedContents()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "words.txt"), moved = Path.Combine(dir, "old.txt"); await File.WriteAllTextAsync(path, "original");
            var reader = new WindowsVocabularyWordListReader(async _ =>
            { File.Move(path, moved); await File.WriteAllTextAsync(path, "replacement"); });
            Assert.Equal("original", (await reader.ReadAsync(path, default)).Rows[0].Cells[0]);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task SizeAndGrowthAreBoundedAndCancellationClosesHandle()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await File.WriteAllTextAsync(path, "word");
            var growing = new WindowsVocabularyWordListReader(async _ =>
            {
                await using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                writer.SetLength(VocabularyWordListPolicy.MaximumBytes + 1L);
            });
            var error = await Assert.ThrowsAsync<VocabularyWordListException>(() => growing.ReadAsync(path, default));
            Assert.Equal(VocabularyWordListError.TooLarge, error.Error);
            error = await Assert.ThrowsAsync<VocabularyWordListException>(() => new WindowsVocabularyWordListReader().ReadAsync(path, default));
            Assert.Equal(VocabularyWordListError.TooLarge, error.Error);
            await File.WriteAllTextAsync(path, "word");
            using var cts = new CancellationTokenSource();
            var cancelled = new WindowsVocabularyWordListReader(_ => { cts.Cancel(); return Task.CompletedTask; });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.ReadAsync(path, cts.Token));
            File.Delete(path); Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
}
