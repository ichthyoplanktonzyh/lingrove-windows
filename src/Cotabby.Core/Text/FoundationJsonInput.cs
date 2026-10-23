using System.Text;

namespace Cotabby.Core.Text;

/// <summary>Foundation JSON encoding detection shared by notebook storage, local Anki and speech
/// framing. Existing callers eagerly validate UTF-8; a Decodable-shaped reader can opt into lazy
/// string validation and validate each key/known string itself. Transcoding remains strict, and
/// consumers retain shape/depth/error policy. One boundary preserves observed BOM precedence.</summary>
internal static class FoundationJsonInput
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static ReadOnlyMemory<byte> Utf8Input(ReadOnlyMemory<byte> input, bool validateUtf8 = true)
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
        // Eager JSON consumers validate all UTF-8, including unknown fields. The speech header
        // reader explicitly opts out and validates keys/known strings when they are decoded.
        if (validateUtf8) _ = StrictUtf8.GetCharCount(bytes[skip..]);
        return input[skip..];
    }
}
