using System.Globalization;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.ImageTransferHost;

/// <summary>
/// One explicit read owns this short-lived process. The parent can terminate it when a foreign
/// clipboard renderer blocks, preserving Mac's prompt UI cancellation without an abandoned
/// in-process native thread. This is fault/lifetime isolation, not a lower-privilege sandbox.
/// No application, inference, file import, shell execution or arbitrary command is started here.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments is ["--ole-drop", var parent, var label]) return OleDropWindow.Run(parent, false, label);
        if (arguments is ["--ole-drop-test", var testParent]) return OleDropWindow.Run(testParent, true);
        if (arguments is not ["--clipboard", var sequence] || !uint.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out uint expected)) return 2;
        using var output = Console.OpenStandardOutput(); byte[]? bytes;
        try { bytes = new VocabularyClipboardImageReader().Read(expected); }
        catch (Exception) { VocabularyImageTransferProtocol.Write(output, null, failed: true); return 0; }
        VocabularyImageTransferProtocol.Write(output, bytes); return 0;
    }
}
