namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Platform-owned Unicode and word segmentation used by the Mac lexical policy. Windows uses
/// system ICU; portable recognizer tests supply actual committed Mac observations. That test
/// oracle is never a production provider, and missing native support must not trigger guessing.
/// </summary>
public interface ILearningLexicalText
{
    /// <summary>NFC, full Unicode lowercase, curly-apostrophe mapping, then whitespace collapse.</summary>
    string Key(string value);
    /// <summary>Letter-containing word occurrences of at most 256 UTF-8 bytes in untouched text.</summary>
    IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language);
    /// <summary>Swift Character letter/whitespace/apostrophe predicate over the original extent.</summary>
    bool IsPatternText(string value);
    /// <summary>The first grapheme's Swift-compatible uppercase property, including full casing.</summary>
    bool FirstIsUppercase(string value);
}

public enum LearningLexicalFailure { InvalidInput, TextProviderUnavailable, InvalidWordRanges }

/// <summary>Only fixed categories cross an OS/resource boundary; raw exception text may contain input.</summary>
public sealed class LearningLexicalException(LearningLexicalFailure failure) : Exception(failure switch
{
    LearningLexicalFailure.TextProviderUnavailable => "Lexical text processing is unavailable on this system.",
    LearningLexicalFailure.InvalidWordRanges => "Lexical word ranges could not be validated.",
    _ => "This lexical text is unavailable.",
})
{
    public LearningLexicalFailure Failure { get; } = failure;
}
