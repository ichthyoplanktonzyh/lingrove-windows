using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) { Write(writer, batch.CopyValue()); }
        // Foundation's default JSONEncoder escapes '/' even when sorting keys.
        string text = Encoding.UTF8.GetString(stream.ToArray()).Replace("/", "\\/", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
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
