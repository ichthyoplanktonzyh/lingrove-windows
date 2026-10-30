using System.IO;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Dictionary;

namespace Cotabby.App.Wpf.Tests;

public sealed class MDictLibraryServiceTests
{
    [Fact]
    public async Task CancellationAndShutdownDrainAnAcceptedLookup()
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-mdict-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Book.mdx");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); bool hold = false;
        var service = new MDictLibraryService(s => { if (hold) { entered.TrySetResult(); release.Wait(); } return s.ToLowerInvariant(); });
        try
        {
            await File.WriteAllBytesAsync(path, MDictFixture3b.v2); var book = await service.ImportAsync(path);
            hold = true; using var cancel = new CancellationTokenSource();
            var lookup = service.LookupAsync("hello", book, cancel.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
            var drain = service.DisposeAsync().AsTask(); Assert.False(drain.IsCompleted); release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup);
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); await service.DisposeAsync(); File.Delete(path); Directory.Delete(root); }
    }

    [Fact]
    public async Task SyntheticImportLooksUpAliasesResourcesAndRejectsTruncatedFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-mdict-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string mdx = Path.Combine(root, "Book.mdx"), mdd = Path.Combine(root, "Book.mdd"), css = Path.Combine(root, "Book.css");
        try
        {
            await File.WriteAllBytesAsync(mdx, MDictFixture3b.v2); await File.WriteAllBytesAsync(mdd, MDictFixture3b.mdd); await File.WriteAllTextAsync(css, ".word{color:blue}");
            await using var service = new MDictLibraryService(s => s.ToLowerInvariant());
            var library = await service.ImportAsync(mdx); Assert.Equal(3, library.EntryCount); Assert.Single(library.ResourcePaths);
            var article = Assert.IsType<LearningDictionaryResult.Article>(await service.LookupAsync("alias", library));
            Assert.Contains("Hello", article.Html); Assert.DoesNotContain("Book.css", article.Html); Assert.Contains("Hello world", article.Text);
            Assert.Same(LearningDictionaryResult.NotFound, await service.LookupAsync("cycle", library));
            var image = await service.ResourceAsync("test.png", library); Assert.NotNull(image); Assert.Equal("image/png", image.MimeType);
            Assert.Null(await service.ResourceAsync("../secret.css", library)); Assert.Null(await service.ResourceAsync("missing.css", library));
            Assert.Null(await service.ResourceAsync("Book.css", library)); // Loose files are not authorized resources.
            string target = Path.Combine(root, "target.css"); await File.WriteAllTextAsync(target, "secret");
            File.Delete(css); File.CreateSymbolicLink(css, target);
            Assert.Null(await service.ResourceAsync("Book.css", library));
            await File.WriteAllBytesAsync(mdx, []);
            await Assert.ThrowsAnyAsync<Exception>(() => service.LookupAsync("hello", library));
            Assert.True(File.Exists(mdx)); Assert.True(File.Exists(mdd));
        }
        finally { foreach (string file in Directory.EnumerateFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
}
