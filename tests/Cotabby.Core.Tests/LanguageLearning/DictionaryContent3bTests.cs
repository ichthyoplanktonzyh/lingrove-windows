using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class DictionaryContent3bTests
{
    [Theory]
    [InlineData("../secret")] [InlineData("%2e%2e/secret")] [InlineData("\\..\\secret")]
    [InlineData("file:///etc/passwd")] [InlineData("a/../../b")] [InlineData("a\0b")]
    public void UnsafeResourcePathsAreRejected(string input) => Assert.Null(DictionaryContentPolicy.ResourcePath(input));
    [Fact]
    public void ResourceAndPlainTextFollowSourceRules()
    {
        Assert.Equal("images/a.png", DictionaryContentPolicy.ResourcePath("\\images\\a.png"));
        Assert.Equal("a b.css", DictionaryContentPolicy.ResourcePath("/a%20b.css"));
        Assert.Equal("Hello world", DictionaryContentPolicy.PlainText("<script>bad()</script><p>Hello <b>world</b></p>"));
        Assert.False(DictionaryContentPolicy.ValidQuery(new string('x', 257)));
    }
    [Fact]
    public void ArticleEnvelopeRemovesInjectedBaseAndDeniesNetworkAndScripts()
    {
        string document = DictionaryHtmlPolicy.Document("<html><head><base href='file:///tmp/'><meta http-equiv='refresh' content='0;url=https://evil.invalid'></head><body><p>word</p></body></html>");
        Assert.DoesNotContain("<base ", document); Assert.DoesNotContain("http-equiv='refresh'", document);
        Assert.Contains("script-src 'none'", document); Assert.Contains("connect-src 'none'", document);
        Assert.Contains("base-uri 'none'", document); Assert.Contains("<p>word</p>", document);
    }
    [Fact]
    public void AdversarialUnclosedScriptsAreTimeBoundAndCancellationIsObserved()
    {
        var html = string.Concat(Enumerable.Repeat("<script>", 100_000));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(() => DictionaryContentPolicy.PlainText(html));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => DictionaryContentPolicy.PlainText("<p>text</p>", canceled.Token));
    }
}
