using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>Mac export option identifiers; labels belong to presentation, formats to the later renderer.</summary>
public enum VocabularyExportFormat { Text, Csv, Json }
public enum VocabularyExportTextStyle { Expressions, Notes }
public enum VocabularyExportScope { All, Filtered, Selected }

public static class VocabularyExportFormats
{
    public static string Code(this VocabularyExportFormat format) => format switch
    { VocabularyExportFormat.Text => "text", VocabularyExportFormat.Csv => "csv", VocabularyExportFormat.Json => "json", _ => throw new ArgumentOutOfRangeException(nameof(format)) };
    public static string FileExtension(this VocabularyExportFormat format) => format == VocabularyExportFormat.Text ? "txt" : format.Code();
}

/// <summary>
/// One export sheet's immutable option snapshot, independent of editing, inference and Anki.
/// A future sheet replaces this value when controls change; the projection/formatter consumes
/// that same snapshot. Expression-list style suppresses media only for the TXT format.
/// </summary>
public sealed record VocabularyExportConfiguration
{
    public VocabularyExportFormat Format { get; init; } = VocabularyExportFormat.Text;
    public VocabularyExportTextStyle TextStyle { get; init; } = VocabularyExportTextStyle.Notes;
    public bool Meanings { get; init; } = true;
    public bool Examples { get; init; } = true;
    public bool Notes { get; init; } = true;
    public bool Explanation { get; init; }
    public bool Content { get; init; } = true;
    public bool Images { get; init; }
    public bool SpreadsheetCompatibility { get; init; } = true;
    public bool IsExpressionList => Format == VocabularyExportFormat.Text && TextStyle == VocabularyExportTextStyle.Expressions;
    public bool IncludesImages => !IsExpressionList && Content && Images;
}

/// <summary>
/// An export sheet owns frozen lists of exact entry/sense IDs offered when it opens. Reading a
/// scope returns that captured order, never refreshes against a live notebook or expands aliases.
/// Constructing the UI's all/filtered/batch lists remains the notebook window's responsibility.
/// </summary>
public sealed class VocabularyExportRequest
{
    public VocabularyExportRequest(IEnumerable<VocabularySelection> all, IEnumerable<VocabularySelection> filtered,
        IEnumerable<VocabularySelection> selected, VocabularyExportScope initialScope)
    { All = CollectionValues.List(all); Filtered = CollectionValues.List(filtered); Selected = CollectionValues.List(selected); InitialScope = initialScope; }
    public Guid Id { get; } = Guid.NewGuid();
    public IReadOnlyList<VocabularySelection> All { get; }
    public IReadOnlyList<VocabularySelection> Filtered { get; }
    public IReadOnlyList<VocabularySelection> Selected { get; }
    public VocabularyExportScope InitialScope { get; }
    public IReadOnlyList<VocabularySelection> Selections(VocabularyExportScope scope) => scope switch
    { VocabularyExportScope.All => All, VocabularyExportScope.Filtered => Filtered, VocabularyExportScope.Selected => Selected, _ => throw new ArgumentOutOfRangeException(nameof(scope)) };
}

/// <summary>
/// Portable learning-material projection owned by one prepared export, not a full backup of the
/// notebook. Nested values expose only the Mac export fields: no host identity, runtime settings,
/// review receipt or model metadata. All collections detach on construction; absent options stay
/// null while enabled empty collections remain empty. This type defines no byte codec or file IO.
/// </summary>
public sealed class VocabularyExportDocument(IEnumerable<VocabularyExportDocument.Entry> entries)
{
    public string Format => "lingrove-vocabulary-export";
    public int Version => 1;
    public IReadOnlyList<Entry> Entries { get; } = CollectionValues.List(entries);

