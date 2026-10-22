namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Platform boundary for constrained language recognition. Implementations score only the
/// requested candidate set; Core owns confidence thresholds and never invents a heuristic score.
/// </summary>
public interface ILearningLanguageDetector
{
    /// <summary>Returns recognizer confidences for the supplied text and ordered allowed candidates.</summary>
    IReadOnlyDictionary<LearningLanguage, double> Scores(
        string sample,
        IReadOnlyList<LearningLanguage> candidates);
}
