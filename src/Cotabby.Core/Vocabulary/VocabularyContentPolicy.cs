using System.Text;
using Cotabby.Core.Review;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Pure Mac sense-content rules, shared by future archive/editor/review owners. Validate metadata
/// before disk/media work. Ordered image occurrences count separately even when a digest repeats;
/// file deduplication is a storage responsibility, not a reason to bypass the per-sense budget.
/// </summary>
public static class VocabularyContentPolicy
{
    public const int MaximumImageBytes = 4 * 1_024 * 1_024;
    public const int MaximumSenseImageBytes = 16 * 1_024 * 1_024;
    public const int MaximumDimension = 1_600;
    public const int MaximumBlocks = 64;
    public const int MaximumTextBytes = 262_144;
    public const int MaximumCaptionBytes = 16_384;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<VocabularyImageReference> Images(IReadOnlyList<VocabularyContentBlock> blocks) =>
        Array.AsReadOnly(blocks.Select(block => block.Content).OfType<VocabularyBlockContent.Image>()
            .Select(image => image.Reference).ToArray());

    // Meaningfulness is separate from validity, matching Mac: even invalid image metadata counts
    // as image content here. Callers must validate before using it as a save/review admission gate.
    // Foundation's whitespacesAndNewlines also includes U+200B; .NET Unicode whitespace does not.
    public static bool Meaningful(IReadOnlyList<VocabularyContentBlock> blocks) => blocks.Any(block => block.Content switch
    {
        VocabularyBlockContent.Text { Value.Text: { } text } => text.EnumerateRunes()
            .Any(scalar => !ScalarSets.IsWhitespaceOrNewline(scalar) && scalar.Value != 0x200B),
        VocabularyBlockContent.Image => true,
        _ => false,
    });

    public static bool IsValid(VocabularyImageReference? image) => image is not null
        && image.Digest is { Length: 64 } digest && digest.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f')
        && image.Width is >= 1 and <= MaximumDimension && image.Height is >= 1 and <= MaximumDimension
        && image.ByteCount is >= 1 and <= MaximumImageBytes;

    public static void Validate(IReadOnlyList<VocabularyContentBlock> blocks)
    {
        if (blocks is null || blocks.Count > MaximumBlocks) throw new VocabularyContentValidationException();
        var ids = new HashSet<Guid>();
        long imageBytes = 0;
        foreach (var block in blocks)
        {
            if (block is null || !ids.Add(block.Id)) throw new VocabularyContentValidationException();
            switch (block.Content)
            {
                case VocabularyBlockContent.Text { Value: { } text } when Enum.IsDefined(text.Style)
                    && FitsUtf8(text.Text, MaximumTextBytes): break;
                case VocabularyBlockContent.Image image when IsValid(image.Reference) && FitsUtf8(image.Caption, MaximumCaptionBytes):
                    // Each operand is bounded before summing; imported Int64.MaxValue must reject,
                    // never overflow into an apparently small aggregate or trigger allocation.
                    imageBytes += image.Reference.ByteCount;
                    break;
                default: throw new VocabularyContentValidationException();
            }
        }
        if (imageBytes > MaximumSenseImageBytes) throw new VocabularyContentValidationException();
    }

    /// <summary>
    /// Literal review projection with the Mac markup/escaping order. Windows validates the whole
    /// input here as well, so an unvalidated digest cannot become HTML attribute/path injection.
    /// This produces a string only; it renders no browser and uploads no Anki media.
    /// </summary>
    public static string RenderHtml(IReadOnlyList<VocabularyContentBlock> blocks)
    {
        Validate(blocks);
        if (blocks.Count == 0) return string.Empty;
        var body = new StringBuilder();
        foreach (var block in blocks)
        {
            switch (block.Content)
            {
                case VocabularyBlockContent.Text text:
                    string value = ReviewHtmlText.Escape(text.Value.Text);
                    if (text.Value.Bold) value = "<strong>" + value + "</strong>";
                    if (text.Value.Italic) value = "<em>" + value + "</em>";
                    body.Append(text.Value.Style switch
                    {
                        VocabularyTextStyle.Heading => "<h3>" + value + "</h3>",
                        VocabularyTextStyle.Bullet => "<ul><li>" + value + "</li></ul>",
                        _ => "<p>" + value + "</p>",
                    });
                    break;
                case VocabularyBlockContent.Image image:
                    string caption = ReviewHtmlText.Escape(image.Caption);
                    body.Append("<figure style=\"margin:12px 0\"><img src=\"").Append(image.Reference.Filename)
                        .Append("\" style=\"max-width:100%;height:auto\" alt=\"").Append(caption).Append("\">");
                    if (image.Caption.Length != 0) body.Append("<figcaption>").Append(caption).Append("</figcaption>");
                    body.Append("</figure>");
                    break;
            }
        }
        return "<div class=\"sense-explanation\" style=\"font-weight:400;font-size:0.91em\">" + body + "</div>";
    }

    private static bool FitsUtf8(string? text, int maximum)
    {
        if (text is null || text.Length > maximum) return false;
        try { return StrictUtf8.GetByteCount(text) <= maximum; }
        catch (EncoderFallbackException) { return false; } // Malformed UTF-16 has no Swift String counterpart.
    }
}

/// <summary>Fixed recoverable validation failure; do not leak notebook text or media paths in errors.</summary>
public sealed class VocabularyContentValidationException() : Exception("This vocabulary content could not be verified.");
