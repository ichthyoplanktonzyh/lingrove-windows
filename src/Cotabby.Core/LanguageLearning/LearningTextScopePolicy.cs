using System.Globalization;
using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>One UTF-16 range captured from an editor snapshot.</summary>
public readonly record struct LearningTextRange(int Location, int Length)
{
    public int End => checked(Location + Length);
}

/// <summary>
/// Purely resolves exact editor text. Windows UIA supplies the value and completeness evidence;
/// this policy checks UTF-16 bounds and grapheme boundaries, then returns the whole requested text.
/// It never trims a target to fit a model budget.
/// </summary>
public static class LearningTextScopePolicy
{
    private const int SentenceNeighborhoodUtf16Units = 32_768;

    /// <summary>Returns an exact, complete selected range or a fixed policy failure.</summary>
    public static string Text(string value, LearningTextRange range)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsWellFormedUtf16(value))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidSourceRange);
        var textElementStarts = StringInfo.ParseCombiningCharacters(value);
        if (range.Location < 0 || range.Length <= 0 || range.Location > value.Length ||
            range.Length > value.Length - range.Location ||
            !IsTextElementBoundary(value, range.Location, textElementStarts) ||
            !IsTextElementBoundary(value, range.Location + range.Length, textElementStarts))
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidSourceRange);
        }

        var source = value.Substring(range.Location, range.Length);
        ValidateSource(source);
        return source;
    }

    /// <summary>
    /// Prefers a non-empty selection; otherwise builds the macOS-sized, grapheme-complete UTF-16
    /// neighborhood around a caret and asks the platform tokenizer for sentence ranges in it. A
    /// sentence that touches an artificial neighborhood edge is rejected whole. Windows supplies
    /// ICU sentence breaking; Core never approximates boundaries with punctuation.
    /// </summary>
    public static LearningTextRange ConversionRange(
        string value,
        LearningTextRange selection,
        ILearningSentenceTokenizer tokenizer,
        LearningLanguage language)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsWellFormedUtf16(value) || !IsValidRange(value, selection, allowEmpty: true))
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidCaretRange);
        }

        var fieldTextElements = StringInfo.ParseCombiningCharacters(value);
        if (!IsTextElementBoundary(value, selection.Location, fieldTextElements) ||
            !IsTextElementBoundary(value, selection.Location + selection.Length, fieldTextElements))
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidCaretRange);
        }

        if (selection.Length > 0)
        {
            _ = Text(value, selection);
            return selection;
        }

        if (!Enum.IsDefined(language))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        if (tokenizer is null)
            throw new LearningTranslationException(LearningTranslationFailure.SentenceTokenizerUnavailable);

        var windowRange = SentenceWindow(value, selection.Location, fieldTextElements);
        var window = value.Substring(windowRange.Location, windowRange.Length);
        var caret = selection.Location - windowRange.Location;
        var windowTextElements = StringInfo.ParseCombiningCharacters(window);

        IReadOnlyList<LearningTextRange> sentences;
        try
        {
            sentences = tokenizer.Tokenize(window, language)
                ?? throw new LearningTranslationException(LearningTranslationFailure.SentenceTokenizerUnavailable);
        }
        catch (LearningTranslationException)
        {
            throw;
        }
        catch
        {
            // Platform tokenizer exceptions can include host details. Keep the policy failure fixed.
            throw new LearningTranslationException(LearningTranslationFailure.SentenceTokenizerUnavailable);
        }

        var previousSentenceEnd = 0;
        foreach (var sentence in sentences)
        {
            if (!IsValidRange(window, sentence, allowEmpty: false) ||
                !IsTextElementBoundary(window, sentence.Location, windowTextElements) ||
                !IsTextElementBoundary(window, sentence.Location + sentence.Length, windowTextElements) ||
                sentence.Location < previousSentenceEnd)
            {
                throw new LearningTranslationException(LearningTranslationFailure.SentenceTokenizerUnavailable);
            }

            previousSentenceEnd = sentence.Location + sentence.Length;
        }

        var probe = caret;
        while (probe > 0 && char.IsWhiteSpace(window[probe - 1]) && !IsNewline(window[probe - 1])) probe--;

        LearningTextRange? previous = null;
        LearningTextRange? atCaret = null;
        foreach (var candidate in sentences)
        {
            if (caret >= candidate.Location && caret < candidate.Location + candidate.Length)
            {
                atCaret = candidate;
                break;
            }

            if (probe >= candidate.Location && probe <= candidate.Location + candidate.Length)
                previous = candidate;
        }

        var resolved = atCaret ?? previous;
        if (resolved is null)
            throw new LearningTranslationException(LearningTranslationFailure.NoSentenceAtCaret);

        var sentenceEnd = resolved.Value.Location + resolved.Value.Length;
        if ((windowRange.Location > 0 && resolved.Value.Location == 0) ||
            (windowRange.Location + windowRange.Length < value.Length && sentenceEnd == window.Length))
        {
            throw new LearningTranslationException(LearningTranslationFailure.SentenceBeyondVerifiedRange);
        }

        var absoluteRange = new LearningTextRange(windowRange.Location + resolved.Value.Location, resolved.Value.Length);
        _ = Text(value, absoluteRange);
        return absoluteRange;
    }

    /// <summary>Validates a whole source string before it crosses into a prompt.</summary>
    public static void ValidateSource(string source)
    {
        ValidateCompleteText(source);
        if (!ContainsLetter(source))
            throw new LearningTranslationException(LearningTranslationFailure.NoConvertibleText);
    }

    /// <summary>Checks the platform string encoding and byte cap without imposing selection policy.</summary>
    public static void ValidateCompleteText(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsWellFormedUtf16(source))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidSourceRange);
        if (Encoding.UTF8.GetByteCount(source) > ModelContextBudget.MaximumTextBytes)
            throw ModelContextBudget.OversizedTarget();
    }

    private static bool ContainsLetter(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsLetter(rune)) return true;
        }

        return false;
    }

    private static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return false;
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidRange(string value, LearningTextRange range, bool allowEmpty) =>
        range.Location >= 0 && range.Length >= 0 && (allowEmpty || range.Length > 0) &&
        range.Location <= value.Length && range.Length <= value.Length - range.Location;

    private static LearningTextRange SentenceWindow(string value, int caret, int[] textElementStarts)
    {
        var start = Math.Max(0, caret - SentenceNeighborhoodUtf16Units);
        var end = caret + Math.Min(SentenceNeighborhoodUtf16Units, value.Length - caret);
        var startBoundary = ExpandStartToTextElement(value, start, textElementStarts);
        var endBoundary = ExpandEndToTextElement(value, end, textElementStarts);
        return new LearningTextRange(startBoundary, endBoundary - startBoundary);
    }

    private static int ExpandStartToTextElement(string value, int location, int[] textElementStarts)
    {
        if (IsTextElementBoundary(value, location, textElementStarts)) return location;
        var insertionPoint = ~Array.BinarySearch(textElementStarts, location);
        return textElementStarts[insertionPoint - 1];
    }

    private static int ExpandEndToTextElement(string value, int location, int[] textElementStarts)
    {
        if (IsTextElementBoundary(value, location, textElementStarts)) return location;
        var insertionPoint = ~Array.BinarySearch(textElementStarts, location);
        return insertionPoint < textElementStarts.Length ? textElementStarts[insertionPoint] : value.Length;
    }

    private static bool IsTextElementBoundary(string value, int location, int[] textElementStarts) =>
        location == 0 || location == value.Length || Array.BinarySearch(textElementStarts, location) >= 0;

    private static bool IsNewline(char value) => value is '\r' or '\n';
}
