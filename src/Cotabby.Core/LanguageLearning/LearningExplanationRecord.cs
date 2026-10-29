using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// A complete context-specific explanation, adapted from the Mac record. The service creates it;
/// sessions and future notebook persistence borrow it. Provider addresses and credentials are
/// absent. This in-memory value does not establish a storage format or cross-platform date encoding.
/// </summary>
public sealed record LearningExplanationRecord(Guid Id, LearningExplanationInput Input,
    LearningLanguage Language, LearningExplanationResult Result, string Model, DateTimeOffset CreatedAt)
{
    // A canonical annotation may contain more date precision than DateTimeOffset can express.
    // Retain that original evidence for a later canonical save without changing portable JSON.
    [System.Text.Json.Serialization.JsonIgnore]
    internal Cotabby.Core.Vocabulary.Frozen.SourceDate? CanonicalCreatedAt { get; init; }
    public bool Equals(LearningExplanationRecord? other) => other is not null && Id == other.Id && Input == other.Input
        && Language == other.Language && Result == other.Result && CanonicalStringComparer.Instance.Equals(Model, other.Model)
        && CreatedAt == other.CreatedAt;
    public override int GetHashCode() => HashCode.Combine(Id, Input, Language, Result,
        CanonicalStringComparer.Instance.GetHashCode(Model), CreatedAt);
}
