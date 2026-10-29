using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>
/// Mac's dedicated portable JSON export shape. Explicit field maps keep storage/review codec
/// conventions out of this wire format. One render owns its reduced map and bounded UTF-8 output;
/// sorted ASCII keys, optional omission, ISO dates and Foundation pretty layout remain explicit.
/// The package policy's 64 MiB ceiling is enforced during output instead of after a whole JSON
/// allocation. This performs no file/media reads and has no in-render cancellation checkpoint.
/// </summary>
public static partial class FrozenVocabularyExportFormatter
{
    public static byte[] RenderJson(FrozenVocabularyExportDocument document)
    {
        using var output = new BoundedWriter();
        Pretty(Map(("format", document.Format), ("version", document.Version), ("entries", document.Entries.Select(JsonEntry)),
            ("semantics", document.Semantics is null ? null : Portable(document.Semantics)),
            ("classification", document.Classification is null ? null : Portable(document.Classification))), 0, output);
        return output.Bytes();
    }
    private static object JsonEntry(FrozenVocabularyExportDocument.Entry e) => Map(("id", e.Id.ToString("D").ToUpperInvariant()),
        ("expression", e.Expression), ("dictionaryForm", e.DictionaryForm), ("language", e.Language?.Code()),
        ("kind", e.Kind.ToString().ToLowerInvariant()), ("senses", e.Senses.Select(JsonSense)));
    private static object JsonSense(FrozenVocabularyExportDocument.Sense s) => Map(("id", s.Id.ToString("D").ToUpperInvariant()),
        ("createdAt", IsoDate(s.CreatedAt)), ("meaning", s.Meaning), ("meaningLanguage", s.MeaningLanguage?.Code()),
        ("otherMeanings", s.OtherMeanings?.Select(JsonMeaning)), ("note", s.Note), ("tags", s.Tags),
        ("examples", s.Examples?.Select(JsonUsage)), ("explanations", s.Explanations?.Select(JsonExplanation)), ("content", s.Content?.Select(JsonBlock)));
    private static object JsonUsage(FrozenVocabularyExportDocument.Usage u) => Map(("id", u.Id.ToString("D").ToUpperInvariant()),
        ("createdAt", IsoDate(u.CreatedAt)), ("text", u.Text), ("completeness", u.Completeness.ToString().ToLowerInvariant()),
        ("ranges", u.Ranges.Select(r => Map(("location", r.Location), ("length", r.Length)))), ("translation", u.Translation),
        ("translationLanguage", u.TranslationLanguage?.Code()), ("otherTranslations", u.OtherTranslations.Select(JsonMeaning)),
        ("meaning", u.Meaning), ("meaningLanguage", u.MeaningLanguage?.Code()), ("explanations", u.Explanations?.Select(JsonExplanation)));
    private static object JsonMeaning(VocabularyMeaning m) => Map(("text", m.Text), ("language", m.Language?.Code()));
    private static object JsonExplanation(FrozenVocabularyExportDocument.Explanation e) => Map(("language", e.Language.Code()), ("meaning", e.Meaning), ("detail", e.Detail));
    private static object JsonBlock(FrozenVocabularyExportDocument.Block b) => Map(("id", b.Id.ToString("D").ToUpperInvariant()),
        ("text", b.Text is { } t ? Map(("text", t.Text), ("style", t.Style.ToString().ToLowerInvariant()), ("bold", t.Bold), ("italic", t.Italic)) : null),
        ("image", b.Image is { } i ? Map(("digest", i.Digest), ("width", i.Width), ("height", i.Height), ("byteCount", i.ByteCount)) : null),
        ("filename", b.Filename), ("caption", b.Caption));
    // The shared Codable leaf codec supplies the source enum/union/UUID shapes. Export's
    // JSONEncoder uses ISO8601 dates instead of archive seconds; convert only date fields.
    private static object Portable<T>(T value)
    {
        using var json = JsonDocument.Parse(VocabularyPayloadCodec.EncodePortableValue(value, VocabularyExportPolicy.MaximumDataBytes));
        return Convert(json.RootElement, null)!;
    }
    private static object? Convert(JsonElement value, string? key) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(x => x.Name, x => Convert(x.Value, x.Name)),
        JsonValueKind.Array => value.EnumerateArray().Select(x => Convert(x, null)).ToArray(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when key is "createdAt" or "updatedAt" => IsoDate(new SourceDate(value.GetDouble())),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null,
        _ => throw new InvalidOperationException("Unknown portable JSON value.")
    };
    private static Dictionary<string, object?> Map(params (string Key, object? Value)[] fields) => fields.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value);
    private static void Pretty(object? value, int depth, BoundedWriter output)
    {
        string indent = new(' ', depth * 2), childIndent = indent + "  ";
        if (value is null) output.Append("null");
        else if (value is IReadOnlyDictionary<string, object?> map)
        {
            output.Append("{\n"); var fields = map.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
            if (fields.Length == 0) output.Append("\n");
            for (int index = 0; index < fields.Length; index++)
            {
                output.Append(childIndent + Quoted(fields[index].Key) + " : "); Pretty(fields[index].Value, depth + 1, output);
                output.Append(index + 1 == fields.Length ? "\n" : ",\n");
            }
            output.Append(indent + "}");
        }
        else if (value is string text) output.Append(Quoted(text));
        else if (value is bool flag) output.Append(flag ? "true" : "false");
        else if (value is int or long or double) output.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
        else if (value is IEnumerable sequence)
        {
            output.Append("[\n"); var items = sequence.Cast<object?>().ToArray();
            if (items.Length == 0) output.Append("\n");
            for (int index = 0; index < items.Length; index++)
            {
                output.Append(childIndent); Pretty(items[index], depth + 1, output);
                output.Append(index + 1 == items.Length ? "\n" : ",\n");
            }
            output.Append(indent + "]");
        }
        else throw new InvalidOperationException("Unknown export JSON value.");
    }
    private static string Quoted(string text)
    {
        var value = new StringBuilder(); value.Append('"');
        foreach (char character in text)
        {
            // JSON requires these escapes. Unlike a web/HTML encoder, the pinned exporter keeps
            // literal Unicode, '<', '&' and forward slashes; UTF-8 validation happens on append.
            switch (character)
            {
                case '"': value.Append("\\\""); break;
                case '\\': value.Append("\\\\"); break;
                case '\b': value.Append("\\b"); break;
                case '\f': value.Append("\\f"); break;
                case '\n': value.Append("\\n"); break;
                case '\r': value.Append("\\r"); break;
                case '\t': value.Append("\\t"); break;
                default:
                    if (character < ' ') value.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else value.Append(character);
                    break;
            }
        }
        return value.Append('"').ToString();
    }
}
