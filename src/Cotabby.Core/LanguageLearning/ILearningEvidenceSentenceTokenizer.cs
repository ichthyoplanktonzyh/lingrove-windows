namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Complete sentence-token extents used by captured learning evidence, including separator spaces.
/// Mac NLTokenizer retains them in example text/ranges and receipt identity. This is separate from
/// the current-sentence preview adapter's trimmed intervals; substituting that adapter would turn
/// a complete captured sentence into a fragment or change the context receipt.
/// </summary>
public interface ILearningEvidenceSentenceTokenizer
{
    IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language);
}
