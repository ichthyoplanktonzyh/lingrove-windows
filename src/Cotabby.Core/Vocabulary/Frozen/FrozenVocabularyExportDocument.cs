using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Detached current portable export values. Dates remain source Double until rendering.</summary>
public sealed class FrozenVocabularyExportDocument(IEnumerable<FrozenVocabularyExportDocument.Entry> entries)
{
    public string Format => "lingrove-vocabulary-export";
    public int Version { get; init; } = 1;
    public VocabularySemanticIndex? Semantics { get; init; }
    public VocabularyClassificationIndex? Classification { get; init; }
    public IReadOnlyList<Entry> Entries { get; } = CollectionValues.List(entries);

    /// <summary>Headword identity and ordered projected senses, owned by this document.</summary>
    public sealed class Entry(Guid id, string expression, string dictionaryForm, LearningLanguage? language,
        VocabularyEntryKind kind, IEnumerable<Sense> senses)
    {
        public Guid Id { get; } = id;
        public string Expression { get; } = expression;
        public string DictionaryForm { get; } = dictionaryForm;
        public LearningLanguage? Language { get; } = language;
        public VocabularyEntryKind Kind { get; } = kind;
        public IReadOnlyList<Sense> Senses { get; } = CollectionValues.List(senses);
    }
    /// <summary>Selected sense material; null option fields differ from enabled empty lists.</summary>
    public sealed class Sense(Guid id, SourceDate createdAt, string? meaning, LearningLanguage? meaningLanguage,
        IEnumerable<VocabularyMeaning>? otherMeanings, string? note, IEnumerable<string>? tags,
        IEnumerable<Usage>? examples, IEnumerable<Explanation>? explanations, IEnumerable<Block>? content)
    {
        public Guid Id { get; } = id;
        public SourceDate CreatedAt { get; } = createdAt;
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
    public sealed class Usage(Guid id, SourceDate createdAt, string text, Resources.VocabularyExampleCompleteness completeness,
        IEnumerable<VocabularyTextRange> ranges, string translation, LearningLanguage? translationLanguage,
        IEnumerable<VocabularyMeaning> otherTranslations, string? meaning, LearningLanguage? meaningLanguage,
        IEnumerable<Explanation>? explanations)
    {
        public Guid Id { get; } = id;
        public SourceDate CreatedAt { get; } = createdAt;
        public string Text { get; } = text;
        public Resources.VocabularyExampleCompleteness Completeness { get; } = completeness;
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
