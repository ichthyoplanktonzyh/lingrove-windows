using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// The deterministic capture/usage identity and admission subset of Mac VocabularyPolicy.
/// A future transactional archive borrows these keys; they do not choose/merge a sense or write
/// storage. Entry spelling is NFC/whitespace-normalized only for its key. Usage keys retain exact
/// original text, ordered UTF-16 spans and completeness, excluding editable translations/UUIDs.
/// </summary>
public static class VocabularyCapturePolicy
{
    public const int MaximumTextBytes = 262_144;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Normalized(string text)
    {
        if (!LearningExplanationText.Fits(text, int.MaxValue)) throw new VocabularyContentValidationException();
        return string.Join(" ", GraphemeString.Words(text.Normalize(NormalizationForm.FormC)));
    }

    public static string EntryKey(VocabularyCapture capture) => Key([capture.Language.Code(), KindCode(capture.Kind), Normalized(capture.Expression)]);
    public static string ExampleKey(VocabularyExample? example) => example is null ? "no-context"
        : ExampleKey(example.Text, example.Ranges, example.Completeness);

    public static string ExampleKey(string text, IReadOnlyList<VocabularyTextRange> ranges, VocabularyExampleCompleteness completeness)
    {
        string spans = string.Join(",", ranges.Select(range => range.Location.ToString(CultureInfo.InvariantCulture)
            + ":" + range.Length.ToString(CultureInfo.InvariantCulture)));
        return Key([text, spans, completeness switch
        {
            VocabularyExampleCompleteness.Sentence => "sentence",
            VocabularyExampleCompleteness.Fragment => "fragment",
            _ => throw new VocabularyContentValidationException(),
        }]);
    }

    public static string CaptureKey(VocabularyCapture capture) => EntryKey(capture) + ":"
        + (capture.Example is null ? "standalone" : ExampleKey(capture.Example));

    /// <summary>
    /// Mirrors capture validation only: no invented nonempty/disjoint-range rule or annotation
    /// budget is added here. Full imported notebook validation and capture acquisition are distinct
    /// boundaries; this does not prove a live editor, sentence boundary or durable graph is valid.
    /// </summary>
    public static void Validate(VocabularyCapture capture)
    {
        if (capture is null || !Enum.IsDefined(capture.Language) || !Enum.IsDefined(capture.Kind)
            || !LearningExplanationText.Fits(capture.Expression, MaximumTextBytes) || Normalized(capture.Expression).Length == 0)
            throw new VocabularyContentValidationException();
        if (capture.Example is not { } example) return;
        if (!Enum.IsDefined(example.Completeness) || string.IsNullOrEmpty(example.Text)
            || !LearningExplanationText.Fits(example.Text, MaximumTextBytes)) throw new VocabularyContentValidationException();
        var boundaries = FoundationTextRanges.Boundaries(example.Text);
        if (!example.Ranges.All(range => FoundationTextRanges.IsValid(range.Location, range.Length, example.Text, boundaries)))
            throw new VocabularyContentValidationException();
    }

    private static string KindCode(VocabularyEntryKind kind) => kind switch
    {
        VocabularyEntryKind.Word => "word", VocabularyEntryKind.Expression => "expression",
        VocabularyEntryKind.Pattern => "pattern", VocabularyEntryKind.Sentence => "sentence",
        _ => throw new VocabularyContentValidationException(),
    };
    private static string Key(IReadOnlyList<string> parts)
    {
        try
        {
            string value = string.Concat(parts.Select(part => StrictUtf8.GetByteCount(part).ToString(CultureInfo.InvariantCulture) + ":" + part));
            return Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(value)));
        }
        catch (EncoderFallbackException) { throw new VocabularyContentValidationException(); }
    }
}