    /// <summary>Headword identity and ordered projected senses, owned by this document.</summary>
    public sealed class Entry(Guid id, string expression, string dictionaryForm, LearningLanguage language,
        VocabularyEntryKind kind, IEnumerable<Sense> senses)
    {
        public Guid Id { get; } = id;
        public string Expression { get; } = expression;
        public string DictionaryForm { get; } = dictionaryForm;
        public LearningLanguage Language { get; } = language;
        public VocabularyEntryKind Kind { get; } = kind;
        public IReadOnlyList<Sense> Senses { get; } = CollectionValues.List(senses);
    }
    /// <summary>Selected sense material; null option fields differ from enabled empty lists.</summary>
    public sealed class Sense(Guid id, DateTimeOffset createdAt, string? meaning, LearningLanguage? meaningLanguage,
        IEnumerable<VocabularyMeaning>? otherMeanings, string? note, IEnumerable<string>? tags,
        IEnumerable<Usage>? examples, IEnumerable<Explanation>? explanations, IEnumerable<Block>? content)
    {
        public Guid Id { get; } = id;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public string? Meaning { get; } = meaning;
        public LearningLanguage? MeaningLanguage { get; } = meaningLanguage;
        public IReadOnlyList<VocabularyMeaning>? OtherMeanings { get; } = CollectionValues.OptionalList(otherMeanings);
        public string? Note { get; } = note;
        public IReadOnlyList<string>? Tags { get; } = CollectionValues.OptionalList(tags);
        public IReadOnlyList<Usage>? Examples { get; } = CollectionValues.OptionalList(examples);
        public IReadOnlyList<Explanation>? Explanations { get; } = CollectionValues.OptionalList(explanations);
        public IReadOnlyList<Block>? Content { get; } = CollectionValues.OptionalList(content);
    }
    /// <summary>Encountered text, exact UTF-16 ranges and selected learning values without source-host metadata.</summary>
    public sealed class Usage(Guid id, DateTimeOffset createdAt, string text, VocabularyExampleCompleteness completeness,
        IEnumerable<VocabularyTextRange> ranges, string translation, LearningLanguage? translationLanguage,
        IEnumerable<VocabularyMeaning> otherTranslations, string? meaning, LearningLanguage? meaningLanguage,
        IEnumerable<Explanation>? explanations)
    {
        public Guid Id { get; } = id;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public string Text { get; } = text;
        public VocabularyExampleCompleteness Completeness { get; } = completeness;
        public IReadOnlyList<VocabularyTextRange> Ranges { get; } = CollectionValues.List(ranges);
        public string Translation { get; } = translation;
        public LearningLanguage? TranslationLanguage { get; } = translationLanguage;
        public IReadOnlyList<VocabularyMeaning> OtherTranslations { get; } = CollectionValues.List(otherTranslations);
        public string? Meaning { get; } = meaning;
        public LearningLanguage? MeaningLanguage { get; } = meaningLanguage;
        public IReadOnlyList<Explanation>? Explanations { get; } = CollectionValues.OptionalList(explanations);
    }
    /// <summary>Opt-in saved explanation wording, detached from its generation input and model receipt.</summary>
    public sealed class Explanation(LearningLanguage language, string meaning, string detail)
    {
        public LearningLanguage Language { get; } = language;
        public string Meaning { get; } = meaning;
        public string Detail { get; } = detail;
    }
    /// <summary>Ordered authored text or image metadata; no bytes or live storage path are retained.</summary>
    public sealed class Block(Guid id, VocabularyTextContent? text, VocabularyImageReference? image, string? filename, string? caption)
    {
        public Guid Id { get; } = id;
        public VocabularyTextContent? Text { get; } = text;
        public VocabularyImageReference? Image { get; } = image;
        public string? Filename { get; } = filename;
        public string? Caption { get; } = caption;
    }
}

public enum VocabularyExportFailure { Empty, Changed, MissingImage, TooLarge }

/// <summary>Fixed export-domain failures; source paths, notebook contents and providers stay out.</summary>
public sealed class VocabularyExportException(VocabularyExportFailure failure) : Exception(failure switch
{
    VocabularyExportFailure.Empty => "Choose at least one sense to export.",
    VocabularyExportFailure.Changed => "The selected senses changed. Reopen Export to refresh the selection.",
    VocabularyExportFailure.MissingImage => "An export image is missing. Restore it or export without images.",
    VocabularyExportFailure.TooLarge => "This export is too large. Export a smaller selection.",
    _ => throw new ArgumentOutOfRangeException(nameof(failure))
})
{ public VocabularyExportFailure Failure { get; } = failure; }
