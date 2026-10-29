using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.CLI;

/// <summary>One uncommitted resource batch over a detached v8 wire graph. Intermediate operations
/// may reference later operations; only Finish reconstructs and validates the complete graph.</summary>
public sealed class CliResourceDraft
{
    private readonly JsonObject _graph;
    private readonly F.SourceDate _now;
    private const double UnixOffset = 978307200;
    public CliResourceDraft(F.VocabularyDocument document, F.SourceDate now)
    { _graph = JsonNode.Parse(F.VocabularyDocumentCodec.Encode(document))!.AsObject(); _now = now; }
    private sealed record WorkingWire(long SchemaVersion, IReadOnlyList<F.VocabularyEntry> Catalog,
        IReadOnlyList<F.VocabularySourceSnapshot> Sources, IReadOnlyList<R.VocabularySourceOccurrence> Occurrences,
        IReadOnlyList<F.VocabularyInterpretation> Interpretations, IReadOnlyList<F.VocabularyCollectionItem> Collections,
        IReadOnlyList<Cotabby.Core.Review.ReviewLayoutTemplate>? ReviewTemplates, F.VocabularySemanticIndex Semantics,
        R.VocabularyClassificationIndex Classification);
    // Intermediate resource operations can temporarily have unresolved references. This is
    // a query view only: graph admission remains deferred until Finish, and callers must update
    // raw entities explicitly instead of replacing a projected graph and losing evidence.
    internal F.VocabularyDocument Working()
    {
        var wire = VocabularyPayloadCodec.DecodePortableValue<WorkingWire>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_graph));
        return new F.VocabularyDocument { SchemaVersion = wire.SchemaVersion, Catalog = wire.Catalog, Sources = wire.Sources,
            Occurrences = wire.Occurrences, Interpretations = wire.Interpretations, Collections = wire.Collections,
            ReviewTemplates = wire.ReviewTemplates, Semantics = wire.Semantics, Classification = wire.Classification };
    }
    internal JsonObject RawGraph => _graph;
    internal static JsonObject RawValue<T>(T value) => JsonNode.Parse(VocabularyPayloadCodec.EncodePortableValue(value))!.AsObject();
    internal static void UpsertRaw(JsonArray values, string field, Guid id, JsonObject value)
    {
        for (int i = 0; i < values.Count; i++)
            if (Guid.TryParse(values[i]?[field]?.GetValue<string>(), out var candidate) && candidate == id) { values[i] = value; return; }
        values.Add(value);
    }
    private static CliException Invalid(string message) => new(CliErrorCode.InvalidInput, message);
    private JsonArray ArrayFor(string resource) => resource switch
    {
        "entries" => _graph["catalog"]!.AsArray(), "sources" or "occurrences" or "interpretations" or "collections" => _graph[resource]!.AsArray(),
        "nodes" or "patterns" or "mappings" or "relations" or "cues" or "cueTargets" => _graph["semantics"]![resource]!.AsArray(),
        "frameworks" or "categories" => _graph["classification"]![resource]!.AsArray(),
        "categoryParents" => _graph["classification"]!["parents"]!.AsArray(), "categoryMemberships" => _graph["classification"]!["memberships"]!.AsArray(),
        "categoryOverrides" => _graph["classification"]!["overrides"]!.AsArray(), _ => throw Invalid("Unknown resource.")
    };
    private static string Key(string resource) => resource == "collections" ? "senseID" : resource == "categoryOverrides" ? "categoryID" : "id";
    private static Guid Identifier(JsonObject value, string resource) => CliResourceProjection.Identifier(value, resource);
    private static JsonObject? Find(JsonArray values, string resource, Guid id) => values.OfType<JsonObject>().FirstOrDefault(x => Identifier(x, resource) == id);
    private (JsonObject? Entry, JsonObject? Sense) FindSense(Guid id)
    {
        foreach (var entry in ArrayFor("entries").OfType<JsonObject>()) if (Find(entry["senses"]!.AsArray(), "senses", id) is { } sense) return (entry, sense);
        return (null, null);
    }
    private bool IsCustomCategory(Guid id) => Find(ArrayFor("categories"), "categories", id) is { } category && category["frameworkID"] is null;
    public Guid Put(string resource, JsonObject submitted)
    {
        Guid id = Identifier(submitted, resource); var shape = CliShapeContract.Resource(resource);
        _ = shape.Normalize(submitted, creating: false);
        if (resource == "frameworks") throw Invalid("Frameworks are read-only catalog receipts.");
        if (resource == "categories")
        {
            if (Find(ArrayFor(resource), resource, id) is { } original && original["frameworkID"] is not null) throw Invalid("Built-in categories are read-only. Write categoryOverrides instead.");
            if (submitted["frameworkID"] is not null || submitted["key"] is not null || submitted["isRetired"]?.GetValue<bool>() == true) throw Invalid("Custom categories cannot claim catalog ownership.");
        }
        if (resource == "categoryParents")
        {
            var old = Find(ArrayFor(resource), resource, id);
            if (old is not null && !IsCustomCategory(Guid.Parse(old["categoryID"]!.GetValue<string>()))) throw Invalid("Built-in category parents are read-only.");
            var child = submitted["categoryID"]?.GetValue<string>() ?? old?["categoryID"]?.GetValue<string>();
            if (!Guid.TryParseExact(child, "D", out var category) || !IsCustomCategory(category)) throw Invalid("Only custom categories accept parent edits.");
        }
        JsonObject? oldValue; JsonArray? destination = null; JsonObject? oldParent = null;
        if (resource == "senses") { (oldParent, oldValue) = FindSense(id); }
        else { destination = ArrayFor(resource); oldValue = Find(destination, resource, id); }
        var patch = shape.Normalize(submitted, creating: oldValue is null);
        JsonObject value = oldValue is null ? Fallback(resource, id) : (JsonObject)oldValue.DeepClone();
        foreach (var (key, replacement) in patch)
        {
            if (key == "entryID" && resource == "senses") continue;
            if (shape.Fields[key].Kind == "date" && replacement is not null)
            {
                double supplied = replacement.GetValue<double>();
                if (value[key] is not null && value[key]!.GetValue<double>() + UnixOffset == supplied) continue;
                value[key] = supplied - UnixOffset; continue;
            }
            if (replacement is JsonObject changes && value[key] is JsonObject prior)
            { var merged = (JsonObject)prior.DeepClone(); foreach (var (name, child) in changes) merged[name] = child?.DeepClone(); value[key] = merged; }
            else value[key] = replacement?.DeepClone();
        }
        if (resource == "entries" && string.IsNullOrEmpty(value["dictionaryForm"]?.GetValue<string>())) value["dictionaryForm"] = value["expression"]?.DeepClone();
        if (resource == "sources" && oldValue is not null && !JsonNode.DeepEquals(PublicFields(oldValue, shape), PublicFields(value, shape)))
            throw new CliException(CliErrorCode.Conflict, "Source snapshots are immutable. Create a new source UUID for a correction.");
        if (resource == "occurrences" && oldValue is not null && !JsonNode.DeepEquals(oldValue, value))
            throw new CliException(CliErrorCode.Conflict, "Occurrences are immutable. Create a new occurrence UUID for a correction.");
        if (resource == "senses")
        {
            string? requested = submitted["entryID"]?.GetValue<string>() ?? oldParent?["id"]?.GetValue<string>();
            if (!Guid.TryParseExact(requested, "D", out var parentId) || Find(ArrayFor("entries"), "entries", parentId) is not { } parent) throw new CliException(CliErrorCode.NotFound, "Create the parent entry before writing its sense.");
            if (ReferenceEquals(parent, oldParent)) { int index = parent["senses"]!.AsArray().IndexOf(oldValue); parent["senses"]!.AsArray()[index] = value; }
            else { if (oldParent is not null) oldParent["senses"]!.AsArray().Remove(oldValue); parent["senses"]!.AsArray().Add(value); }
        }
        else if (oldValue is null) destination!.Add(value); else destination![destination.IndexOf(oldValue)] = value;
        return id;
    }
    private static JsonObject PublicFields(JsonObject source, CliShapeContract.Shape shape)
    {
        var result = new JsonObject(); foreach (var (key, field) in shape.Fields) if (source.TryGetPropertyValue(key, out var value) && value is not null)
            result[key] = field.Kind == "date" && value is not null ? JsonValue.Create(value.GetValue<double>() + UnixOffset) : value?.DeepClone(); return result;
    }
    public void Delete(string resource, Guid id)
    {
        if (resource == "senses") { var (parent, sense) = FindSense(id); if (sense is null) throw Missing(); parent!["senses"]!.AsArray().Remove(sense); return; }
        var values = ArrayFor(resource); var old = Find(values, resource, id) ?? throw Missing();
        if (resource == "frameworks") throw Invalid("Frameworks are read-only catalog receipts.");
        if (resource == "entries" && old["senses"]!.AsArray().Count != 0) throw Invalid("Delete or move the entry's senses explicitly first.");
        if (resource == "categories")
        {
            if (!IsCustomCategory(id)) throw Invalid("Built-in categories are read-only.");
            RemoveMatching(ArrayFor("categoryParents"), x => Match(x, "categoryID", id) || Match(x, "parentID", id));
            RemoveMatching(ArrayFor("categoryMemberships"), x => Match(x, "categoryID", id)); RemoveMatching(ArrayFor("categoryOverrides"), x => Match(x, "categoryID", id));
        }
        if (resource == "categoryParents" && !IsCustomCategory(Guid.Parse(old["categoryID"]!.GetValue<string>()))) throw Invalid("Built-in category parents are read-only.");
        values.Remove(old);
    }
    private static void RemoveMatching(JsonArray values, Func<JsonObject, bool> predicate) { for (int i = values.Count - 1; i >= 0; i--) if (values[i] is JsonObject value && predicate(value)) values.RemoveAt(i); }
    private static bool Match(JsonObject value, string field, Guid id) => Guid.TryParse(value[field]?.GetValue<string>(), out var actual) && actual == id;
    private static CliException Missing() => new(CliErrorCode.NotFound, "Resource not found.");
    public F.VocabularyDocument Finish()
    {
        try { return F.VocabularyDocumentCodec.Decode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_graph, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })); }
        catch (VocabularyException) { throw Invalid("Invalid resource relationships or content. Check references, accepted claims and graph constraints."); }
    }
    private JsonObject Fallback(string resource, Guid id)
    {
        var adopted = new R.VocabularySemanticProvenance { Origin = R.VocabularySemanticOrigin.Model, Status = R.VocabularySemanticStatus.Confirmed };
        JsonObject Encode<T>(T value) => JsonNode.Parse(VocabularyPayloadCodec.EncodePortableValue(value))!.AsObject();
        return resource switch
        {
            "categories" => Encode(new R.VocabularyCategory(id, R.VocabularyCategoryAxis.Content, []) { Provenance = adopted }),
            "categoryParents" => Encode(new R.VocabularyCategoryParent(id, id, id)),
            "categoryMemberships" => Encode(new R.VocabularyMeaningCategoryMembership(id, id, id) { Provenance = adopted }),
            "categoryOverrides" => Encode(new R.VocabularyCategoryOverride(id)),
            "entries" => Encode(new F.VocabularyEntry(id, "", "", null, VocabularyEntryKind.Word, [], _now, _now)),
            "senses" => Encode(new F.VocabularySense(id, "", _now, _now) { MeaningOrigin = VocabularyMeaningOrigin.Ai, CreatedWithoutContext = true }),
            "sources" => Encode(new F.VocabularySourceSnapshot(id, "", R.VocabularySourceKind.Imported, null, _now)),
            "occurrences" => Encode(new R.VocabularySourceOccurrence(id, id, [])),
            "interpretations" => Encode(new F.VocabularyInterpretation(id, id, _now) { MeaningOrigin = VocabularyMeaningOrigin.Ai, Provenance = adopted }),
            "collections" => Encode(new F.VocabularyCollectionItem(id, _now)),
            "nodes" => Encode(new F.VocabularyMeaningNode(id, []) { Provenance = adopted, CreatedAt = _now, UpdatedAt = _now }),
            "patterns" => Encode(new R.VocabularyUsagePattern(id, "") { Id = id, Provenance = adopted }),
            "mappings" => Encode(new R.VocabularyMeaningMapping(id, id) { Id = id, Provenance = adopted }),
            "relations" => Encode(new R.VocabularyMeaningRelation(id, id) { Id = id, Provenance = adopted }),
            "cues" => Encode(new R.VocabularyRetrievalCue("") { Id = id }),
            "cueTargets" => Encode(new R.VocabularyCueTarget(id, id) { Id = id, Provenance = adopted }),
            _ => throw Invalid("Unknown resource.")
        };
    }
}
