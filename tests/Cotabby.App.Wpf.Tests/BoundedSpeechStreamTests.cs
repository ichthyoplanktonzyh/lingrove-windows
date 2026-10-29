using System.Runtime.InteropServices;
using Cotabby.Win32.Speech;

namespace Cotabby.App.Wpf.Tests;

public sealed class BoundedSpeechStreamTests
{
    [Fact]
    public void OutputBudgetIsCheckedBeforeWriteAndSeekCannotBypassIt()
    {
        var stream = new BoundedSpeechStream(4, CancellationToken.None); stream.Write([1, 2, 3, 4], 4, IntPtr.Zero);
        Assert.Throws<COMException>(() => stream.Write([5], 1, IntPtr.Zero)); Assert.True(stream.Overflowed); Assert.Equal(new byte[] { 1, 2, 3, 4 }, stream.Bytes());
        Assert.Throws<COMException>(() => stream.Seek(5, 0, IntPtr.Zero));
    }
    [Fact]
    public void CancellationRejectsBeforeWriting()
    {
        using var cancellation = new CancellationTokenSource(); var stream = new BoundedSpeechStream(4, cancellation.Token); cancellation.Cancel();
        Assert.Throws<COMException>(() => stream.Write([1], 1, IntPtr.Zero)); Assert.Empty(stream.Bytes());
    }
}
