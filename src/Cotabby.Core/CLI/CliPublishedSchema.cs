using System.Text.Json.Nodes;

namespace Cotabby.Core.CLI;

/// <summary>Discoverable 3b task/batch schemas, composed from the same shape definitions as input admission.</summary>
public static class CliPublishedSchema
{
    public static JsonObject Capture()
    {
        var result = CliShapeContract.Capture.Schema(); var fields = result["properties"]!.AsObject();
        fields["version"] = new JsonObject { ["const"] = 1 };
        fields["idempotencyKey"] = new JsonObject { ["type"] = "string", ["maxLength"] = 200 };
        fields["expectedRevision"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 };
        result["required"] = new JsonArray("entries", "idempotencyKey", "version");
        result["title"] = "Lingrove capture task v1"; result["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        return result;
    }
    public static JsonObject Batch()
    {
        var result = JsonNode.Parse(CliLegacySchema.Json)!.AsObject(); result["title"] = "Lingrove vocabulary and resource batch";
        var properties = result["properties"]!.AsObject(); properties["version"] = new JsonObject { ["enum"] = new JsonArray(1, 2) };
        properties["expectedRevision"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 };
        var alternatives = properties["operations"]!["items"]!["oneOf"]!.AsArray();
        alternatives.Add(new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("capture"),
            ["properties"] = new JsonObject { ["capture"] = CliShapeContract.Capture.Schema() } });
        result["x-readOnlyResources"] = new JsonArray("frameworks");
        foreach (var resource in CliArguments.Resources.Where(value => value != "frameworks"))
        {
            var shape = CliShapeContract.Resource(resource).Schema(); shape["x-requiredOnCreate"] = shape["required"]!.DeepClone();
            shape["required"] = new JsonArray(resource == "collections" ? "senseID" : resource == "categoryOverrides" ? "categoryID" : "id");
            alternatives.Add(new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("put"),
                ["properties"] = new JsonObject { ["put"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false,
                    ["required"] = new JsonArray("resource", "value"), ["properties"] = new JsonObject { ["resource"] = new JsonObject { ["const"] = resource }, ["value"] = shape } } } });
        }
        alternatives.Add(new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("delete"),
            ["properties"] = new JsonObject { ["delete"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false,
                ["required"] = new JsonArray("resource", "id"), ["properties"] = new JsonObject {
                    ["resource"] = new JsonObject { ["enum"] = new JsonArray(CliArguments.Resources.Where(value => value != "frameworks").Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()) },
                    ["id"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" } } } } });
        result["allOf"] = JsonNode.Parse("""[{"if":{"properties":{"operations":{"contains":{"anyOf":[{"required":["put"]},{"required":["delete"]}]}}}},"then":{"required":["expectedRevision"],"properties":{"version":{"const":2}}}},{"if":{"properties":{"operations":{"contains":{"required":["capture"]}}}},"then":{"properties":{"version":{"const":2}}}}]""");
        var example = result["$defs"]!["example"]!["properties"]!.AsObject();
        example["completeness"] = new JsonObject { ["enum"] = new JsonArray("sentence", "fragment", "unknown") };
        example["sourceKind"] = new JsonObject { ["enum"] = new JsonArray("unspecified", "captured", "authored", "generated", "imported") };
        return result;
    }
}
