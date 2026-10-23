using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyContentTests
{
    [Fact]
    public void DefaultsAndExplicitCreationRetainOrderedBlockIdentityAcrossEdits()
    {
        var text = new VocabularyTextContent();
        Assert.Equal("", text.Text); Assert.Equal(VocabularyTextStyle.Paragraph, text.Style);
        Assert.False(text.Bold); Assert.False(text.Italic);
        var first = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(text));
        var second = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(text));
        Assert.NotEqual(Guid.Empty, first.Id); Assert.NotEqual(first.Id, second.Id);
        var edited = first with { Content = new VocabularyBlockContent.Text(text with { Text = "Edited" }) };
        Assert.Equal(first.Id, edited.Id);
        VocabularyContentBlock[] ordered = [second, edited];
        VocabularyContentPolicy.Validate(ordered);
        Assert.Equal(new[] { second.Id, first.Id }, ordered.Select(block => block.Id));
        Assert.Equal("", Assert.IsType<VocabularyBlockContent.Text>(first.Content).Value.Text);
    }

    [Fact]
    public void CanonicalValueEqualityDoesNotNormalizeStoredContentOrCaptions()
    {
        var decomposed = new VocabularyTextContent("cafe\u0301");
        var composed = new VocabularyTextContent("café");
        Assert.Equal(decomposed, composed); Assert.Equal(decomposed.GetHashCode(), composed.GetHashCode());
        Assert.Equal("cafe\u0301", decomposed.Text);
        var image = new VocabularyImageReference(new string('a', 64), 1, 1, 1);
        var first = new VocabularyBlockContent.Image(image, "cafe\u0301");
        var second = new VocabularyBlockContent.Image(image, "café");
        Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("cafe\u0301", first.Caption);
    }

    [Fact]
    public void ImageProjectionIsDetachedOrderedAndKeepsRepeatedReferences()
    {
        var first = new VocabularyImageReference(new string('a', 64), 1, 1, 1);
        var second = first with { Digest = new string('b', 64) };
        var source = new List<VocabularyContentBlock>
        {
            VocabularyContentBlock.Create(new VocabularyBlockContent.Image(second)),
            VocabularyContentBlock.Create(new VocabularyBlockContent.Image(first)),
            VocabularyContentBlock.Create(new VocabularyBlockContent.Image(second)),
        };
        var images = VocabularyContentPolicy.Images(source);
        source.Clear();
        Assert.Equal(new[] { second, first, second }, images);
        Assert.True(Assert.IsAssignableFrom<ICollection<VocabularyImageReference>>(images).IsReadOnly);
    }

    [Fact]
    public void RejectedReferenceNeverBecomesHtmlEvenWhenItIsMeaningfulImageContent()
    {
        var image = new VocabularyImageReference("\"><script>alert(1)</script>", 1, 1, 1);
        VocabularyContentBlock[] blocks = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image))];
        Assert.True(VocabularyContentPolicy.Meaningful(blocks)); Assert.False(VocabularyContentPolicy.IsValid(image));
        var error = Assert.Throws<VocabularyContentValidationException>(() => VocabularyContentPolicy.RenderHtml(blocks));
        Assert.DoesNotContain("script", error.Message);
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void MalformedDotNetUtf16HasNoSwiftCounterpartAndCannotBeEncodedWithReplacement(int codeUnit)
    {
        string text = new((char)codeUnit, 1); // Attribute metadata cannot retain an unpaired surrogate string.
        VocabularyContentBlock[] blocks = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new(text)))];
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyContentPolicy.Validate(blocks));
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyContentPolicy.RenderHtml(blocks));
        VocabularyContentBlock[] caption = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(
            new(new string('a', 64), 1, 1, 1), text))];
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyContentPolicy.Validate(caption));
    }

    [Fact]
    public void UnknownStyleAndNullAlternativesFailValidationBeforeProjection()
    {
        VocabularyContentBlock[][] invalid =
        [
            [new(Guid.NewGuid(), null!)],
            [new(Guid.NewGuid(), new VocabularyBlockContent.Text(null!))],
            [new(Guid.NewGuid(), new VocabularyBlockContent.Text(new(null!)))],
            [new(Guid.NewGuid(), new VocabularyBlockContent.Text(new("literal", (VocabularyTextStyle)999)))],
            [new(Guid.NewGuid(), new VocabularyBlockContent.Image(null!))],
            [new(Guid.NewGuid(), new VocabularyBlockContent.Image(new(new string('a', 64), 1, 1, 1), null!))],
            [null!],
        ];
        foreach (var blocks in invalid)
            Assert.Throws<VocabularyContentValidationException>(() => VocabularyContentPolicy.RenderHtml(blocks));
    }

    [Fact]
    public void ReviewEscapingRetainsOriginalCrAndOtherUnicodeRatherThanNormalizingEndlines()
    {
        Assert.Equal("&amp;&lt;&gt;&quot;&#39;\r<br>\u2028\uFEFF", ReviewHtmlText.Escape("&<>\"'\r\n\u2028\uFEFF"));
    }
}
