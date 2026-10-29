using Cotabby.Core.Focus;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>Memory-only read receipt; native handles and write authority remain outside this value.</summary>
public sealed record ReadingSelection(Guid Id, string Text, int ProcessId, string FieldId, long FocusEpoch,
    int? SelectionStart, int SelectionLength, bool IsSecure, bool IsComplete,
    InputCompositionState Composition, long CompositionSequence, ScreenRect Anchor, ReadingContext? Context = null, LearningLanguage? LanguageHint = null,
    Guid? SelectionAnchorId = null)
{
    /// <summary>Nonblocking request-owned admission for final reading effects.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<bool>? Admission { get; init; }
    /// <summary>Verified origin process label, when available; never an editor handle.</summary>
    public string? SourceApplicationId { get; init; }
    /// <summary>Capture time is stable across retries and durable contextual saves.</summary>
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public ReadingSourceKind Source { get; init; } = ReadingSourceKind.Selection;
    /// <summary>Present only for a separately verified explicit screen-region capture.</summary>
    public ReadingScreenReceipt? ScreenReceipt { get; init; }
}

public sealed record ReadingScreenReceipt(Guid EvidenceId, ScreenRect FieldBounds, ScreenRect DisplayBounds, ScreenRect Region);

public enum ReadingBoundary { Container, LayoutBlock, Sentence, Crop, Unknown, Deadline }
public enum ReadingContextMethod { Characters, Block, Paragraph, Sentence, MarkerWindow, MarkerWalk }

/// <summary>One verified occurrence in a bounded context. Offsets are UTF-16, not a text search.</summary>
public sealed record ReadingContext(string Text, int TargetStart, int TargetLength, ReadingBoundary Left,
    ReadingBoundary Right, ReadingContextMethod Method);

public enum ReadingRouteReason { WholeSentence, WholeBlock, WholeUnverifiedSentence, TerminalText, ContextualUnit, StandaloneUnit }
public sealed record ReadingRoute(bool IsTranslation, ReadingRouteReason Reason, string? LookupText, bool IsSingleWord);
