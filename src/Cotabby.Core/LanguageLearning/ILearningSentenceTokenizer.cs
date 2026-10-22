namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Platform sentence-boundary service. Windows implements this with ICU sentence breaking; Core
/// passes a complete value or bounded caret neighborhood and accepts only its UTF-16 ranges. Core
/// never guesses sentence boundaries from punctuation.
/// </summary>
public interface ILearningSentenceTokenizer
{
    /// <summary>Returns sentence ranges relative to the supplied text using the captured source language.</summary>
    IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language);
}
