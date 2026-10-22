using Cotabby.Win32.Clipboard;

namespace Cotabby.Inference.Tests.Clipboard;

public sealed class ClipboardFormatCopyPolicyTests
{
    [Theory]
    [InlineData(ClipboardNativeMethods.CF_UNICODETEXT)]
    [InlineData(ClipboardNativeMethods.CF_DIB)]
    [InlineData(ClipboardNativeMethods.CF_DIBV5)]
    [InlineData(ClipboardNativeMethods.CF_HDROP)]
    [InlineData(ClipboardNativeMethods.CF_LOCALE)]
    public void StandardGlobalMemoryFormatsAreEligibleForByteSnapshots(uint format)
    {
        Assert.True(ClipboardFormatCopyPolicy.IsStandardGlobalFormat(format));
    }

    [Theory]
    [InlineData("HTML Format")]
    [InlineData("html format")]
    [InlineData("Rich Text Format")]
    [InlineData("rich text format")]
    public void CommonRegisteredHtmlAndRtfFormatsAreEligible(string name)
    {
        Assert.True(ClipboardFormatCopyPolicy.IsSupportedRegisteredName(name));
    }

    [Theory]
    [InlineData("FileContents")]
    [InlineData("application/custom-stream")]
    [InlineData("")]
    public void UnknownRegisteredOrOpaqueFormatsFailClosed(string name)
    {
        Assert.False(ClipboardFormatCopyPolicy.IsSupportedRegisteredName(name));
    }

    [Theory]
    [InlineData(ClipboardNativeMethods.CF_BITMAP)]
    [InlineData(ClipboardNativeMethods.CF_METAFILEPICT)]
    [InlineData(ClipboardNativeMethods.CF_PALETTE)]
    [InlineData(ClipboardNativeMethods.CF_ENHMETAFILE)]
    public void GdiHandleFormatsAreNotTreatedAsGlobalMemory(uint format)
    {
        Assert.False(ClipboardFormatCopyPolicy.IsStandardGlobalFormat(format));
    }
}
