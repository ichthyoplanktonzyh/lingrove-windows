using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Insertion;

namespace Cotabby.Inference.Tests.Insertion;

public sealed class Win32InsertionBackendTests
{
    [Fact]
    public void ReadFocusedField_RefreshesTheSharedTrackerForEachRead()
    {
        var focus = new FakeFocusTracker();
        var backend = CreateBackend(focus);

        Assert.Same(focus.Field, backend.ReadFocusedField());
        Assert.Same(focus.Field, backend.ReadFocusedField());
        Assert.Equal(2, focus.RefreshCount);
    }

    [Fact]
    public void UnicodeEdit_DelegatesTheWholeDeleteAndTextTransactionOnce()
    {
        var focus = new FakeFocusTracker();
        var keyboard = new FakeKeyboardDispatcher();
        var backend = CreateBackend(focus, keyboard);

        bool sent = backend.SendUnicodeEdit(focus.Field, deleteUtf16Count: 3, "tion");

        Assert.True(sent);
        Assert.Equal((focus.Field, 3, "tion"), keyboard.UnicodeEdit!.Value);
        Assert.Equal(1, keyboard.UnicodeEditCalls);
    }

    [Fact]
    public async Task PasteEdit_PreparesLeaseAndWaitsWithoutAnyNativeClipboardOrInputCall()
    {
        var focus = new FakeFocusTracker();
        var keyboard = new FakeKeyboardDispatcher();
        var clipboard = new FakeClipboardPasteLeaseFactory();
        bool waited = false;
        var backend = new Win32InsertionBackend(
            focus,
            keyboard,
            clipboard,
            () =>
            {
                waited = true;
                return Task.CompletedTask;
            });

        using var cancellation = new CancellationTokenSource();
        IClipboardPasteLease? lease = await backend.PreparePasteAsync("a longer continuation", cancellation.Token);
        bool sent = backend.SendPasteEdit(focus.Field, deleteUtf16Count: 2, lease!);
        await backend.WaitForPasteAsync();

        Assert.Same(clipboard.Lease, lease);
        Assert.Equal(("a longer continuation", cancellation.Token), clipboard.Prepared!.Value);
        Assert.True(sent);
        Assert.Equal((focus.Field, 2), keyboard.PasteEdit!.Value);
        Assert.True(keyboard.ClipboardIsCurrent!());
        clipboard.Lease.IsCurrent = false;
        Assert.False(keyboard.ClipboardIsCurrent());
        Assert.True(waited);
    }

    [Fact]
    public void CanTypeUsesTheSharedBoundedUnicodePathDecision()
    {
        var backend = CreateBackend(new FakeFocusTracker());

        Assert.True(backend.CanType("small continuation"));
        Assert.False(backend.CanType(new string('x', SyntheticKeys.MaxTypedChars + 1)));
        Assert.False(backend.CanType("line one\nline two"));
    }

    private static Win32InsertionBackend CreateBackend(
        FakeFocusTracker focus,
        FakeKeyboardDispatcher? keyboard = null) =>
        new(
            focus,
            keyboard ?? new FakeKeyboardDispatcher(),
            new FakeClipboardPasteLeaseFactory(),
            () => Task.CompletedTask);

    private sealed class FakeKeyboardDispatcher : IKeyboardEditDispatcher
    {
        public int UnicodeEditCalls { get; private set; }
        public (FocusedField Target, int DeleteCount, string Text)? UnicodeEdit { get; private set; }
        public (FocusedField Target, int DeleteCount)? PasteEdit { get; private set; }
        public Func<bool>? ClipboardIsCurrent { get; private set; }

        public bool SendUnicodeEdit(FocusedField target, int deleteUtf16Count, string text)
        {
            UnicodeEditCalls++;
            UnicodeEdit = (target, deleteUtf16Count, text);
            return true;
        }

        public bool SendPasteEdit(FocusedField target, int deleteUtf16Count, Func<bool> clipboardIsCurrent)
        {
            PasteEdit = (target, deleteUtf16Count);
            ClipboardIsCurrent = clipboardIsCurrent;
            return true;
        }
    }

    private sealed class FakeClipboardPasteLeaseFactory : IClipboardPasteLeaseFactory
    {
        public FakeClipboardPasteLease Lease { get; } = new();
        public (string Text, CancellationToken Token)? Prepared { get; private set; }

        public Task<IClipboardPasteLease?> PrepareAsync(string text, CancellationToken ct)
        {
            Prepared = (text, ct);
            return Task.FromResult<IClipboardPasteLease?>(Lease);
        }
    }

    private sealed class FakeClipboardPasteLease : IClipboardPasteLease
    {
        public bool IsCurrent { get; set; } = true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFocusTracker : IFocusTracker
    {
        public FocusedField Field { get; } = new()
        {
            ProcessId = 123,
            ProcessName = "fake-editor",
            Text = "continuation",
            CaretOffset = 12,
            CaretRect = ScreenRect.Empty,
            FieldRect = ScreenRect.Empty,
            IsSingleLine = false,
            IsSecure = false,
            HasReliableSelection = true,
            FocusChangeSequence = 1,
            Composition = InputCompositionState.Clear,
            CompositionSequence = 1,
            IsTextWindowTruncated = false,
            FieldId = "fake:field",
        };

        public int RefreshCount { get; private set; }
        public FocusedField? Current => Field;
        public event EventHandler<FocusedField?>? FocusChanged
        {
            add { }
            remove { }
        }

        public FocusedField? Refresh()
        {
            RefreshCount++;
            return Field;
        }

        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
