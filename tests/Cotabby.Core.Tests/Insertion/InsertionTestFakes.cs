using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;

namespace Cotabby.Core.Tests.Insertion;

internal static class InsertionTestFields
{
    public static FocusedField Create(
        string text = "hello",
        int? caretOffset = null,
        string fieldId = "uia:window-1/field-2",
        int processId = 100,
        long focusChangeSequence = 1) => new()
    {
        ProcessId = processId,
        ProcessName = "editor",
        Text = text,
        CaretOffset = caretOffset ?? text.Length,
        CaretRect = new ScreenRect(10, 10, 1, 20),
        FieldRect = new ScreenRect(0, 0, 500, 300),
        IsSingleLine = false,
        IsSecure = false,
        FieldId = fieldId,
        FocusChangeSequence = focusChangeSequence,
        Composition = InputCompositionState.Clear,
        CompositionSequence = 1,
        HasReliableSelection = true,
        IsTextWindowTruncated = false,
    };
}

internal sealed record InsertionEdit(FocusedField Target, int DeleteUtf16Count, string Text);

internal sealed class FakeInsertionBackend : ITextInsertionBackend
{
    public FocusedField? Current { get; set; }
    public bool CanTypeResult { get; set; } = true;
    public bool UnicodeEditResult { get; set; } = true;
    public bool PasteEditResult { get; set; } = true;
    public bool ApplyEditBeforeReturning { get; set; } = true;
    public Exception? UnicodeEditException { get; set; }
    public Exception? PasteEditException { get; set; }
    public Exception? WaitForPasteException { get; set; }
    public Func<string, CancellationToken, Task<IClipboardPasteLease?>>? PrepareOverride { get; set; }
    public IClipboardPasteLease? DefaultLease { get; set; }

    public List<string> Events { get; } = [];
    public List<string> PreparedTexts { get; } = [];
    public List<InsertionEdit> UnicodeEdits { get; } = [];
    public List<InsertionEdit> PasteEdits { get; } = [];
    public int ReadCount { get; private set; }
    public int CanTypeCount { get; private set; }
    public int PrepareCount { get; private set; }
    public int PasteWaitCount { get; private set; }

    public FocusedField? ReadFocusedField()
    {
        ReadCount++;
        return Current;
    }

    public bool CanType(string text)
    {
        CanTypeCount++;
        return CanTypeResult;
    }

    public bool SendUnicodeEdit(FocusedField target, int deleteUtf16Count, string text)
    {
        Events.Add("unicode");
        if (UnicodeEditException is not null) throw UnicodeEditException;

        UnicodeEdits.Add(new InsertionEdit(target, deleteUtf16Count, text));
        if (ApplyEditBeforeReturning) ApplyEdit(target, deleteUtf16Count, text);
        return UnicodeEditResult;
    }

    public bool SendPasteEdit(FocusedField target, int deleteUtf16Count, IClipboardPasteLease lease)
    {
        Events.Add("paste");
        if (PasteEditException is not null) throw PasteEditException;
        if (!lease.IsCurrent) return false;

        PasteEdits.Add(new InsertionEdit(target, deleteUtf16Count, "<clipboard>"));
        if (ApplyEditBeforeReturning) ApplyEdit(target, deleteUtf16Count, "<clipboard>");
        return PasteEditResult;
    }

    public async Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct)
    {
        Events.Add("prepare");
        PreparedTexts.Add(text);
        PrepareCount++;
        return PrepareOverride is null
            ? DefaultLease
            : await PrepareOverride(text, ct).ConfigureAwait(false);
    }

    public Task WaitForPasteAsync()
    {
        Events.Add("wait");
        PasteWaitCount++;
        return WaitForPasteException is null
            ? Task.CompletedTask
            : Task.FromException(WaitForPasteException);
    }

    private void ApplyEdit(FocusedField target, int deleteUtf16Count, string text)
    {
        int start = target.CaretOffset - deleteUtf16Count;
        string updated = target.Text.Remove(start, deleteUtf16Count).Insert(start, text);
        Current = target with { Text = updated, CaretOffset = start + text.Length };
    }
}

internal sealed class FakeClipboardPasteLease : IClipboardPasteLease
{
    private readonly List<string> _events;

    public FakeClipboardPasteLease(List<string> events, bool isCurrent = true)
    {
        _events = events;
        IsCurrent = isCurrent;
    }

    public bool IsCurrent { get; set; }
    public bool WasDisposed { get; private set; }
    public Exception? DisposeException { get; set; }

    public ValueTask DisposeAsync()
    {
        _events.Add("dispose");
        WasDisposed = true;
        return DisposeException is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(DisposeException);
    }
}
