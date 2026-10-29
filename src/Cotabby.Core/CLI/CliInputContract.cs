using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Text;

namespace Cotabby.Core.CLI;

/// <summary>A detached wire batch. File-path resolution and actual image admission belong to the
/// client; the Store admits only bytes, never an arbitrary client-provided filesystem path.</summary>
public sealed class CliBatch
{
    private readonly byte[] _json;
    internal CliBatch(JsonObject value) => _json = JsonSerializer.SerializeToUtf8Bytes(value);
    public JsonObject CopyValue() => JsonNode.Parse(_json)!.AsObject();
}

public static class CliInputContract
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    public const int MaximumImageBytes = 16 * 1024 * 1024;
    public static CliBatch DecodeBatch(ReadOnlyMemory<byte> bytes) => Validate(Parse(bytes), clientInput: true);
    public static CliBatch DecodeCapture(ReadOnlyMemory<byte> bytes)
    {
        var value = Parse(bytes); Only(value, ["version", "idempotencyKey", "expectedRevision", "defaults", "meanings", "entries", "relations"]);
        if (Integer(value["version"]) != 1 || Text(value["idempotencyKey"]) is not { } key) throw Invalid("Capture needs version 1 and idempotencyKey. Run 'lingrove schema capture'.");
        var capture = (JsonObject)value.DeepClone(); capture.Remove("version"); capture.Remove("idempotencyKey"); var revision = capture["expectedRevision"]?.DeepClone(); capture.Remove("expectedRevision");
        var material = CliShapeContract.Capture.Normalize(capture); ValidateCapture(material);
        var batch = new JsonObject { ["version"] = 2, ["idempotencyKey"] = key, ["operations"] = new JsonArray(new JsonObject { ["capture"] = material }) };
        if (revision is not null) batch["expectedRevision"] = revision;
        return Validate(batch, clientInput: true);
    }
    /// <summary>Used again by the app after the client has replaced permitted image paths with bytes.</summary>
    public static CliBatch ValidateWire(JsonObject value) => Validate((JsonObject)value.DeepClone(), clientInput: false);
    private static JsonObject Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) throw Invalid("Input exceeds 32 MiB.");
        try { return JsonNode.Parse(bytes.Span, documentOptions: new JsonDocumentOptions { MaxDepth = 128 }) as JsonObject ?? throw Invalid("Invalid batch JSON; run 'lingrove schema' for the input format."); }
        catch (JsonException) { throw Invalid("Invalid batch JSON; run 'lingrove schema' for the input format."); }
    }
    private static CliBatch Validate(JsonObject batch, bool clientInput)
    {
        Only(batch, ["version", "idempotencyKey", "expectedRevision", "operations"]);
        long? version = Integer(batch["version"]); string? key = Text(batch["idempotencyKey"]);
        if (version is not (1 or 2) || key is null || Encoding.UTF8.GetByteCount(key) is < 1 or > 200 || FoundationText.TrimWhitespaceAndNewlines(key).Length == 0
            || batch["operations"] is not JsonArray ops || ops.Count is < 1 or > 100) throw Invalid("Invalid batch header or operation count.");
        if (batch["expectedRevision"] is not null && Integer(batch["expectedRevision"]) is not >= 0) throw Invalid("Invalid expectedRevision.");
        int images = 0, blocks = 0, examples = 0, imageBytes = 0;
        foreach (var opNode in ops)
        {
            if (opNode is not JsonObject op || op.Count != 1 || op.First().Value is not JsonObject action) throw Invalid("Each operation needs exactly one action.");
            string kind = op.First().Key;
            switch (kind)
            {
                case "capture":
                    if (version != 2) throw Invalid("Capture operations require batch version 2.");
                    var capture = CliShapeContract.Capture.Normalize(action); examples += ValidateCapture(capture); op[kind] = capture; break;
                case "put":
                    if (version != 2 || batch["expectedRevision"] is null) throw Invalid("Resource operations require version 2 and expectedRevision.");
                    Only(action, ["resource", "value"]); string resource = Text(action["resource"]) ?? throw Invalid("Invalid resource.");
                    _ = CliShapeContract.Resource(resource).Normalize(action["value"], creating: false); break;
                case "delete":
                    if (version != 2 || batch["expectedRevision"] is null) throw Invalid("Resource operations require version 2 and expectedRevision.");
                    Only(action, ["resource", "id"]); _ = CliShapeContract.Resource(Text(action["resource"]) ?? ""); _ = Id(action["id"]); break;
                case "add": case "append": case "update":
                    string[] names = kind switch { "add" => ["expression", "dictionaryForm", "language", "kind", "meaning", "meaningLanguage", "note", "tags", "examples", "content"], "append" => ["entryID", "senseID", "examples", "content", "tags"], _ => ["entryID", "senseID", "expectedRevision", "meaning", "meaningLanguage", "note", "tags"] };
                    Only(action, names); ValidateTraditional(action, kind);
                    var contents = OptionalArray(action, "content", 64); var usages = OptionalArray(action, "examples", 100); blocks += contents.Count; examples += usages.Count;
                    foreach (var usageNode in usages) ValidateExample(usageNode);
                    foreach (var blockNode in contents)
                    {
                        if (blockNode is not JsonObject block) throw Invalid("Invalid content block."); Only(block, ["text", "image", "bold", "italic"]);
                        bool text = block["text"] is not null, image = block["image"] is not null; if (text == image) throw Invalid("Each block needs text or an image.");
                        if (text) { _ = Text(block["text"]) ?? throw Invalid("Invalid text block."); OptionalBool(block, "bold"); OptionalBool(block, "italic"); }
                        else
                        {
                            images++; if (block["image"] is not JsonObject media) throw Invalid("Invalid image block."); Only(media, clientInput ? ["path", "caption"] : ["path", "data", "caption"]);
                            if (block["bold"] is not null || block["italic"] is not null) throw Invalid("Invalid image block.");
                            string? path = Text(media["path"]), encoded = Text(media["data"]); if ((path is null) == (encoded is null)) throw Invalid("Invalid image block.");
                            if (encoded is not null) { try { imageBytes = checked(imageBytes + Convert.FromBase64String(encoded).Length); } catch (Exception e) when (e is FormatException or OverflowException) { throw Invalid("Invalid image bytes."); } }
                            if (media["caption"] is not null && Text(media["caption"]) is null) throw Invalid("Invalid image caption.");
                        }
                    }
                    break;
                default: throw Invalid("Each operation needs exactly one action.");
            }
        }
        if (images > 64 || blocks > 512 || examples > 1000 || imageBytes > MaximumImageBytes) throw Invalid("Batch exceeds 64 images, 16 MiB image bytes, 512 blocks or 1000 examples.");
        return new(batch);
    }
    private static int ValidateCapture(JsonObject capture)
    {
        var entries = (JsonArray)capture["entries"]!; int senses = 0, examples = 0;
        if (entries.Count == 0) throw Invalid("Capture requires entries and senses, at most 100 senses and 1000 examples.");
        foreach (var entry in entries) { var values = (JsonArray)entry!["senses"]!; if (values.Count == 0) throw Invalid("Capture requires entries and senses, at most 100 senses and 1000 examples."); senses += values.Count; foreach (var sense in values) examples += (sense!["examples"] as JsonArray)?.Count ?? 0; }
        if (senses > 100 || examples > 1000) throw Invalid("Capture requires entries and senses, at most 100 senses and 1000 examples."); return examples;
    }
    private static void ValidateTraditional(JsonObject action, string kind)
    {
        if (kind == "add" && (Text(action["expression"]) is null || Text(action["language"]) is null || Text(action["kind"]) is null || Text(action["meaning"]) is null)) throw Invalid("Invalid add operation.");
        if (kind is "append" or "update") { _ = Id(action["entryID"]); _ = Id(action["senseID"]); }
        if (kind == "update" && Integer(action["expectedRevision"]) is not >= 0) throw Invalid("Invalid expectedRevision.");
        foreach (string name in new[] { "dictionaryForm", "meaning", "meaningLanguage", "note", "language", "kind", "expression" }) if (action[name] is not null && Text(action[name]) is null) throw Invalid("Invalid field.");
        foreach (var tag in OptionalArray(action, "tags", int.MaxValue)) if (Text(tag) is null) throw Invalid("Invalid tags.");
    }
    private static void ValidateExample(JsonNode? value)
    {
        if (value is not JsonObject item) throw Invalid("Invalid example."); Only(item, ["text", "translation", "translationLanguage", "meaning", "meaningLanguage", "completeness", "sourceKind", "ranges"]);
        if (Text(item["text"]) is null) throw Invalid("Invalid example.");
        foreach (string key in new[] { "translation", "translationLanguage", "meaning", "meaningLanguage", "completeness", "sourceKind" }) if (item[key] is not null && Text(item[key]) is null) throw Invalid("Invalid example.");
        if (Text(item["sourceKind"]) is { } source && source is not ("unspecified" or "captured" or "authored" or "generated" or "imported")) throw Invalid("Unknown example sourceKind.");
        foreach (var range in OptionalArray(item, "ranges", 64)) { if (range is not JsonObject pair) throw Invalid("Invalid range."); Only(pair, ["location", "length"]); if (Integer(pair["location"]) is null || Integer(pair["length"]) is null) throw Invalid("Invalid range."); }
    }
    private static void Only(JsonObject value, IReadOnlyCollection<string> names) { if (value.Any(p => !names.Contains(p.Key))) throw Invalid("Unknown fields or invalid object."); }
    private static JsonArray OptionalArray(JsonObject value, string name, int limit) { if (!value.ContainsKey(name)) return []; if (value[name] is not JsonArray array || array.Count > limit) throw Invalid("Invalid array or count."); return array; }
    private static void OptionalBool(JsonObject value, string name) { if (value[name] is not null && (value[name] is not JsonValue v || !v.TryGetValue<bool>(out _))) throw Invalid("Invalid boolean."); }
    private static string? Text(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;
    private static long? Integer(JsonNode? value) { if (value is not JsonValue scalar) return null; if (scalar.TryGetValue<long>(out var n)) return n; if (scalar.TryGetValue<int>(out var i)) return i; return null; }
    private static Guid Id(JsonNode? value) => Guid.TryParseExact(Text(value), "D", out var result) ? result : throw Invalid("Invalid UUID.");
    private static CliException Invalid(string message) => new(CliErrorCode.InvalidInput, message);
}
