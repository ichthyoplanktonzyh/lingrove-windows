using System.Text;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Pure Mac export text boundaries. A later full renderer will consume the portable document;
/// these helpers own literal CSV-cell escaping and the bounded UTF-8 preview independently of
/// notebook state, file dialogs, storage and locale. They never modify the original learning text.
/// </summary>
public static partial class VocabularyExportFormatter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static string CsvCell(string text, bool spreadsheetCompatibility = true)
    {
        bool formula = false;
        if (spreadsheetCompatibility)
        {
            foreach (var character in new GraphemeString(text))
            {
                if (character.IsWhitespace || character.IsNewline) continue;
                // Swift compares complete Character values. A formula-looking base carrying
                // a combining mark is not the literal single-character prefix in this rule.
                formula = character.IsAnyOf("=+-@"); break;
            }
        }
        return "\"" + (formula ? "'" : "") + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
    public static string Preview(ReadOnlySpan<byte> data, VocabularyExportFormat format)
    {
        var body = format == VocabularyExportFormat.Csv && data.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? data[3..] : data;
        int length = Math.Min(body.Length, 16_000);
        // Mac trims until its entire prefix is valid, including malformed bytes before the end.
        // Strict decoding avoids replacement glyphs or splitting a CJK/emoji UTF-8 sequence.
        while (length > 0)
        {
            try
            {
                string text = StrictUtf8.GetString(body[..length]);
                // Foundation String(data:encoding:.utf8) consumes one initial BOM on decode,
                // independently of CSV's explicit BOM removal. The byte budget still includes it.
                if (body[..length].StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) text = text[1..];
                return text + (body.Length > length ? "\n…" : "");
            }
            catch (DecoderFallbackException) { length--; }
        }
        return body.IsEmpty ? "" : "\n…";
    }
}
