namespace Cotabby.Core.Text;

/// <summary>
/// Shared Foundation range admission for vocabulary, explanation and review evidence. Each caller
/// creates one immutable map and reuses it for its spans. Expansion may depend on the queried range,
/// so validation compares an expanded range with itself instead of checking two independent array
/// endpoints. Readonly UI selection still uses whole Swift/.NET graphemes through its separate map.
/// The measured Darwin/Unicode compatibility boundary is documented in VOCABULARY_EDITOR_CURRENT.md.
/// </summary>
internal static class FoundationTextRanges
{
    public static FoundationComposedText Boundaries(string text, CancellationToken cancellationToken = default) => new(text, cancellationToken);
    public static bool IsValid(int location, int length, string text, FoundationComposedText boundaries) =>
        ReferenceEquals(boundaries.Text, text) && boundaries.IsValid(location, length);
}
