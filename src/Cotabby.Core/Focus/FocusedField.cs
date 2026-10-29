namespace Cotabby.Core.Focus;

/// <summary>Host evidence about uncommitted input; absence or failure of evidence is unknown.</summary>
public enum InputCompositionState
{
    Unknown = 0,
    Composing,
    Clear,
}

/// <summary>
/// A snapshot of the editable field holding focus, captured at one instant. A snapshot can be
/// stale by the time a caller acts on it, so read the field again before changing its text.
/// </summary>
public sealed record FocusedField
{
    /// <summary>Process id of the host app. Used for app-specific behavior toggles.</summary>
    public required int ProcessId { get; init; }

    /// <summary>Best-effort host process name without extension (e.g. <c>"chrome"</c>).</summary>
    public required string ProcessName { get; init; }

    /// <summary>
    /// Captured field text. A producer that omits source text must keep
    /// <see cref="IsTextWindowTruncated"/> true; the caret offset is relative to this string.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>Caret position as a UTF-16 offset into <see cref="Text"/>.</summary>
    public required int CaretOffset { get; init; }

    /// <summary>The text before the caret. A caret offset outside the text is clamped.</summary>
    public string PrecedingText => Text[..Math.Clamp(CaretOffset, 0, Text.Length)];

    /// <summary>The text after the caret.</summary>
    public string TrailingText => Text[Math.Clamp(CaretOffset, 0, Text.Length)..];

    /// <summary>Caret screen rectangle in physical pixels (DPI-aware).</summary>
    public required ScreenRect CaretRect { get; init; }

    /// <summary>True when the host gave no caret rectangle and <see cref="CaretRect"/> is the field's own.</summary>
    public bool CaretIsEstimated { get; init; }

    /// <summary>Field bounding rectangle in physical pixels.</summary>
    public required ScreenRect FieldRect { get; init; }

    /// <summary>True if the field is single-line (input, search box, etc.).</summary>
    public required bool IsSingleLine { get; init; }

    /// <summary>True if the field is a password field, where suggestions must be suppressed.</summary>
    public required bool IsSecure { get; init; }

    /// <summary>True when a non-empty range of text is selected.</summary>
    public bool HasSelection { get; init; }

    /// <summary>
    /// True only when the snapshot producer actually read the selection/caret, rather than
    /// assuming the end of a ValuePattern string. Unknown is the default: a producer must opt
    /// into edit eligibility after obtaining one actual selection/caret range.
    /// </summary>
    public bool HasReliableSelection { get; init; }

    /// <summary>Current host composition evidence. Only a positive clear observation permits work.</summary>
    public InputCompositionState Composition { get; init; }

    /// <summary>
    /// Observation epoch owned by the focus adapter. A change retires asynchronous captures even
    /// when their field, text and caret are otherwise identical. Zero means unavailable.
    /// </summary>
    public long CompositionSequence { get; init; }

    /// <summary>
    /// True when text was trimmed to a local window or completeness is unknown. Without an
    /// absolute document-range anchor, equal repeated windows cannot prove the caret stayed put.
    /// A producer opts out only after proving both sides complete; unknown snapshots cannot edit.
    /// </summary>
    public bool IsTextWindowTruncated { get; init; } = true;

    /// <summary>
    /// Capture epoch advanced by the focus owner on every focus transition and restart. Equal
    /// field IDs and text do not authorize a delayed edit after leaving and revisiting a field.
    /// </summary>
    public long FocusChangeSequence { get; init; }

    /// <summary>
    /// Identity of the field within its process, stable while focus stays in it. Empty when the
    /// host gives none.
    /// </summary>
    public string FieldId { get; init; } = "";

    /// <summary>Accessible name of the field, usually its visible label or placeholder.</summary>
    public string Label { get; init; } = "";

    /// <summary>Help text or placeholder the host exposes for the field.</summary>
    public string HelpText { get; init; } = "";

    /// <summary>Automation id. In Chromium hosts this is the DOM id.</summary>
    public string AutomationId { get; init; } = "";

    /// <summary>Class name. In Chromium hosts this is the DOM class list.</summary>
    public string ClassName { get; init; } = "";

    /// <summary>Title of the window that holds the field.</summary>
    public string WindowTitle { get; init; } = "";
}

/// <summary>Screen rectangle in physical pixels.</summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public static ScreenRect Empty => default;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
