using System.Text;

namespace Cotabby.Core.Text;

/// <summary>Foundation JSON byte admission shared by notebook storage and the local Anki wire.
/// It only detects/transcodes strict UTF input; each consumer retains its own shape, depth and
/// failure mapping. Keeping this boundary shared prevents two subtly different BOM policies.</summary>
internal static class FoundationJsonInput
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static ReadOnlyMemory<byte> Utf8Input(ReadOnlyMemory<byte> input)
    {
        // Foundation detects UTF-16/32 as well as UTF-8. Preserve the observed BOM precedence:
        // FF FE is consumed as UTF-16, so a UTF-32LE BOM fails, although unmarked UTF-32LE works.
        // This belongs to decoding; encoding always emits UTF-8 without a BOM.
        var bytes = input.Span;
        Encoding? encoding = null; int skip = 0;
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) skip = 3;
        else if (bytes.StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        else if (bytes.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); skip = 4; }
        else if (bytes.Length >= 4)
        {
            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0) encoding = new UTF32Encoding(true, false, true);
            else if (bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0) encoding = new UTF32Encoding(false, false, true);
            else if (bytes[0] == 0 && bytes[2] == 0) encoding = new UnicodeEncoding(true, false, true);
            else if (bytes[1] == 0 && bytes[3] == 0) encoding = new UnicodeEncoding(false, false, true);
        }
        if (encoding is not null) return StrictUtf8.GetBytes(encoding.GetString(bytes[skip..]));
        // JsonDocument can defer string decoding. Validate every byte, including future fields.
        _ = StrictUtf8.GetCharCount(bytes[skip..]);
        return input[skip..];
    }
}
