namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// A card's immutable explanation presentation, owned by its session. A revision changes whenever
/// work is retired; views marshal snapshots to their dispatcher and keep document/focus authority
/// separately. An existing complete record survives stop/failure while a refresh is pending.
/// </summary>
public sealed record LearningExplanationState
{
    public LearningExplanationInput? Input { get; init; }
    public LearningExplanationRecord? Record { get; init; }
    public bool IsGenerating { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid Revision { get; init; } = Guid.NewGuid();
}
