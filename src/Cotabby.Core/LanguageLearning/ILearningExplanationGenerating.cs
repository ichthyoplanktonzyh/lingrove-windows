namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// App-owned explanation service borrowed by a future card/session. It accepts frozen targets and
/// exposes only complete records. Saved lookup is optional notebook integration; navigation never
/// implies model generation. Implementations never borrow translation/autocomplete task state.
/// </summary>
public interface ILearningExplanationGenerating
{
    event Action? Changed;
    LearningLanguage Language { get; }
    bool IsConfigured { get; }
    /// <summary>The current immutable destination, without credentials, for pre-request disclosure.</summary>
    LearningModelSnapshot? Profile => null;
    Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId,
        bool refresh = false, CancellationToken cancellationToken = default);
    Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input);
    LearningExplanationRecord? SavedRecord(LearningExplanationInput input);
    void RecordDisplay(string requestId);
}
