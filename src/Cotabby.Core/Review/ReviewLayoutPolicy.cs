using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Review;

/// <summary>
/// Mac structural layout admission, shared by archive validation and the separate source resolver.
/// The layout owns ordered parts and card-only blocks; validation does not silently repair missing
/// source IDs. Resolution and rendering live in the companion partial, preserving that distinction.
/// </summary>
public static partial class ReviewLayoutPolicy
{
    public static void Validate(ReviewLayout layout)
    {
        if (layout is null || layout.Parts.Count > 64 || layout.Parts.Any(part => part is null)
            || layout.Parts.Select(part => part.Id).Distinct().Count() != layout.Parts.Count) throw Invalid();
        foreach (var part in layout.Parts)
        {
            // Swift closed enums cannot hold undefined .NET integers.
            if (!Enum.IsDefined(part.Source) || part.Source != ReviewContentSource.Custom && part.Content.Count != 0) throw Invalid();
            VocabularyContentPolicy.Validate(part.Content);
        }
        VocabularyContentPolicy.Validate(layout.CustomContent);
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
}
