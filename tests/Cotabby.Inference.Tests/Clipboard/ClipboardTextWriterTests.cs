using System.ComponentModel;
using Cotabby.Win32.Clipboard;

namespace Cotabby.Inference.Tests.Clipboard;

/// <summary>Runs the production transaction with fake handles; never opens the OS clipboard.</summary>
public sealed class ClipboardTextWriterTests
{
    [Fact]
    public void CompletePayloadIsPreparedBeforeOpeningAndTransferredBeforeClosing()
    {
        var backend = new Backend();
        const string text = "  cafe\u0301 👩🏽‍💻\r\n";
        Assert.True(new ClipboardTextWriter(backend).TryWrite(text, () => true));
        Assert.Equal(text, backend.Payload);
        Assert.Equal(new[] { "prepare", "open", "empty", "transfer", "close", "dispose" }, backend.Calls);
    }

    [Fact]
    public void InvalidTextNeverReachesTheNativeBoundary()
    {
        var backend = new Backend();
        Assert.False(new ClipboardTextWriter(backend).TryWrite("before\0after", () => true));
        Assert.Empty(backend.Calls);
    }

    [Fact]
    public void PreparationFailureCannotEmptyOrOpenTheClipboard()
    {
        var backend = new Backend { Prepared = false };
        Assert.False(new ClipboardTextWriter(backend).TryWrite("source", () => true));
        Assert.Equal(new[] { "prepare" }, backend.Calls);
    }

    [Fact]
    public void BusyClipboardFreesThePreparedPayloadWithoutRetryOrClose()
    {
        var backend = new Backend { Opened = false };
        Assert.False(new ClipboardTextWriter(backend).TryWrite("source", () => true));
        Assert.Equal(new[] { "prepare", "open", "dispose" }, backend.Calls);
    }

    [Fact]
    public void EmptyFailureClosesAndDisposesWithoutTransferring()
    {
        var backend = new Backend { Emptied = false };
        Assert.False(new ClipboardTextWriter(backend).TryWrite("source", () => true));
        Assert.Equal(new[] { "prepare", "open", "empty", "close", "dispose" }, backend.Calls);
    }

    [Fact]
    public void RevokedFinalGrantClosesWithoutEmptyingOrTransferring()
    {
        var backend = new Backend();
        Assert.False(new ClipboardTextWriter(backend).TryWrite("source", () => { backend.Calls.Add("grant"); return false; }));
        Assert.Equal(new[] { "prepare", "open", "grant", "close", "dispose" }, backend.Calls);
    }

    [Fact]
    public void CloseFailureCannotReportSuccessEvenAfterACompletedTransfer()
    {
        var backend = new Backend { Closed = false };
        Assert.False(new ClipboardTextWriter(backend).TryWrite("source", () => true));
        Assert.Equal(new[] { "prepare", "open", "empty", "transfer", "close", "dispose" }, backend.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeTransferFailureAlwaysClosesAndDisposesWithoutRetry(bool throws)
    {
        var backend = new Backend { Transferred = false, ThrowOnTransfer = throws };
        Assert.False(new ClipboardTextWriter(backend).TryWrite("source", () => true));
        Assert.Equal(new[] { "prepare", "open", "empty", "transfer", "close", "dispose" }, backend.Calls);
    }

    private sealed class Backend : IClipboardCopyBackend, IPreparedClipboardText
    {
        public List<string> Calls { get; } = [];
        public string? Payload;
        public bool Prepared = true, Opened = true, Emptied = true, Transferred = true, Closed = true, ThrowOnTransfer;
        public IPreparedClipboardText? Prepare(string text) { Calls.Add("prepare"); Payload = text; return Prepared ? this : null; }
        public bool Open() { Calls.Add("open"); return Opened; }
        public bool Empty() { Calls.Add("empty"); return Emptied; }
        public bool Close() { Calls.Add("close"); return Closed; }
        public bool Transfer() { Calls.Add("transfer"); if (ThrowOnTransfer) throw new Win32Exception(); return Transferred; }
        public void Dispose() => Calls.Add("dispose");
    }
}
