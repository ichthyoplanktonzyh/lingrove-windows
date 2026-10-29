using System.Text.Json.Nodes;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.CLI;

/// <summary>Task-shaped capture adapts the 3b planner into the detached raw resource graph.
/// It never invokes a model and does not infer deletion of uncollected evidence.</summary>
internal sealed class CliCapturePlan
{
    private readonly CliResourceDraft _draft;
    private readonly JsonObject _defaults;
    private readonly F.SourceDate _now;
    private readonly Func<string, Guid> _id;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, Guid> _nodes = new(CanonicalStringComparer.Instance);
    private readonly Dictionary<Guid, R.VocabularyMeaningNodeKind> _nodeKinds;
    private readonly Dictionary<Guid, F.VocabularyEntry> _entriesById;
    private readonly Dictionary<string, Guid> _entryKeys;
    private readonly Dictionary<Guid, F.VocabularyCollectionItem> _collections;
    private readonly Dictionary<Guid, Dictionary<string, (Guid Owner, F.VocabularyExample Example)>> _examplesByEntry;
    private readonly Dictionary<string, HashSet<Guid>> _ids;
    private readonly IReadOnlyList<R.VocabularyCategory> _categories;
    private readonly HashSet<(Guid Node, Guid Category)> _memberships;
    private readonly JsonArray _entries = [], _meanings = [];
    private int _senses, _examples, _mappings;
    private CliCapturePlan(CliResourceDraft draft, JsonObject input, F.SourceDate now, Func<string, Guid> id, CancellationToken cancellationToken)
    {
        _draft = draft; _defaults = input["defaults"] as JsonObject ?? new(); _now = now; _id = id; _cancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested(); var document = draft.Working();
        _nodeKinds = document.Semantics.Nodes.ToDictionary(value => value.Id, value => value.Kind);
        _entriesById = document.Catalog.ToDictionary(value => value.Id);
        _entryKeys = document.Catalog.GroupBy(F.VocabularyArchiveValidation.EntryKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);
        _collections = document.Collections.ToDictionary(value => value.SenseId);
        _examplesByEntry = document.Entries.ToDictionary(entry => entry.Id, entry => entry.Senses.SelectMany(sense => sense.Examples.Select(example => (Key: CliExamplePolicy.Key(example), Owner: sense.Id, Example: example)))
            .GroupBy(value => value.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => (group.First().Owner, group.First().Example), StringComparer.Ordinal));
        _categories = document.Classification.Categories;
        _memberships = document.Classification.Memberships.Select(value => (value.NodeId, value.CategoryId)).ToHashSet();
        _ids = new(StringComparer.Ordinal) {
            ["nodes"] = document.Semantics.Nodes.Select(value => value.Id).ToHashSet(), ["patterns"] = document.Semantics.Patterns.Select(value => value.Id).ToHashSet(),
            ["mappings"] = document.Semantics.Mappings.Select(value => value.Id).ToHashSet(), ["relations"] = document.Semantics.Relations.Select(value => value.Id).ToHashSet(),
            ["cues"] = document.Semantics.Cues.Select(value => value.Id).ToHashSet(), ["cueTargets"] = document.Semantics.CueTargets.Select(value => value.Id).ToHashSet(),
            ["categoryMemberships"] = document.Classification.Memberships.Select(value => value.Id).ToHashSet(),
            ["sources"] = document.Sources.Select(value => value.Id).ToHashSet(), ["occurrences"] = document.Occurrences.Select(value => value.Id).ToHashSet(),
            ["interpretations"] = document.Interpretations.Select(value => value.Id).ToHashSet(), ["senses"] = document.Catalog.SelectMany(value => value.Senses).Select(value => value.Id).ToHashSet()
        };
    }
    internal static JsonObject Apply(CliResourceDraft draft, JsonObject input, F.SourceDate now, Func<string, Guid> id, int operation, CancellationToken cancellationToken)
    {
        var plan = new CliCapturePlan(draft, input, now, id, cancellationToken);
        _ = CliExamplePolicy.Language(plan.String(plan._defaults, "language")); _ = CliExamplePolicy.Language(plan.String(plan._defaults, "meaningLanguage"));
        _ = CliExamplePolicy.Language(plan.String(plan._defaults, "translationLanguage")); _ = plan.Provenance(null);
        foreach (var (node, index) in (input["meanings"] as JsonArray ?? []).Select((value, index) => (value, index))) plan.Meaning(node!.AsObject(), $"meaning.{index}");
        foreach (var (node, index) in input["entries"]!.AsArray().Select((value, index) => (value, index))) plan.Entry(node!.AsObject(), $"entry.{index}");
        foreach (var (node, index) in (input["relations"] as JsonArray ?? []).Select((value, index) => (value, index))) plan.Relation(node!.AsObject(), $"relation.{index}");
        return new JsonObject { ["operation"] = operation, ["action"] = "capture", ["entries"] = plan._entries, ["meanings"] = plan._meanings,
            ["summary"] = new JsonObject { ["entryCount"] = plan._entries.Count, ["senseCount"] = plan._senses, ["exampleCount"] = plan._examples, ["meaningCount"] = plan._meanings.Count, ["associationCount"] = plan._mappings } };
    }
    private string? String(JsonObject value, string key) => value[key]?.GetValue<string>();
    private JsonObject Provenance(string? status) => new() { ["origin"] = "model", ["status"] = status ?? String(_defaults, "status") ?? "confirmed", ["source"] = String(_defaults, "source") ?? "CLI capture" };
    private void PutNew(string resource, Guid id, JsonObject value)
    {
        if (!_ids[resource].Add(id))
            throw new CliException(CliErrorCode.Conflict, "Generated resource identity already exists.");
        _draft.Put(resource, value);
    }
    private static string G(Guid id) => id.ToString("D").ToUpperInvariant();
    private void Meaning(JsonObject input, string path)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        string reference = CliExamplePolicy.Text(String(input, "ref")!, 200, true);
        if (_nodes.ContainsKey(reference)) throw Invalid("Meaning refs must be unique within the capture.");
        Guid nodeId;
        if (String(input, "id") is { } existing)
        {
            if (new[] { "title", "kind", "language", "aliases", "definition", "cues", "status" }.Any(key => input[key] is not null)) throw Invalid("An existing meaning accepts ref, id and additive categories only.");
            nodeId = Guid.Parse(existing);
            if (!_nodeKinds.ContainsKey(nodeId)) throw Missing("Referenced meaning does not exist.");
        }
        else
        {
            nodeId = _id(path); string? language = String(input, "language") ?? String(_defaults, "meaningLanguage"); _ = CliExamplePolicy.Language(language);
            var labels = new JsonArray(new JsonObject { ["id"] = G(_id(path + ".label")), ["text"] = CliExamplePolicy.Text(String(input, "title") ?? "", 4096, true), ["language"] = language, ["preferred"] = true });
            var preferred = new HashSet<string>(StringComparer.Ordinal) { language ?? "und" };
            foreach (var (alias, offset) in (input["aliases"] as JsonArray ?? []).Select((value, index) => (value!.AsObject(), index)))
            {
                string? code = String(alias, "language") ?? String(_defaults, "meaningLanguage"); _ = CliExamplePolicy.Language(code);
                labels.Add(new JsonObject { ["id"] = G(_id(path + ".alias." + offset)), ["text"] = String(alias, "text"), ["language"] = code, ["preferred"] = preferred.Add(code ?? "und") });
            }
            var provenance = Provenance(String(input, "status"));
            PutNew("nodes", nodeId, new JsonObject { ["id"] = G(nodeId), ["kind"] = String(input, "kind") ?? "concept", ["labels"] = labels,
                ["definition"] = String(input, "definition") ?? "", ["provenance"] = provenance });
            _nodeKinds.Add(nodeId, String(input, "kind") == "function" ? R.VocabularyMeaningNodeKind.Function : R.VocabularyMeaningNodeKind.Concept);
            foreach (var (cue, offset) in (input["cues"] as JsonArray ?? []).Select((value, index) => (value!.GetValue<string>(), index)))
            {
                Guid cueId = _id(path + ".cue." + offset), targetId = _id(path + ".target." + offset);
                PutNew("cues", cueId, new JsonObject { ["id"] = G(cueId), ["text"] = cue, ["language"] = language });
                PutNew("cueTargets", targetId, new JsonObject { ["id"] = G(targetId), ["cueID"] = G(cueId), ["nodeID"] = G(nodeId), ["provenance"] = provenance.DeepClone() });
            }
        }
        foreach (var (value, offset) in (input["categories"] as JsonArray ?? []).Select((value, index) => (value!.GetValue<string>(), index)))
        {
            var matches = _categories.Where(category => Guid.TryParse(value, out var parsed) && category.Id == parsed || CanonicalStringComparer.Instance.Equals(category.Key, value)).ToArray();
            if (matches.Length != 1 || matches[0].IsRetired) throw Invalid("Category reference must name one active category UUID or catalog key.");
            if (_memberships.Add((nodeId, matches[0].Id)))
            {
                Guid membership = _id(path + ".category." + offset);
                PutNew("categoryMemberships", membership, new JsonObject { ["id"] = G(membership), ["nodeID"] = G(nodeId), ["categoryID"] = G(matches[0].Id), ["provenance"] = Provenance(String(input, "status")) });
            }
        }
        _nodes.Add(reference, nodeId); _meanings.Add(new JsonObject { ["ref"] = reference, ["nodeID"] = G(nodeId), ["created"] = input["id"] is null });
    }
    private void Entry(JsonObject input, string path)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        string expression = CliExamplePolicy.Text(String(input, "expression")!, 32000, true);
        string? languageCode = String(input, "language") ?? String(_defaults, "language"); var language = CliExamplePolicy.Language(languageCode) ?? throw Invalid("Supply entry language or defaults.language and a supported kind.");
        string kindText = String(input, "kind") ?? "word"; if (!Enum.TryParse<VocabularyEntryKind>(kindText, true, out var kind) || !Enum.IsDefined(kind)) throw Invalid("Unknown kind.");
        string dictionary = CliExamplePolicy.Text(String(input, "dictionaryForm") ?? expression, 32000, true);
        var candidate = new F.VocabularyEntry(_id(path), expression, dictionary, language, kind, [], _now, _now);
        string key = F.VocabularyArchiveValidation.EntryKey(candidate);
        F.VocabularyEntry? entry = _entryKeys.TryGetValue(key, out var existingEntry) ? _entriesById[existingEntry] : null; bool created = entry is null;
        if (entry is null)
        {
            if (_entriesById.ContainsKey(candidate.Id)) throw Invalid("Generated entry identity already exists.");
            entry = candidate; _entriesById.Add(entry.Id, entry); _entryKeys.Add(key, entry.Id);
            _examplesByEntry.Add(entry.Id, new Dictionary<string, (Guid, F.VocabularyExample)>(StringComparer.Ordinal));
            _draft.Append("entries", entry.Id, entry);
        }
        var outcomes = new JsonArray();
        foreach (var (value, index) in input["senses"]!.AsArray().Select((value, index) => (value!.AsObject(), index))) outcomes.Add(Sense(value, entry.Id, path + ".sense." + index));
        entry = _entriesById[entry.Id] with { UpdatedAt = _now }; _entriesById[entry.Id] = entry;
        _draft.Upsert("entries", entry.Id, entry);
        _entries.Add(new JsonObject { ["entryID"] = G(entry.Id), ["expression"] = expression, ["language"] = languageCode, ["created"] = created, ["senses"] = outcomes });
    }
    private JsonObject Sense(JsonObject input, Guid entryId, string path)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var entry = _entriesById[entryId]; Guid senseId;
        if (String(input, "id") is { } existing)
        {
            if (input["meaning"] is not null || input["meaningLanguage"] is not null || input["note"] is not null) throw Invalid("Existing senses accept examples, tags and associations; use update for replacements.");
            senseId = Guid.Parse(existing); if (!entry.Senses.Any(value => value.Id == senseId)) throw Missing("Referenced sense does not belong to the selected entry.");
        }
        else
        {
            senseId = _id(path);
            if (!_ids["senses"].Add(senseId)) throw Invalid("Generated sense identity already exists.");
            var sense = new F.VocabularySense(senseId, CliExamplePolicy.Text(String(input, "meaning") ?? "", 32000, true), _now, _now)
            { MeaningLanguage = CliExamplePolicy.Language(String(input, "meaningLanguage") ?? String(_defaults, "meaningLanguage")), MeaningOrigin = VocabularyMeaningOrigin.Ai, CreatedWithoutContext = true };
            var header = entry with { Senses = entry.Senses.Append(sense).ToArray() }; _entriesById[entryId] = header;
            _draft.Upsert("entries", entryId, header);
        }
        var usageIds = Examples(input["examples"] as JsonArray ?? [], entryId, senseId, path);
        var collection = _collections.GetValueOrDefault(senseId) ?? new F.VocabularyCollectionItem(senseId, _now);
        collection = collection with { Note = String(input, "note") ?? collection.Note,
            Tags = CliExamplePolicy.Tags(collection.Tags.Concat((input["tags"] as JsonArray ?? []).Select(value => value!.GetValue<string>()))),
            InterpretationIds = collection.InterpretationIds.Concat(usageIds).Distinct().ToArray() };
        _draft.Upsert("collections", senseId, collection);
        _collections[senseId] = collection;
        var associations = new JsonArray();
        foreach (var (association, index) in (input["associations"] as JsonArray ?? []).Select((value, index) => (value!.AsObject(), index)))
            associations.Add(G(Association(association, senseId, usageIds, path + ".mapping." + index)));
        _senses++; _examples += usageIds.Count;
        return new JsonObject { ["senseID"] = G(senseId), ["created"] = input["id"] is null,
            ["usageIDs"] = new JsonArray(usageIds.Select(value => (JsonNode?)JsonValue.Create(G(value))).ToArray()), ["associationIDs"] = associations };
    }
    private List<Guid> Examples(JsonArray inputs, Guid entryId, Guid senseId, string path)
    {
        var result = new List<Guid>();
        foreach (var (raw, offset) in inputs.Select((value, index) => (value!.AsObject(), index)))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var input = (JsonObject)raw.DeepClone();
            if (input["meaningLanguage"] is null) input["meaningLanguage"] = String(_defaults, "meaningLanguage");
            if (input["translationLanguage"] is null) input["translationLanguage"] = String(_defaults, "translationLanguage") ?? String(_defaults, "meaningLanguage");
            if (input["sourceKind"] is null) input["sourceKind"] = String(_defaults, "sourceKind") ?? "generated";
            var value = CliExamplePolicy.Create(input, _entriesById[entryId].Expression, _id(path + ".example." + offset), _now, "generated");
            var examples = _examplesByEntry[entryId];
            if (examples.TryGetValue(CliExamplePolicy.Key(value), out var prior))
            {
                if (prior.Owner != senseId) throw new CliException(CliErrorCode.Conflict, "This usage belongs to another sense. Append to its owner.");
                bool meaningMatches = value.Meaning.Length == 0 || CanonicalStringComparer.Instance.Equals(value.Meaning, prior.Example.Meaning) && value.MeaningLanguage == prior.Example.MeaningLanguage;
                bool translationMatches = value.Translation.Length == 0 || CanonicalStringComparer.Instance.Equals(value.Translation, prior.Example.Translation) && value.TranslationLanguage == prior.Example.TranslationLanguage;
                if (!meaningMatches || !translationMatches) throw new CliException(CliErrorCode.Conflict, "Existing usage differs. Use append or explicit resource edits.");
                result.Add(prior.Example.Id); continue;
            }
            Guid occurrenceId = _id(path + ".occurrence." + offset);
            if (!_ids["sources"].Add(value.SourceId) || !_ids["occurrences"].Add(occurrenceId) || !_ids["interpretations"].Add(value.Id)) throw Invalid("Generated evidence identity already exists.");
            var source = new F.VocabularySourceSnapshot(value.SourceId, value.Text, value.SourceKind, null, _now);
            var occurrence = new R.VocabularySourceOccurrence(occurrenceId, value.SourceId, value.Ranges);
            var claim = new F.VocabularyInterpretation(value.Id, occurrenceId, _now) { SenseId = senseId, Status = R.VocabularyInterpretationStatus.Accepted,
                Meaning = value.Meaning, MeaningLanguage = value.MeaningLanguage, MeaningOrigin = raw["meaning"] is null ? VocabularyMeaningOrigin.Captured : VocabularyMeaningOrigin.Ai,
                Translation = value.Translation, TranslationLanguage = value.TranslationLanguage, Completeness = value.Completeness,
                Provenance = new R.VocabularySemanticProvenance { Origin = R.VocabularySemanticOrigin.Model,
                    Status = String(_defaults, "status") == "proposed" ? R.VocabularySemanticStatus.Proposed : R.VocabularySemanticStatus.Confirmed,
                    Source = String(_defaults, "source") ?? "CLI capture" } };
            _draft.Append("sources", source.Id, source);
            _draft.Append("occurrences", occurrence.Id, occurrence);
            _draft.Append("interpretations", claim.Id, claim);
            examples.Add(CliExamplePolicy.Key(value), (senseId, value)); result.Add(value.Id);
        }
        return result;
    }
    private Guid Association(JsonObject input, Guid senseId, List<Guid> evidence, string path)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!_nodes.TryGetValue(String(input, "meaning")!, out var nodeId)) throw Invalid("Unknown meaning ref in association.");
        if (!_nodeKinds.TryGetValue(nodeId, out var nodeKind)) throw Invalid("Unknown meaning ref in association.");
        Guid? patternId = null; var provenance = Provenance(String(input, "status"));
        if (String(input, "usage") is { } usage)
        {
            patternId = _id(path + ".pattern");
            PutNew("patterns", patternId.Value, new JsonObject { ["id"] = G(patternId.Value), ["senseID"] = G(senseId), ["template"] = usage,
                ["formality"] = String(input, "formality") ?? "unspecified", ["medium"] = String(input, "medium") ?? "unspecified",
                ["collocations"] = input["collocations"]?.DeepClone() ?? new JsonArray(), ["provenance"] = provenance.DeepClone() });
        }
        else if (input["formality"] is not null || input["medium"] is not null || input["collocations"] is not null) throw Invalid("Usage attributes require usage.");
        Guid mapping = _id(path);
        PutNew("mappings", mapping, new JsonObject { ["id"] = G(mapping), ["nodeID"] = G(nodeId), ["senseID"] = G(senseId),
            ["patternID"] = patternId is null ? null : G(patternId.Value), ["relation"] = nodeKind == R.VocabularyMeaningNodeKind.Concept ? "expressesContent" : "realizesFunction",
            ["conditions"] = String(input, "conditions") ?? "", ["nuance"] = String(input, "nuance") ?? "",
            ["evidenceIDs"] = new JsonArray(evidence.Distinct().OrderBy(G, StringComparer.Ordinal).Select(value => (JsonNode?)JsonValue.Create(G(value))).ToArray()), ["provenance"] = provenance });
        _mappings++; return mapping;
    }
    private void Relation(JsonObject input, string path)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!_nodes.TryGetValue(String(input, "from")!, out var source) || !_nodes.TryGetValue(String(input, "to")!, out var target)) throw Invalid("Unknown meaning ref or relation kind.");
        Guid id = _id(path); PutNew("relations", id, new JsonObject { ["id"] = G(id), ["sourceID"] = G(source), ["targetID"] = G(target),
            ["kind"] = String(input, "kind"), ["note"] = String(input, "note") ?? "", ["provenance"] = Provenance(String(input, "status")) });
    }
    private static CliException Invalid(string message) => new(CliErrorCode.InvalidInput, message);
    private static CliException Missing(string message) => new(CliErrorCode.NotFound, message);
}
