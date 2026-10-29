using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.CLI;

/// <summary>Read-only resource projections borrow one canonical snapshot. Only fields from the
/// 3b CLI contract leave the process; collection/review state remains independent of catalog.</summary>
public static class CliResourceProjection
{
    private const double UnixOffset = 978307200;
    public static IReadOnlyList<JsonObject> Records(F.VocabularyDocument document, string resource)
    {
        var shape = CliShapeContract.Resource(resource);
        JsonObject Convert<T>(T value)
        {
            var encoded = JsonNode.Parse(VocabularyPayloadCodec.EncodePortableValue(value))!.AsObject(); var result = new JsonObject();
            foreach (var (key, field) in shape.Fields)
            {
                if (!encoded.TryGetPropertyValue(key, out var v)) continue;
                if (field.Kind == "date" && v is not null) result[key] = JsonValue.Create(v.GetValue<double>() + UnixOffset);
                else result[key] = v?.DeepClone();
            }
            return result;
        }
        return resource switch
        {
            "frameworks" => document.Classification.Frameworks.Select(Convert).ToArray(),
            "categories" => document.Classification.Categories.Select(Convert).ToArray(),
            "categoryParents" => document.Classification.Parents.Select(Convert).ToArray(),
            "categoryMemberships" => document.Classification.Memberships.Select(Convert).ToArray(),
            "categoryOverrides" => document.Classification.Overrides.Select(Convert).ToArray(),
            "entries" => document.Catalog.Select(Convert).ToArray(),
            "senses" => document.Catalog.SelectMany(entry => entry.Senses.Select(sense => { var value = Convert(sense); value["entryID"] = entry.Id.ToString("D").ToUpperInvariant(); return value; })).ToArray(),
            "sources" => document.Sources.Select(Convert).ToArray(),
            "occurrences" => document.Occurrences.Select(Convert).ToArray(),
            "interpretations" => document.Interpretations.Select(Convert).ToArray(),
            "collections" => document.Collections.Select(Convert).ToArray(),
            "nodes" => document.Semantics.Nodes.Select(Convert).ToArray(),
            "patterns" => document.Semantics.Patterns.Select(Convert).ToArray(),
            "mappings" => document.Semantics.Mappings.Select(Convert).ToArray(),
            "relations" => document.Semantics.Relations.Select(Convert).ToArray(),
            "cues" => document.Semantics.Cues.Select(Convert).ToArray(),
            "cueTargets" => document.Semantics.CueTargets.Select(Convert).ToArray(),
            _ => throw new CliException(CliErrorCode.InvalidInput, "Unknown resource.")
        };
    }
    public static Guid Identifier(JsonObject value, string resource)
    {
        string field = resource == "collections" ? "senseID" : resource == "categoryOverrides" ? "categoryID" : "id";
        return value[field] is JsonValue scalar && scalar.TryGetValue<string>(out string? text) && Guid.TryParseExact(text, "D", out var id)
            ? id : throw new CliException(CliErrorCode.InvalidInput, "Resource writes require an explicit UUID.");
    }
    public static JsonObject Get(F.VocabularyDocument document, ulong revision, string resource, Guid id)
    {
        var value = Records(document, resource).FirstOrDefault(record => Identifier(record, resource) == id) ?? throw new CliException(CliErrorCode.NotFound, "Resource not found.");
        return new() { ["revision"] = revision, ["resource"] = resource, ["record"] = value };
    }
    public static JsonObject List(F.VocabularyDocument document, ulong revision, string resource, IVocabularyTextSearching search,
        string? query = null, Guid? related = null, string? status = null, int limit = 50, int offset = 0)
    {
        if (limit is < 1 or > 200 || offset < 0 || System.Text.Encoding.UTF8.GetByteCount(query ?? "") > 4096
            || status is not null && (resource != "interpretations" || status is not ("candidate" or "accepted" or "rejected")))
            throw new CliException(CliErrorCode.InvalidInput, "Use limit 1–200, nonnegative offset, and an interpretation status when applicable.");
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var values = Records(document, resource).Where(value => (related is null || Any(value, text => Guid.TryParseExact(text, "D", out var id) && id == related))
            && (status is null || value["status"]?.GetValue<string>() == status) && terms.All(term => Any(value, text => search.Contains(text, term)))).ToArray();
        var page = values.Skip(Math.Min(offset, values.Length)).Take(limit).ToArray();
        var result = new JsonObject { ["revision"] = revision, ["resource"] = resource, ["total"] = values.Length, ["offset"] = offset,
            ["records"] = new JsonArray(page.Select(p => (JsonNode?)p).ToArray()), ["nextOffset"] = offset < values.Length && page.Length < values.Length - offset ? JsonValue.Create(offset + page.Length) : null };
        // Foundation JSONEncoder emits Unicode literally but escapes forward slashes. Account
        // for those extra bytes instead of using the web encoder's much larger Unicode escapes.
        string serialized = result.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if ((long)System.Text.Encoding.UTF8.GetByteCount(serialized) + serialized.Count(c => c == '/') > 16 * 1024 * 1024) throw new CliException(CliErrorCode.InvalidInput, "Resource page exceeds 16 MiB. Request a smaller limit.");
        return result;
    }
    private static bool Any(JsonNode? node, Func<string, bool> predicate) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => predicate(text),
        JsonObject obj => obj.Any(pair => Any(pair.Value, predicate)), JsonArray array => array.Any(value => Any(value, predicate)), _ => false
    };
}
