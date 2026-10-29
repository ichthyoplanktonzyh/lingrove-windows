using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cotabby.Core.CLI;

/// <summary>Closed source 3b resource and capture shapes. Omitted patch fields stay omitted;
/// nested replacement values receive their source creation defaults. No persistence occurs here.</summary>
public static class CliShapeContract
{
    public sealed record Field(string Kind, int Limit = 0, IReadOnlyList<string>? Values = null, Shape? Object = null, Field? Item = null);
    public sealed record Shape(IReadOnlyDictionary<string, Field> Fields, IReadOnlyList<string> Required, IReadOnlyDictionary<string, JsonNode?> Defaults)
    {
        public JsonObject Normalize(JsonNode? value, bool creating = true)
        {
            if (value is not JsonObject source || source.Any(p => !Fields.ContainsKey(p.Key)) || creating && Required.Any(k => !source.ContainsKey(k))) throw Invalid("Unknown or missing resource fields. Run 'lingrove schema'.");
            var result = new JsonObject(); if (creating) foreach (var item in Defaults) result[item.Key] = item.Value?.DeepClone();
            foreach (var item in source) result[item.Key] = NormalizeField(Fields[item.Key], item.Value);
            return result;
        }
        public JsonObject Schema() => new() { ["type"] = "object", ["additionalProperties"] = false,
            ["properties"] = new JsonObject(Fields.Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, SchemaField(p.Value)))),
            ["required"] = new JsonArray(Required.Order(StringComparer.Ordinal).Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) };
    }
    private static CliException Invalid(string message) => new(CliErrorCode.InvalidInput, message);
    private static JsonNode? NormalizeField(Field field, JsonNode? value)
    {
        if (field.Kind == "nullable") return value is null ? null : NormalizeField(field.Item!, value);
        if (field.Kind == "object") return field.Object!.Normalize(value);
        if (field.Kind == "array" && value is JsonArray array && array.Count <= field.Limit) return new JsonArray(array.Select(v => NormalizeField(field.Item!, v)).ToArray());
        if (value is JsonValue scalar)
        {
            if (field.Kind == "text" && scalar.TryGetValue<string>(out var text) && Encoding.UTF8.GetByteCount(text) <= field.Limit) return scalar.DeepClone();
            if (field.Kind == "uuid" && scalar.TryGetValue<string>(out var uuid) && Guid.TryParseExact(uuid, "D", out _)) return scalar.DeepClone();
            if (field.Kind == "enum" && scalar.TryGetValue<string>(out var code) && field.Values!.Contains(code, StringComparer.Ordinal)) return scalar.DeepClone();
            if (field.Kind == "bool" && scalar.TryGetValue<bool>(out _)) return scalar.DeepClone();
            // JsonNode parsed by System.Text.Json retains a JsonElement; numeric probes use its
            // token kind so decimal/exponent syntax cannot silently become an integer value.
            if (field.Kind == "integer" && ((scalar.TryGetValue<long>(out var integer) && integer >= 0) || (scalar.TryGetValue<int>(out var small) && small >= 0))) return scalar.DeepClone();
            if (field.Kind is "date" or "confidence" && scalar.TryGetValue<double>(out var numberValue) && double.IsFinite(numberValue) && (field.Kind != "confidence" || numberValue is >= 0 and <= 1)) return scalar.DeepClone();
            if (scalar.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number)
            {
                if (field.Kind == "integer" && element.TryGetInt64(out var i) && i >= 0 && element.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0) return scalar.DeepClone();
                if (field.Kind is "date" or "confidence" && element.TryGetDouble(out var number) && double.IsFinite(number) && (field.Kind != "confidence" || number is >= 0 and <= 1)) return scalar.DeepClone();
            }
        }
        throw Invalid("Invalid resource field type, enum or size. Run 'lingrove schema'.");
    }
    private static JsonObject SchemaField(Field field) => field.Kind switch
    {
        "text" => new() { ["type"] = "string", ["maxLength"] = field.Limit }, "uuid" => new() { ["type"] = "string", ["format"] = "uuid" },
        "date" => new() { ["type"] = "number", ["description"] = "Unix seconds since 1970-01-01 UTC" }, "bool" => new() { ["type"] = "boolean" },
        "integer" => new() { ["type"] = "integer", ["minimum"] = 0 }, "confidence" => new() { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
        "enum" => new() { ["enum"] = new JsonArray(field.Values!.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) },
        "object" => field.Object!.Schema(), "array" => new() { ["type"] = "array", ["maxItems"] = field.Limit, ["items"] = SchemaField(field.Item!) },
        "nullable" => new() { ["anyOf"] = new JsonArray(SchemaField(field.Item!), new JsonObject { ["type"] = "null" }) }, _ => throw new InvalidOperationException()
    };
    private static Field T(int n) => new("text", n); private static readonly Field U = new("uuid"), D = new("date"), B = new("bool"), I = new("integer"), C = new("confidence");
    private static Field E(params string[] v) => new("enum", Values: v); private static Field N(Field v) => new("nullable", Item: v); private static Field A(Field v, int n) => new("array", n, Item: v); private static Field O(Shape v) => new("object", Object: v);
    private static Shape S((string, Field)[] fields, string[]? required = null, IReadOnlyDictionary<string, JsonNode?>? defaults = null) => new(fields.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.Ordinal), required ?? [], defaults ?? new Dictionary<string, JsonNode?>());
    private static readonly Field Provenance = O(S([("origin", E("user", "imported", "model")), ("status", E("confirmed", "proposed")), ("source", T(4096)), ("confidence", N(C))]));
    private static readonly Field Range = O(S([("location", I), ("length", I)], ["location", "length"]));
    private static readonly Field Meaning = O(S([("text", T(262144)), ("language", N(T(100)))], ["text"]));
    private static readonly Field Label = O(S([("id", U), ("text", T(4096)), ("language", N(T(100))), ("preferred", B)], ["id", "text"], new Dictionary<string, JsonNode?> { ["preferred"] = JsonValue.Create(true) }));
    private static readonly Field Role = O(S([("id", U), ("name", T(200)), ("description", T(4096))], ["id", "name"], new Dictionary<string, JsonNode?> { ["description"] = JsonValue.Create("") }));
    private static readonly Field Slot = O(S([("id", U), ("name", T(200)), ("constraint", T(4096))], ["id", "name"], new Dictionary<string, JsonNode?> { ["constraint"] = JsonValue.Create("") }));
    private static readonly Field Binding = O(S([("id", U), ("roleID", U), ("slotID", U)], ["id", "roleID", "slotID"]));
    private static readonly Field CategoryExample = O(S([("text", T(4096)), ("language", N(T(100)))], ["text"]));
    private static readonly Field CategoryLocalization = O(S([("language", T(100)), ("name", N(T(4096))), ("definition", N(T(16384))), ("scopeNote", N(T(16384)))], ["language"]));
    private static readonly Field CategoryReference = O(S([("source", T(16384)), ("identifier", T(16384)), ("url", T(16384)), ("attribution", N(T(16384))), ("license", N(T(16384))), ("relationship", E("adapted", "combined", "narrower"))], ["source", "identifier", "url", "relationship"]));
    public static Shape Resource(string name) => name switch
    {
        "frameworks" => S([("id", U), ("key", T(200)), ("version", I)], ["id", "key", "version"]),
        "categories" => S([("id", U), ("axis", E("content", "function")), ("labels", A(Label,64)), ("definition", T(16384)), ("frameworkID", N(U)), ("examples", A(CategoryExample,64)), ("scopeNote", T(16384)), ("references", A(CategoryReference,64)), ("localizations", A(CategoryLocalization,64)), ("key", N(T(200))), ("isRetired", B), ("provenance", Provenance)], ["id", "axis", "labels"]),
        "categoryParents" => S([("id", U), ("categoryID", U), ("parentID", U)], ["id", "categoryID", "parentID"]),
        "categoryMemberships" => S([("id", U), ("nodeID", U), ("categoryID", U), ("provenance", Provenance)], ["id", "nodeID", "categoryID"]),
        "categoryOverrides" => S([("categoryID", U), ("labels", N(A(Label,64))), ("definition", N(T(16384))), ("examples", N(A(CategoryExample,64))), ("scopeNote", N(T(16384))), ("references", N(A(CategoryReference,64))), ("localizations", N(A(CategoryLocalization,64))), ("isHidden", B)], ["categoryID"]),
        "entries" => S([("id", U), ("expression", T(32000)), ("dictionaryForm", T(32000)), ("language", N(T(100))), ("kind", E("word", "expression", "pattern", "sentence")), ("createdAt", D), ("updatedAt", D)], ["id", "expression", "kind"]),
        "senses" => S([("id", U), ("entryID", U), ("meaning", T(32000)), ("meaningLanguage", N(T(100))), ("meaningOrigin", E("captured", "ai", "user")), ("alternateMeanings", A(Meaning,64)), ("mergedFrom", A(U,128)), ("createdWithoutContext", N(B)), ("createdAt", D), ("updatedAt", D)], ["id", "entryID", "meaning"]),
        "sources" => S([("id", U), ("text", T(262144)), ("kind", E("unspecified", "captured", "authored", "generated", "imported")), ("appBundleIdentifier", N(T(1024))), ("encounteredAt", D)], ["id", "text", "kind"]),
        "occurrences" => S([("id", U), ("sourceID", U), ("ranges", A(Range,64))], ["id", "sourceID", "ranges"]),
        "interpretations" => S([("id", U), ("occurrenceID", U), ("senseID", N(U)), ("status", E("candidate", "accepted", "rejected")), ("meaning", T(32000)), ("meaningLanguage", N(T(100))), ("meaningOrigin", E("captured", "ai", "user")), ("translation", T(262144)), ("translationLanguage", N(T(100))), ("translationVariants", A(Meaning,64)), ("completeness", E("sentence", "fragment", "unknown")), ("provenance", Provenance), ("createdAt", D)], ["id", "occurrenceID"]),
        "collections" => S([("senseID", U), ("interpretationIDs", A(U,10000)), ("isSavedWithoutContext", B), ("note", T(262144)), ("tags", A(T(200),100)), ("savedAt", D)], ["senseID"]),
        "nodes" => S([("id", U), ("kind", E("concept", "function")), ("labels", A(Label,64)), ("definition", T(16384)), ("scopeNote", T(16384)), ("roles", A(Role,32)), ("provenance", Provenance), ("createdAt", D), ("updatedAt", D)], ["id", "labels"]),
        "patterns" => S([("id", U), ("senseID", U), ("template", T(8192)), ("slots", A(Slot,32)), ("formality", E("unspecified", "informal", "neutral", "formal")), ("medium", E("unspecified", "spoken", "written", "both")), ("region", T(200)), ("note", T(8192)), ("collocations", A(T(1024),128)), ("provenance", Provenance)], ["id", "senseID", "template"]),
        "mappings" => S([("id", U), ("nodeID", U), ("senseID", U), ("patternID", N(U)), ("relation", E("expressesContent", "realizesFunction")), ("conditions", T(8192)), ("nuance", T(8192)), ("roleBindings", A(Binding,32)), ("evidenceIDs", A(U,128)), ("provenance", Provenance)], ["id", "nodeID", "senseID"]),
        "relations" => S([("id", U), ("sourceID", U), ("targetID", U), ("kind", E("narrower", "opposite", "related")), ("note", T(8192)), ("provenance", Provenance)], ["id", "sourceID", "targetID"]),
        "cues" => S([("id", U), ("text", T(4096)), ("language", N(T(100))), ("kind", E("paraphrase", "simpleExpression", "personal"))], ["id", "text"]),
        "cueTargets" => S([("id", U), ("cueID", U), ("nodeID", U), ("conditions", T(8192)), ("provenance", Provenance)], ["id", "cueID", "nodeID"]),
        _ => throw Invalid("Unknown resource. Use: " + string.Join(", ", CliArguments.Resources) + ".")
    };
    private static readonly Field Status = E("confirmed", "proposed"), SourceKind = E("unspecified", "captured", "authored", "generated", "imported");
    private static readonly Field CaptureLabel = O(S([("text", T(4096)), ("language", T(100))], ["text"]));
    private static readonly Field CaptureDefaults = O(S([("language", T(100)), ("meaningLanguage", T(100)), ("translationLanguage", T(100)), ("sourceKind", SourceKind), ("status", Status), ("source", T(4096))]));
    private static readonly Field CaptureMeaning = O(S([("ref", T(200)), ("id", U), ("title", T(4096)), ("language", T(100)), ("kind", E("concept", "function")), ("aliases", A(CaptureLabel,63)), ("definition", T(16384)), ("cues", A(T(4096),64)), ("status", Status), ("categories", A(T(200),64))], ["ref"]));
    private static readonly Field CaptureExample = O(S([("text", T(262144)), ("translation", T(262144)), ("translationLanguage", T(100)), ("meaning", T(32000)), ("meaningLanguage", T(100)), ("completeness", E("sentence", "fragment", "unknown")), ("sourceKind", SourceKind), ("ranges", A(Range,64))], ["text"]));
    private static readonly Field CaptureAssociation = O(S([("meaning", T(200)), ("usage", T(8192)), ("nuance", T(8192)), ("conditions", T(8192)), ("formality", E("unspecified", "informal", "neutral", "formal")), ("medium", E("unspecified", "spoken", "written", "both")), ("collocations", A(T(1024),128)), ("status", Status)], ["meaning"]));
    private static readonly Field CaptureSense = O(S([("id", U), ("meaning", T(32000)), ("meaningLanguage", T(100)), ("note", T(262144)), ("tags", A(T(200),100)), ("examples", A(CaptureExample,100)), ("associations", A(CaptureAssociation,64))]));
    private static readonly Field CaptureEntry = O(S([("expression", T(32000)), ("language", T(100)), ("kind", E("word", "expression", "pattern", "sentence")), ("dictionaryForm", T(32000)), ("senses", A(CaptureSense,100))], ["expression", "senses"]));
    private static readonly Field CaptureRelation = O(S([("from", T(200)), ("to", T(200)), ("kind", E("narrower", "opposite", "related")), ("note", T(8192)), ("status", Status)], ["from", "to", "kind"]));
    public static Shape Capture { get; } = S([("defaults", CaptureDefaults), ("meanings", A(CaptureMeaning,100)), ("entries", A(CaptureEntry,100)), ("relations", A(CaptureRelation,200))], ["entries"]);
}
