using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Data-only Mac observations; no file/media/model/Anki effects or replacement oracle.</summary>
public sealed class VocabularyContentParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-content-v1.json"))).RootElement.Clone();

    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray()
        .Select(row => new object[] { row.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacSenseContentOwnsValidationMeaningAndExactReviewHtml(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var blocks = row.GetProperty("blocks").EnumerateArray().Select(Block).ToArray();
        var expected = row.GetProperty("expected");
        Assert.Equal(expected.GetProperty("meaningful").GetBoolean(), VocabularyContentPolicy.Meaningful(blocks));
        Assert.Equal(expected.GetProperty("images").EnumerateArray().Select(image => image.GetString()),
            VocabularyContentPolicy.Images(blocks).Select(image => image.Filename));
        if (!expected.GetProperty("valid").GetBoolean())
        {
            Assert.Throws<VocabularyContentValidationException>(() => VocabularyContentPolicy.Validate(blocks));
            Assert.Throws<VocabularyContentValidationException>(() => VocabularyContentPolicy.RenderHtml(blocks));
            return;
        }
        VocabularyContentPolicy.Validate(blocks);
        string html = VocabularyContentPolicy.RenderHtml(blocks);
        var bytes = Encoding.UTF8.GetBytes(html);
        Assert.Equal(expected.GetProperty("htmlBytes").GetInt32(), bytes.Length);
        Assert.Equal(expected.GetProperty("htmlSHA256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        if (expected.GetProperty("html").ValueKind == JsonValueKind.String)
            Assert.Equal(expected.GetProperty("html").GetString(), html);
    }

    private static VocabularyContentBlock Block(JsonElement value)
    {
        var id = value.GetProperty("id").GetGuid();
        if (value.GetProperty("kind").GetString() == "text")
            return new(id, new VocabularyBlockContent.Text(new(
                value.TryGetProperty("text", out var text) ? Text(text) : string.Empty,
                value.TryGetProperty("style", out var style) ? Enum.Parse<VocabularyTextStyle>(style.GetString()!, true) : VocabularyTextStyle.Paragraph,
                value.TryGetProperty("bold", out var bold) && bold.GetBoolean(),
                value.TryGetProperty("italic", out var italic) && italic.GetBoolean())));
        return new(id, new VocabularyBlockContent.Image(new(value.GetProperty("digest").GetString()!,
            value.GetProperty("width").GetInt64(), value.GetProperty("height").GetInt64(), value.GetProperty("byteCount").GetInt64()),
            value.TryGetProperty("caption", out var caption) ? Text(caption) : string.Empty));
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()!
        : string.Concat(Enumerable.Repeat(value.GetProperty("repeating").GetString()!, value.GetProperty("count").GetInt32()));
}
