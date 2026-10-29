using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Cotabby.Win32.Reading;
using Windows.Graphics.Imaging;

namespace Cotabby.App.Wpf.Tests;

public sealed class ReadingNativeBufferCleanupTests
{
    [Fact]
    public void ScreenshotRequiresObservedHostOrActualChild()
    {
        Assert.True(WindowsReadingPixels.AcceptObservedField(10, 10, true, (_, _) => false));
        Assert.True(WindowsReadingPixels.AcceptObservedField(10, 20, true, (_, _) => true));
        Assert.False(WindowsReadingPixels.AcceptObservedField(10, 20, false, (_, _) => true));
        Assert.False(WindowsReadingPixels.AcceptObservedField(10, 20, true, (_, _) => false));
    }
    [Fact]
    public void SyntheticDibMemoryIsClearedBeforeRelease()
    {
        const int size = 1024; var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(Enumerable.Repeat((byte)42, size).ToArray(), 0, pointer, size);
            WindowsReadingPixels.ClearDib(pointer, size);
            var result = new byte[size]; Marshal.Copy(pointer, result, 0, size);
            Assert.All(result, value => Assert.Equal(0, value));
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SyntheticWinRtCopiesClearOnlyAfterInFlightWorkDrains(bool cancelled)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        const int size = 16 * 16 * 4;
        var buffer = new Windows.Storage.Streams.Buffer(size) { Length = size };
        using (var stream = buffer.AsStream()) stream.Write(Enumerable.Repeat((byte)42, size).ToArray());
        using var image = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, 16, 16, BitmapAlphaMode.Ignore);
        var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = WindowsReadingOcr.FinishAndClearAsync(work.Task, buffer, image, size);
        Assert.False(cleanup.IsCompleted); if (cancelled) work.SetCanceled(); else work.SetResult(); await cleanup;
        using (var stream = buffer.AsStream()) { var input = new byte[size]; stream.ReadExactly(input); Assert.All(input, value => Assert.Equal(0, value)); }
        var copy = new Windows.Storage.Streams.Buffer(size) { Length = size }; image.CopyToBuffer(copy);
        using (var stream = copy.AsStream()) { var pixels = new byte[size]; stream.ReadExactly(pixels); Assert.All(pixels, value => Assert.Equal(0, value)); }
    }
}
