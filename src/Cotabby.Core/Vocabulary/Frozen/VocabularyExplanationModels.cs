using LearningExplanationInput = Cotabby.Core.Vocabulary.Resources.LearningExplanationInput;
using LearningExplanationContext = Cotabby.Core.Vocabulary.Resources.LearningExplanationContext;
using FrozenCompleteness = Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>A durable annotation retains the source's optional language/completeness without
/// changing the existing provider profile or manufacturing an English source-language hint.</summary>
public sealed record LearningExplanationRecord(Guid Id, LearningExplanationInput Input,
    LearningLanguage Language, LearningExplanationResult Result, string Model, SourceDate CreatedAt)
{
    public bool Equals(LearningExplanationRecord? other) => other is not null && Id == other.Id && Input == other.Input
        && Language == other.Language && Result == other.Result && CollectionValues.Same(Model, other.Model) && CreatedAt == other.CreatedAt;
    public override int GetHashCode() => HashCode.Combine(Id, Input, Language, Result, CollectionValues.StringHash(Model), CreatedAt);
}
