using System.IO;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Dictionary;

namespace Cotabby.App.Wpf.Tests;

public sealed class MDictLibraryServiceTests
{
    [Fact]
    public async Task AutoDiscoveredCompanionsUseNumericFilenamePriority()
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-mdict-order-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string mdx = Path.Combine(root, "Book.mdx"); File.WriteAllBytes(mdx, MDictFixture3b.v2);
            File.WriteAllBytes(Path.Combine(root, "Book.10.mdd"), MDictFixture3b.mdd); File.WriteAllBytes(Path.Combine(root, "Book.2.mdd"), MDictFixture3b.mdd);
            await using var service = new MDictLibraryService(s => s.ToLowerInvariant());
            var book = await service.ImportAsync(mdx);
            Assert.Equal(new[] { "Book.2.mdd", "Book.10.mdd" }, book.ResourcePaths.Select(Path.GetFileName));
        }
        finally { Directory.Delete(root, true); }
    }

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
            Assert.Contains("Hello", article.Html); Assert.Contains("<link rel=\"stylesheet\" href=\"Book.css\">", article.Html); Assert.Contains("Hello world", article.Text);
            Assert.Same(LearningDictionaryResult.NotFound, await service.LookupAsync("cycle", library));
            var image = await service.ResourceAsync("test.png", library); Assert.NotNull(image); Assert.Equal("image/png", image.MimeType);
            Assert.Null(await service.ResourceAsync("../secret.css", library)); Assert.Null(await service.ResourceAsync("missing.css", library));
            var stylesheet = await service.ResourceAsync("Book.css", library); Assert.NotNull(stylesheet);
            Assert.Equal(".word{color:blue}", System.Text.Encoding.UTF8.GetString(stylesheet.Data));
            string target = Path.Combine(root, "target.css"); await File.WriteAllTextAsync(target, "secret");
            File.Delete(css); File.CreateSymbolicLink(css, target);
            Assert.Null(await service.ResourceAsync("Book.css", library));
            await File.WriteAllBytesAsync(mdx, []);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LookupAsync("hello", library));
            Assert.True(File.Exists(mdx)); Assert.True(File.Exists(mdd));
        }
        finally { foreach (string file in Directory.EnumerateFiles(root)) File.Delete(file); Directory.Delete(root); }
    }

    [Fact]
    public async Task LooseImagesFontsAndStylesUseTheSameBoundedRootAfterMddPriority()
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-mdict-root-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string mdx = Path.Combine(root, "Book.mdx"); File.WriteAllBytes(mdx, MDictFixture3b.v2); File.WriteAllBytes(Path.Combine(root, "Book.mdd"), MDictFixture3b.mdd);
            await using var service = new MDictLibraryService(s => s.ToLowerInvariant()); var book = await service.ImportAsync(mdx);
            var packed = (await service.ResourceAsync("test.png", book))!;
            File.WriteAllBytes(Path.Combine(root, "test.png"), [1, 2, 3]);
            Assert.Equal(packed.Data, (await service.ResourceAsync("test.png", book))!.Data);
            Directory.CreateDirectory(Path.Combine(root, "assets")); File.WriteAllBytes(Path.Combine(root, "assets", "loose.png"), packed.Data);
            Assert.Equal(packed.Data, (await service.ResourceAsync("assets/loose.png", book))!.Data);
            File.WriteAllBytes(Path.Combine(root, "assets", "font.woff"), [1, 2, 3]);
            var font = await service.ResourceAsync("assets/font.woff", book); Assert.NotNull(font); Assert.Equal("font/woff", font.MimeType); Assert.Equal(new byte[] { 1, 2, 3 }, font.Data);
            File.WriteAllBytes(Path.Combine(root, "huge.css"), new byte[8_000_001]); Assert.Null(await service.ResourceAsync("huge.css", book));
            Assert.Null(await service.ResourceAsync("..%2Foutside.css", book)); Assert.Null(await service.ResourceAsync("assets/../outside.css", book));
            Assert.Null(await service.ResourceAsync("file:///outside.css", book)); Assert.Null(await service.ResourceAsync("Book.mdx", book));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportRejectsLinkedMainOrAutomaticallyDiscoveredCompanion(bool companion)
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-mdict-links-" + Guid.NewGuid().ToString("N"));
        string bookFolder = Path.Combine(root, "book"), outside = Path.Combine(root, "outside"); Directory.CreateDirectory(bookFolder); Directory.CreateDirectory(outside);
        try
        {
            string mdx = Path.Combine(bookFolder, "Book.mdx"), target = Path.Combine(outside, companion ? "data.mdd" : "data.mdx");
            File.WriteAllBytes(target, companion ? MDictFixture3b.mdd : MDictFixture3b.v2);
            if (companion) { File.WriteAllBytes(mdx, MDictFixture3b.v2); File.CreateSymbolicLink(Path.Combine(bookFolder, "Book.mdd"), target); }
            else File.CreateSymbolicLink(mdx, target);
            await using var service = new MDictLibraryService(s => s.ToLowerInvariant());
            await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(mdx));
            Assert.Equal(companion ? MDictFixture3b.mdd : MDictFixture3b.v2, File.ReadAllBytes(target));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AttachAndCachedLookupRejectLinksAndMismatchedResourceRoots()
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-mdict-attach-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string mdx = Path.Combine(root, "Book.mdx"), mdd = Path.Combine(root, "other.mdd"), link = Path.Combine(root, "link.mdd");
            File.WriteAllBytes(mdx, MDictFixture3b.v2); File.WriteAllBytes(mdd, MDictFixture3b.mdd); File.CreateSymbolicLink(link, mdd);
            await using var service = new MDictLibraryService(s => s.ToLowerInvariant()); var book = await service.ImportAsync(mdx);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.AttachAsync(book, [link]));
            Directory.CreateDirectory(Path.Combine(root, "other"));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LookupAsync("hello", book with { FolderPath = Path.Combine(root, "other") }));
            Assert.IsType<LearningDictionaryResult.Article>(await service.LookupAsync("hello", book));
            File.Move(mdx, mdx + ".original"); File.CreateSymbolicLink(mdx, mdx + ".original");
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LookupAsync("hello", book));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SameLengthAndTimestampReplacementCannotReuseTheOldParser()
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-mdict-cache-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string mdx = Path.Combine(root, "Book.mdx"); byte[] input = MDictFixture3b.v2; File.WriteAllBytes(mdx, input);
            await using var service = new MDictLibraryService(s => s.ToLowerInvariant()); var book = await service.ImportAsync(mdx);
            Assert.IsType<LearningDictionaryResult.Article>(await service.LookupAsync("hello", book));
            // An all-zero buffer can describe an empty dictionary. Keep the same length but
            // use an impossible header length, so this must fail if the new object is parsed.
            var invalid = (byte[])input.Clone(); invalid.AsSpan(0, 4).Fill(255);
            var timestamp = File.GetLastWriteTimeUtc(mdx); File.Move(mdx, mdx + ".original"); File.WriteAllBytes(mdx, invalid); File.SetLastWriteTimeUtc(mdx, timestamp);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LookupAsync("hello", book));
            File.Delete(mdx); File.Move(mdx + ".original", mdx);
            Assert.IsType<LearningDictionaryResult.Article>(await service.LookupAsync("hello", book));
        }
        finally { Directory.Delete(root, true); }
    }
}
