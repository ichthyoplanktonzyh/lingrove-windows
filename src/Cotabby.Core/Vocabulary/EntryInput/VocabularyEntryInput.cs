using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary.EntryInput;

/// <summary>Unsaved form or import row. Archive ownership assigns identities and commits it.</summary>
public sealed record VocabularyEntryInput
{
    public VocabularyEntryInput(string Expression = "", string DictionaryForm = "", LearningLanguage? Language = LearningLanguage.English,
        VocabularyEntryKind Kind = VocabularyEntryKind.Word, string Meaning = "", LearningLanguage? MeaningLanguage = null,
        string Note = "", IReadOnlyList<string>? Tags = null, string Example = "", string ExampleTranslation = "")
    {
        this.Expression = Expression; this.DictionaryForm = DictionaryForm; this.Language = Language; this.Kind = Kind;
        this.Meaning = Meaning; this.MeaningLanguage = MeaningLanguage; this.Note = Note; this.Tags = Tags ?? [];
        this.Example = Example; this.ExampleTranslation = ExampleTranslation;
    }
    private IReadOnlyList<string> _tags = Array.Empty<string>();
    public string Expression { get; init; }
    public string DictionaryForm { get; init; }
    public LearningLanguage? Language { get; init; }
    public VocabularyEntryKind Kind { get; init; }
    public string Meaning { get; init; }
    public LearningLanguage? MeaningLanguage { get; init; }
    public string Note { get; init; }
    public IReadOnlyList<string> Tags { get => _tags; init => _tags = CollectionValues.List(value ?? throw new ArgumentNullException(nameof(value))); }
    public string Example { get; init; }
    public string ExampleTranslation { get; init; }
}
public enum VocabularyWordListError
{
    TooLarge, Encoding, UnsupportedFile, Empty, Mapping, InvalidInput, MalformedCsv,
    MissingExpression, Language, Kind, MissingExample, TranslationWithoutExample,
}

/// <summary>Failure category and physical source line, if the error belongs to a row.</summary>
public sealed class VocabularyWordListException(VocabularyWordListError error, int? line = null) : Exception(error.ToString())
{
    public VocabularyWordListError Error { get; } = error;
    public int? Line { get; } = line;
}

/// <summary>Pure validation shared by manual creation and mapped imports; no archive mutation.</summary>
public static class VocabularyEntryInputPolicy
{
    public static VocabularyEntryInput Validated(VocabularyEntryInput input)
    {
        if (input is null || input.Expression is null || input.DictionaryForm is null || input.Meaning is null
            || input.Note is null || input.Example is null || input.ExampleTranslation is null
            || input.Tags.Any(tag => tag is null))
            throw new VocabularyWordListException(VocabularyWordListError.InvalidInput);
        try
        {
            string expression = VocabularyCapturePolicy.Normalized(input.Expression);
            string dictionary = VocabularyCapturePolicy.Normalized(input.DictionaryForm);
            if (dictionary.Length == 0) dictionary = expression;
            string meaning = FoundationText.TrimWhitespaceAndNewlines(input.Meaning), note = FoundationText.TrimWhitespaceAndNewlines(input.Note);
            string example = FoundationText.TrimWhitespaceAndNewlines(input.Example), translation = FoundationText.TrimWhitespaceAndNewlines(input.ExampleTranslation);
            string[] tags = input.Tags.Select(VocabularyCapturePolicy.Normalized).Where(t => t.Length != 0)
                .Distinct(StringComparer.Ordinal).Order(ScalarOrder).ToArray();
            if (expression.Length == 0 || !Fits(expression, 32_000) || !Fits(dictionary, 32_000)
                || !Fits(meaning, 32_000) || !Fits(note, 262_144) || !Fits(example, 262_144)
                || !Fits(translation, 262_144) || tags.Length > 64 || tags.Any(t => !Fits(t, 200))
                || !Enum.IsDefined(input.Kind) || (input.Language is { } language && !Enum.IsDefined(language))
                || (input.MeaningLanguage is { } meaningLanguage && !Enum.IsDefined(meaningLanguage)))
                throw new VocabularyWordListException(VocabularyWordListError.InvalidInput);
            if (example.Length == 0 && translation.Length != 0)
                throw new VocabularyWordListException(VocabularyWordListError.TranslationWithoutExample);
            return input with { Expression = expression, DictionaryForm = dictionary, Meaning = meaning,
                Note = note, Tags = Array.AsReadOnly(tags), Example = example, ExampleTranslation = translation };
        }
        catch (VocabularyContentValidationException) { throw new VocabularyWordListException(VocabularyWordListError.InvalidInput); }
        catch (EncoderFallbackException) { throw new VocabularyWordListException(VocabularyWordListError.InvalidInput); }
    }

    // Swift compares canonically normalized strings lexicographically by UTF-8. Input tags
    // are already NFC; scalar order is equivalent to UTF-8 byte order, including supplementary text.
    private static readonly IComparer<string> ScalarOrder = Comparer<string>.Create((left, right) =>
    {
        var a = left.EnumerateRunes().GetEnumerator(); var b = right.EnumerateRunes().GetEnumerator();
        while (true)
        {
            bool hasA = a.MoveNext(), hasB = b.MoveNext();
            if (!hasA || !hasB) return hasA.CompareTo(hasB);
            int order = a.Current.Value.CompareTo(b.Current.Value);
            if (order != 0) return order;
        }
    });

    private static bool Fits(string value, int limit) => !value.Contains('\0') && new UTF8Encoding(false, true).GetByteCount(value) <= limit;
}
