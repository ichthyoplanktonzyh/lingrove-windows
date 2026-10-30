using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Focus;

namespace Cotabby.Win32.Insertion;

/// <summary>
/// Connects Core's guarded edit transaction to fresh UI Automation reads, one foreground-checked
/// SendInput batch, and the conditional clipboard lease. It owns no long-lived focus snapshot: each
/// read asks the shared tracker again so Core can compare the result with the caller's target.
/// </summary>
public sealed class Win32InsertionBackend : ITextInsertionBackend, IConditionalTextInsertionBackend
{
    private static readonly TimeSpan PasteConsumeDelay = TimeSpan.FromMilliseconds(80);

    private readonly IFocusTracker _focus;
    private readonly IKeyboardEditDispatcher _keyboard;
    private readonly IClipboardPasteLeaseFactory _clipboard;
    private readonly Func<Task> _waitForPaste;

    /// <summary>Build the production adapter around the app's shared focus tracker and clipboard reader.</summary>
    public Win32InsertionBackend(IFocusTracker focus, ClipboardTextReader? clipboardTextReader = null)
        : this(
            focus,
            new KeySender(),
            new Win32ClipboardPasteLeaseFactory(clipboardTextReader),
            () => Task.Delay(PasteConsumeDelay))
    {
    }

    internal Win32InsertionBackend(
        IFocusTracker focus,
        IKeyboardEditDispatcher keyboard,
        IClipboardPasteLeaseFactory clipboard,
        Func<Task> waitForPaste)
    {
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _waitForPaste = waitForPaste ?? throw new ArgumentNullException(nameof(waitForPaste));
    }

    /// <inheritdoc />
    public FocusedField? ReadFocusedField() => _focus.Refresh();

    public FocusedField? ReadFocusedField(Func<bool> permit) => _focus is UiaFocusTracker tracker ? tracker.RefreshGuarded(permit) : null;

    /// <inheritdoc />
    public bool CanType(string text) => SyntheticKeys.CanType(text);

    /// <inheritdoc />
    public bool SendUnicodeEdit(FocusedField target, int deleteUtf16Count, string text) =>
        _keyboard.SendUnicodeEdit(target, deleteUtf16Count, text);

    public bool SendUnicodeEdit(FocusedField target, int deleteUtf16Count, string text, Func<bool> permit) =>
        _keyboard is IConditionalKeyboardEditDispatcher conditional && conditional.SendUnicodeEdit(target, deleteUtf16Count, text, permit);

    /// <inheritdoc />
    public bool SendPasteEdit(FocusedField target, int deleteUtf16Count, IClipboardPasteLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return _keyboard.SendPasteEdit(target, deleteUtf16Count, () => lease.IsCurrent);
    }

    public bool SendPasteEdit(FocusedField target, int deleteUtf16Count, IClipboardPasteLease lease, Func<bool> permit) =>
        _keyboard.SendPasteEdit(target, deleteUtf16Count, () => lease.IsCurrent && permit());

    /// <inheritdoc />
    public Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct) =>
        _clipboard.PrepareAsync(text, ct);

    /// <inheritdoc />
    public Task WaitForPasteAsync() => _waitForPaste();
}

internal interface IKeyboardEditDispatcher
{
    bool SendUnicodeEdit(FocusedField target, int deleteUtf16Count, string text);
    bool SendPasteEdit(FocusedField target, int deleteUtf16Count, Func<bool> clipboardIsCurrent);
}

internal interface IConditionalKeyboardEditDispatcher
{
    bool SendUnicodeEdit(FocusedField target, int deleteUtf16Count, string text, Func<bool> permit);
}
