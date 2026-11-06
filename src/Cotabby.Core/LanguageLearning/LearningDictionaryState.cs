namespace Cotabby.Core.LanguageLearning;

public enum LearningDictionaryContentStatus { Loading, Result, Failed, AwaitingOnlineConfirmation }
public enum LearningDictionaryPage { Contextual, Dictionary }

/// <summary>
/// One transient card target. Document/target/request identities have separate lifetimes: a
/// component word changes the target without rereading the editor or changing its captured unit.
/// Dictionary and contextual explanation are independent presentations of that target. Generated
/// text never becomes a source-owned dictionary definition, user meaning or authorization to edit.
/// </summary>
public sealed record LearningDictionaryState
{
    public string? DocumentId { get; init; }
    public LearningUnit? Unit { get; init; }
    public LearningDictionaryWord? SelectedWord { get; init; }
    public string? ReferenceQuery { get; init; }
    public bool ReferenceIsExpression { get; init; }
    public LearningLanguage? Language { get; init; }
    public string? SpeechContext { get; init; }
    public LearningDictionaryPage Page { get; init; } = LearningDictionaryPage.Dictionary;
    public bool HasContextualExplanation { get; init; }
    public bool ExplanationConfigured { get; init; }
    public LearningLanguage? ExplanationLanguage { get; init; }
    public LearningExplanationState? Explanation { get; init; }
    public Guid TargetIdentity { get; init; } = Guid.NewGuid();
    public Guid RequestIdentity { get; init; } = Guid.NewGuid();
    public LearningDictionaryContentStatus Status { get; init; } = LearningDictionaryContentStatus.Loading;
    public LearningDictionaryResult? Result { get; init; }
    public string? ErrorMessage { get; init; }
    public DictionarySource? SelectedSource { get; init; }
    public IReadOnlyList<DictionarySource> AvailableSources { get; init; } = [];
    public IReadOnlyDictionary<DictionarySource, string> SourceLabels { get; init; } = new Dictionary<DictionarySource, string>();
    public string Title => ReferenceQuery ?? SelectedWord?.Text ?? Unit?.Surface ?? string.Empty;
    public string Query => ReferenceQuery ?? SelectedWord?.Text ?? Unit?.Query ?? string.Empty;
    public bool IsExpression => ReferenceQuery is not null ? ReferenceIsExpression : SelectedWord is null && Unit?.Kind is LearningUnitKind.Expression or LearningUnitKind.Pattern;
}
