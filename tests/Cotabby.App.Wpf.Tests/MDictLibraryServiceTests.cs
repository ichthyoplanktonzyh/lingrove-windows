using System.IO;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Dictionary;

namespace Cotabby.App.Wpf.Tests;

public sealed class MDictLibraryServiceTests
{
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
            Assert.Contains("Hello", article.Html); Assert.Contains("Book.css", article.Html); Assert.Contains("Hello world", article.Text);
            Assert.Same(LearningDictionaryResult.NotFound, await service.LookupAsync("cycle", library));
            var image = await service.ResourceAsync("test.png", library); Assert.NotNull(image); Assert.Equal("image/png", image.MimeType);
            Assert.Null(await service.ResourceAsync("../secret.css", library)); Assert.Null(await service.ResourceAsync("missing.css", library));
            await File.WriteAllBytesAsync(mdx, []);
            await Assert.ThrowsAnyAsync<Exception>(() => service.LookupAsync("hello", library));
            Assert.True(File.Exists(mdx)); Assert.True(File.Exists(mdd));
        }
        finally { foreach (string file in Directory.EnumerateFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
}
