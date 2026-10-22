using System.Text;
using System.Text.Json;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Decodes the pinned Mac JSON shape without IO. Required fields retain their types; optional
/// attribution/phonetics/examples stay optional, and unknown fields are ignored like JSONDecoder.
/// The source bounds bytes before allocation; this second guard protects direct decoder callers.
/// </summary>
public static class LearningLexiconResourceDecoder
{
    public static LearningLexiconResource Decode(ReadOnlyMemory<byte> data)
    {
        if (data.Length > LearningLexiconResource.MaximumBytes) throw Unreadable();
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            return new LearningLexiconResource(Text(root, "version"), Text(root, "attribution"),
                Text(root, "licenseURL"), root.GetProperty("entries").EnumerateArray().Select(Entry).ToArray());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException)
        { throw Unreadable(); }
    }

    private static LearningLexiconEntry Entry(JsonElement value) => new(
        Text(value, "id"), Text(value, "language"), Text(value, "headword"), Strings(value, "forms"),
        value.GetProperty("senses").EnumerateArray().Select(sense => new LearningLexiconSense(
            Text(sense, "partOfSpeech"), Strings(sense, "definitions"), Strings(sense, "labels"))).ToArray(),
        Text(value, "sourceURL"), OptionalStrings(value, "phonetics"), OptionalStrings(value, "examples"),
        OptionalText(value, "sourceName"), OptionalText(value, "licenseURL"), OptionalText(value, "licenseName"));

    private static string Text(JsonElement value, string name) => String(value.GetProperty(name));
    private static string String(JsonElement value)
    {
        var text = value.GetString() ?? throw new JsonException();
        // Swift String has no lone-surrogate representation. Reject invalid .NET strings rather
        // than allowing a later UTF-8 conversion to replace them and corrupt dictionary keys.
        _ = StrictUtf8.GetByteCount(text);
        return text;
    }
    private static string[] Strings(JsonElement value, string name) =>
        value.GetProperty(name).EnumerateArray().Select(String).ToArray();
    private static string? OptionalText(JsonElement value, string name) =>
        !value.TryGetProperty(name, out var item) || item.ValueKind == JsonValueKind.Null ? null : String(item);
    private static string[]? OptionalStrings(JsonElement value, string name) =>
        !value.TryGetProperty(name, out var item) || item.ValueKind == JsonValueKind.Null
            ? null : item.EnumerateArray().Select(String).ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static LearningLexiconException Unreadable() => new(LearningLexiconFailure.UnreadableResource);
}
