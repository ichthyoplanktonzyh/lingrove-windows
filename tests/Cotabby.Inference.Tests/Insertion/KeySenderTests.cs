using Cotabby.Core.Focus;
using Cotabby.Win32.Insertion;
using Cotabby.Win32.Interop;

namespace Cotabby.Inference.Tests.Insertion;

public sealed class KeySenderTests
{
    [Fact]
    public void SendsOneCombinedDeleteAndTextBatchWhenTargetProcessIsForeground()
    {
        var native = new FakeKeyboardNativeApi { ForegroundProcessId = 123 };
        var sender = new KeySender(native);

        bool sent = sender.SendUnicodeEdit(Field(processId: 123), deleteCount: 2, text: "ing");

        Assert.True(sent);
        Assert.Equal(1, native.SendInputCalls);
        Assert.NotNull(native.LastInputBatch);
        Assert.Equal(
            SyntheticKeys.Edit([], deleteCount: 2, text: "ing").Count,
            native.LastInputBatch!.Length);
    }

    [Fact]
    public void PartialSendInputResultFailsWithoutRetryingAnySuffix()
    {
        var native = new FakeKeyboardNativeApi { ForegroundProcessId = 123 };
        native.ReturnedInputCount = 1;
        var sender = new KeySender(native);

        bool sent = sender.SendUnicodeEdit(Field(processId: 123), deleteCount: 1, text: "word");

        Assert.False(sent);
        Assert.Equal(1, native.SendInputCalls);
    }

    [Fact]
    public void ForegroundProcessMismatchRejectsBeforeNativeInput()
    {
        var native = new FakeKeyboardNativeApi { ForegroundProcessId = 456 };
        var sender = new KeySender(native);

        bool sent = sender.SendPasteEdit(Field(processId: 123), deleteCount: 1, clipboardIsCurrent: () => true);

        Assert.False(sent);
        Assert.Equal(0, native.SendInputCalls);
    }

    [Fact]
    public void PasteRechecksLeaseAfterModifierAndBatchPreparation()
    {
        var lease = new FakeClipboardPasteLease();
        var native = new FakeKeyboardNativeApi { ForegroundProcessId = 123 };
        native.OnGetAsyncKeyState = _ => lease.IsCurrent = false;
        var sender = new KeySender(native);

        bool sent = sender.SendPasteEdit(
            Field(processId: 123),
            deleteCount: 1,
            clipboardIsCurrent: () => lease.IsCurrent);

        Assert.False(sent);
        Assert.Equal(1, lease.IsCurrentChecks);
        Assert.Equal(0, native.SendInputCalls);
    }

    private static FocusedField Field(int processId) => new()
    {
        ProcessId = processId,
        ProcessName = "fake-editor",
        Text = "word",
        CaretOffset = 4,
        CaretRect = ScreenRect.Empty,
        FieldRect = ScreenRect.Empty,
        IsSingleLine = true,
        IsSecure = false,
        HasReliableSelection = true,
        FocusChangeSequence = 1,
        IsTextWindowTruncated = false,
        FieldId = "fake:field",
    };

    private sealed class FakeKeyboardNativeApi : IKeyboardNativeApi
    {
        public uint? ForegroundProcessId { get; set; }
        public int SendInputCalls { get; private set; }
        public uint? ReturnedInputCount { get; set; }
        public NativeMethods.INPUT[]? LastInputBatch { get; private set; }
        public Action<int>? OnGetAsyncKeyState { get; set; }

        public short GetAsyncKeyState(int virtualKey)
        {
            OnGetAsyncKeyState?.Invoke(virtualKey);
            return 0;
        }

        public uint SendInput(NativeMethods.INPUT[] inputs)
        {
            SendInputCalls++;
            LastInputBatch = inputs;
            return ReturnedInputCount ?? (uint)inputs.Length;
        }
    }

    private sealed class FakeClipboardPasteLease
    {
        private bool _isCurrent = true;

        public bool IsCurrent
        {
            get
            {
                IsCurrentChecks++;
                return _isCurrent;
            }
            set => _isCurrent = value;
        }

        public int IsCurrentChecks { get; private set; }
    }
}
