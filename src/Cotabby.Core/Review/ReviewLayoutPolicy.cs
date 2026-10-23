using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Review;

/// <summary>
/// Mac structural layout admission, shared by a future archive and card resolver. The layout owns
/// ordered parts and card-only blocks; validating does not resolve source references or render a
/// card. Missing source IDs remain a separate resolution concern, just as in the Mac implementation.
/// </summary>
public static class ReviewLayoutPolicy
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
