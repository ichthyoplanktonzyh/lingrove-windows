using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// User-authored sense content, independent of generated explanation records. A future notebook
/// snapshot owns ordered blocks; edits/reordering retain Id. Only explicit creation makes an ID,
/// so loading or copying a block never silently changes its durable identity.
/// </summary>
public sealed record VocabularyContentBlock(Guid Id, VocabularyBlockContent Content)
{
    public static VocabularyContentBlock Create(VocabularyBlockContent content) => new(Guid.NewGuid(), content);
}

/// <summary>Closed literal content alternatives from Mac; no HTML, file bytes or live UI state.</summary>
public abstract record VocabularyBlockContent
{
    private VocabularyBlockContent() { }
    public sealed record Text(VocabularyTextContent Value) : VocabularyBlockContent;
    public sealed record Image(VocabularyImageReference Reference, string Caption = "") : VocabularyBlockContent
    {
        public bool Equals(Image? other) => other is not null && Reference == other.Reference
            && CanonicalStringComparer.Instance.Equals(Caption, other.Caption);
        public override int GetHashCode() => HashCode.Combine(Reference, CanonicalStringComparer.Instance.GetHashCode(Caption));
    }
}

public enum VocabularyTextStyle { Paragraph, Heading, Bullet }

/// <summary>
/// Small formatting values shared by future native reading and review output. Swift canonical
/// equality is preserved while the exact original text remains available for editing/export.
/// </summary>
public sealed record VocabularyTextContent(string Text = "", VocabularyTextStyle Style = VocabularyTextStyle.Paragraph,
    bool Bold = false, bool Italic = false)
{
    public bool Equals(VocabularyTextContent? other) => other is not null && Style == other.Style
        && Bold == other.Bold && Italic == other.Italic && CanonicalStringComparer.Instance.Equals(Text, other.Text);
    public override int GetHashCode() => HashCode.Combine(CanonicalStringComparer.Instance.GetHashCode(Text), Style, Bold, Italic);
}

/// <summary>
/// Immutable PNG metadata; captions belong to blocks, so references may be shared. Long numeric
/// fields retain imported 64-bit values until validation rejects them before allocation or sums.
/// This value does not prove bytes match the digest/dimensions; media preparation must do that.
/// Filename is usable only after policy validation, never as an untrusted storage path.
/// </summary>
public sealed record VocabularyImageReference(string Digest, long Width, long Height, long ByteCount)
{
    public string Filename => "lg-image-" + Digest + ".png";
    public bool Equals(VocabularyImageReference? other) => other is not null && Width == other.Width
        && Height == other.Height && ByteCount == other.ByteCount && CanonicalStringComparer.Instance.Equals(Digest, other.Digest);
    public override int GetHashCode() => HashCode.Combine(CanonicalStringComparer.Instance.GetHashCode(Digest), Width, Height, ByteCount);
}
