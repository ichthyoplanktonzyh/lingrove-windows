namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Presentation supplies one localization snapshot for the Mac Vocabulary-table labels. The
/// renderer has no UI/localization service and does not vary its labels midway through a payload.
/// Defaults are the pinned source's English fallback strings, also used by native source probes.
/// </summary>
public sealed record VocabularyExportTextLabels
{
    public string DictionaryForm { get; init; } = "Dictionary form";
    public string Sense { get; init; } = "Sense";
    public string Notes { get; init; } = "Notes";
    public string Tags { get; init; } = "Tags";
    public string ExampleSentence { get; init; } = "Example sentence";
    public string ExampleTranslation { get; init; } = "Example translation";
    public string ContextualMeaning { get; init; } = "Contextual meaning";
    public string Image { get; init; } = "Image";
}
