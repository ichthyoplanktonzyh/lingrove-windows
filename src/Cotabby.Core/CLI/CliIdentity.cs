using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Text;

namespace Cotabby.Core.CLI;

/// <summary>Deterministic source-derived operation identity. A dry run and the corresponding
/// acknowledged commit use the same batch key, content fingerprint and operation/suffix path.</summary>
public static class CliIdentity
{
    public static Guid OperationId(string batchKey, string fingerprint, int operation, string suffix)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(batchKey + "\0" + fingerprint + "\0" + operation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + suffix));
        hash[6] = (byte)((hash[6] & 15) | 0x50); hash[8] = (byte)((hash[8] & 63) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
    /// <summary>Sorted compact wire bytes; clients attach image bytes before this hash is computed.
    /// The per-record receipt stores the fingerprint, never the input content or credentials.</summary>
    public static string Fingerprint(CliBatch batch)
    {
        using var stream = new MemoryStream();
        var canonical = batch.CopyValue(); NormalizeTypedValues(canonical); RemoveOptionalNulls(canonical, false);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) { Write(writer, canonical); }
        // Foundation's default JSONEncoder escapes '/' even when sorting keys.
        string text = Encoding.UTF8.GetString(stream.ToArray()).Replace("/", "\\/", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    private static void NormalizeTypedValues(JsonObject batch)
    {
        static void Uuid(JsonObject value, string field)
        {
            if (value[field] is JsonValue token && token.TryGetValue<string>(out var text) && Guid.TryParseExact(text, "D", out var id))
                value[field] = id.ToString("D").ToUpperInvariant();
        }
        foreach (var item in batch["operations"]!.AsArray())
        {
            var operation = item!.AsObject();
            if (operation["put"] is JsonObject put && put["value"] is { } resource)
                put["value"] = NormalizeResourceNumbers(resource);
            foreach (string name in new[] { "append", "update" }) if (operation[name] is JsonObject target) { Uuid(target, "entryID"); Uuid(target, "senseID"); }
            if (operation["delete"] is JsonObject deletion) Uuid(deletion, "id");
            if (operation["capture"] is JsonObject capture)
            {
                if (capture["meanings"] is JsonArray meanings) foreach (var value in meanings.OfType<JsonObject>()) Uuid(value, "id");
                if (capture["entries"] is JsonArray entries) foreach (var entry in entries.OfType<JsonObject>())
                    if (entry["senses"] is JsonArray senses) foreach (var sense in senses.OfType<JsonObject>()) Uuid(sense, "id");
            }
            var action = operation["add"] ?? operation["append"];
            if (action?["content"] is JsonArray blocks) foreach (var block in blocks.OfType<JsonObject>())
                if (block["image"] is JsonObject image && image["data"] is JsonValue data && data.TryGetValue<string>(out var text))
                    image["data"] = Convert.ToBase64String(Convert.FromBase64String(text));
        }
    }
    private static JsonNode? NormalizeResourceNumbers(JsonNode? value)
    {
        if (value is JsonObject obj)
        {
            var copy = new JsonObject(); foreach (var pair in obj) copy[pair.Key] = NormalizeResourceNumbers(pair.Value); return copy;
        }
        if (value is JsonArray array) return new JsonArray(array.Select(NormalizeResourceNumbers).ToArray());
        if (value is JsonValue scalar && scalar.TryGetValue<JsonElement>(out var number) && number.ValueKind == JsonValueKind.Number)
        {
            // CLIJSON attempts Int64 before Double. Foundation accepts integral decimal and
            // exponent spellings, then JSONEncoder writes their canonical integer form.
            try { return JsonValue.Create(FoundationJsonInteger.Read(number)); }
            catch (JsonException) { return JsonValue.Create(number.GetDouble()); }
        }
        return value?.DeepClone();
    }
    // Swift's synthesized Codable omits nil optional fields. Resource put values are CLIJSON,
    // where explicit null is meaningful patch data and must remain in the fingerprint.
    private static void RemoveOptionalNulls(JsonNode? node, bool preserveNulls)
    {
        if (node is JsonObject obj)
        {
            foreach (var name in obj.Select(pair => pair.Key).ToArray())
            {
                if (obj[name] is null) { if (!preserveNulls) obj.Remove(name); continue; }
                bool preserve = preserveNulls || name == "value" && obj.ContainsKey("resource");
                RemoveOptionalNulls(obj[name], preserve);
            }
        }
        else if (node is JsonArray array) foreach (var value in array) RemoveOptionalNulls(value, preserveNulls);
    }
    private static void Write(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null: writer.WriteNullValue(); break;
            case JsonObject map:
                writer.WriteStartObject(); foreach (var pair in map.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { writer.WritePropertyName(pair.Key); Write(writer, pair.Value); } writer.WriteEndObject(); break;
            case JsonArray array:
                writer.WriteStartArray(); foreach (var value in array) Write(writer, value); writer.WriteEndArray(); break;
            default: node.WriteTo(writer); break;
        }
    }
}
