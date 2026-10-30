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
    /// <summary>Present only for a separately verified visual pointer or explicit region capture.</summary>
    public ReadingScreenReceipt? ScreenReceipt { get; init; }
    /// <summary>Actual selected-range geometry; the presentation anchor is not OCR evidence.</summary>
    public ScreenRect? SelectionBounds { get; init; }
    /// <summary>Captured foreground-window geometry for optional visual enrichment, not text-range authority.</summary>
    public ScreenRect? OriginWindowBounds { get; init; }
    public ReadingSelectionGesture? Gesture { get; init; }
}

public enum ReadingGestureKind { Drag, DoubleClick }
public sealed record ReadingSelectionGesture(Guid SelectionId, double StartX, double StartY, double EndX, double EndY,
    ReadingGestureKind Kind, DateTimeOffset CompletedAt);

public sealed record ReadingScreenReceipt(Guid EvidenceId, ScreenRect FieldBounds, ScreenRect DisplayBounds, ScreenRect Region)
{
    /// <summary>Explicit visual acquisition has an origin window, not an editable field or caret.
    /// FieldBounds retains the older name for the permitted capture surface, which may be a display.</summary>
    public ReadingVisualOrigin? WindowOrigin { get; init; }
}
public sealed record ReadingVisualOrigin(Guid Id, ScreenRect WindowBounds);

public enum ReadingBoundary { Container, LayoutBlock, Sentence, Crop, Unknown, Deadline }
public enum ReadingContextMethod { Characters, Block, Paragraph, Sentence, MarkerWindow, MarkerWalk, Ocr, SelectionOcr }

/// <summary>One verified occurrence in a bounded context. Offsets are UTF-16, not a text search.</summary>
public sealed record ReadingContext(string Text, int TargetStart, int TargetLength, ReadingBoundary Left,
    ReadingBoundary Right, ReadingContextMethod Method)
{
    /// <summary>Original exact acquisition target (including selected whitespace), distinct from
    /// the trimmed sentence target shown to the user. Memory-only source/selection alignment evidence.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ReadingAcquisitionWindow? Window { get; init; }
}

public enum ReadingRouteReason { WholeSentence, WholeBlock, WholeUnverifiedSentence, TerminalText, ContextualUnit, StandaloneUnit }
public sealed record ReadingRoute(bool IsTranslation, ReadingRouteReason Reason, string? LookupText, bool IsSingleWord);
