using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.CLI;

/// <summary>Batch-local typed lookup state. Mutations synchronize only the changed row with the
/// detached raw graph; intermediate references may be unresolved until final whole-graph admission.</summary>
internal sealed class CliTypedGraph
{
    private sealed class Rows<T>(IReadOnlyList<T> values, Func<T, Guid> key)
    {
        private readonly List<T> _values = values.ToList();
        private Dictionary<Guid, int> _index = values.Select((value, index) => (key(value), index)).ToDictionary(value => value.Item1, value => value.index);
        public IReadOnlyList<T> Values => _values;
        public void Put(T value)
        {
            Guid id = key(value);
            if (_index.TryGetValue(id, out int index)) _values[index] = value;
            else { _index.Add(id, _values.Count); _values.Add(value); }
        }
        public void Remove(Guid id)
        {
            if (!_index.TryGetValue(id, out int index)) return;
            _values.RemoveAt(index); _index = _values.Select((value, offset) => (key(value), offset)).ToDictionary(value => value.Item1, value => value.offset);
        }
        public void Retain(HashSet<Guid> ids)
        {
            _values.RemoveAll(value => !ids.Contains(key(value)));
            _index = _values.Select((value, offset) => (key(value), offset)).ToDictionary(value => value.Item1, value => value.offset);
        }
    }
    private readonly F.VocabularyDocument _baseline;
    private readonly Rows<F.VocabularyEntry> _entries;
    private readonly Rows<F.VocabularySourceSnapshot> _sources;
    private readonly Rows<R.VocabularySourceOccurrence> _occurrences;
    private readonly Rows<F.VocabularyInterpretation> _interpretations;
    private readonly Rows<F.VocabularyCollectionItem> _collections;
    private readonly Rows<F.VocabularyMeaningNode> _nodes;
    private readonly Rows<R.VocabularyUsagePattern> _patterns;
    private readonly Rows<R.VocabularyMeaningMapping> _mappings;
    private readonly Rows<R.VocabularyMeaningRelation> _relations;
    private readonly Rows<R.VocabularyRetrievalCue> _cues;
    private readonly Rows<R.VocabularyCueTarget> _targets;
    private readonly Rows<R.VocabularyCategory> _categories;
    private readonly Rows<R.VocabularyCategoryParent> _parents;
    private readonly Rows<R.VocabularyMeaningCategoryMembership> _memberships;
    private readonly Rows<R.VocabularyCategoryOverride> _overrides;
    public CliTypedGraph(F.VocabularyDocument document)
    {
        _baseline = document;
        _entries = new(document.Catalog, value => value.Id); _sources = new(document.Sources, value => value.Id);
        _occurrences = new(document.Occurrences, value => value.Id); _interpretations = new(document.Interpretations, value => value.Id);
        _collections = new(document.Collections, value => value.SenseId); _nodes = new(document.Semantics.Nodes, value => value.Id);
        _patterns = new(document.Semantics.Patterns, value => value.Id); _mappings = new(document.Semantics.Mappings, value => value.Id);
        _relations = new(document.Semantics.Relations, value => value.Id); _cues = new(document.Semantics.Cues, value => value.Id);
        _targets = new(document.Semantics.CueTargets, value => value.Id); _categories = new(document.Classification.Categories, value => value.Id);
        _parents = new(document.Classification.Parents, value => value.Id); _memberships = new(document.Classification.Memberships, value => value.Id);
        _overrides = new(document.Classification.Overrides, value => value.CategoryId);
    }
    public F.VocabularyDocument Snapshot() => _baseline with { Catalog = _entries.Values, Sources = _sources.Values, Occurrences = _occurrences.Values,
        Interpretations = _interpretations.Values, Collections = _collections.Values,
        Semantics = _baseline.Semantics with { Nodes = _nodes.Values, Patterns = _patterns.Values, Mappings = _mappings.Values,
            Relations = _relations.Values, Cues = _cues.Values, CueTargets = _targets.Values },
        Classification = _baseline.Classification with { Categories = _categories.Values, Parents = _parents.Values, Memberships = _memberships.Values, Overrides = _overrides.Values } };
    public void Put(string resource, object value)
    {
        switch (resource)
        {
            case "entries": _entries.Put((F.VocabularyEntry)value); break;
            case "sources": _sources.Put((F.VocabularySourceSnapshot)value); break;
            case "occurrences": _occurrences.Put((R.VocabularySourceOccurrence)value); break;
            case "interpretations": _interpretations.Put((F.VocabularyInterpretation)value); break;
            case "collections": _collections.Put((F.VocabularyCollectionItem)value); break;
            default: throw new InvalidOperationException("Unsupported typed CLI update.");
        }
    }
    public void Sync(string resource, Guid id, JsonObject? raw)
    {
        static T Decode<T>(JsonObject node)
        {
            try { return VocabularyPayloadCodec.DecodePortableValue<T>(JsonSerializer.SerializeToUtf8Bytes(node)); }
            catch (VocabularyException) { throw new CliException(CliErrorCode.InvalidInput, "Resource fields do not match the typed record. Check language codes and required fields."); }
        }
        void Set<T>(Rows<T> rows) { if (raw is null) rows.Remove(id); else rows.Put(Decode<T>(raw)); }
        switch (resource)
        {
            case "entries": Set(_entries); break; case "sources": Set(_sources); break; case "occurrences": Set(_occurrences); break;
            case "interpretations": Set(_interpretations); break; case "collections": Set(_collections); break;
            case "nodes": Set(_nodes); break; case "patterns": Set(_patterns); break; case "mappings": Set(_mappings); break;
            case "relations": Set(_relations); break; case "cues": Set(_cues); break; case "cueTargets": Set(_targets); break;
            case "categories": Set(_categories); break; case "categoryParents": Set(_parents); break;
            case "categoryMemberships": Set(_memberships); break; case "categoryOverrides": Set(_overrides); break;
            default: throw new InvalidOperationException("Unsupported typed CLI resource.");
        }
    }
    public void RetainCategoryRelations(JsonArray parents, JsonArray memberships, JsonArray overrides)
    {
        static HashSet<Guid> Keys(JsonArray values, string field) => values.OfType<JsonObject>().Select(value => Guid.Parse(value[field]!.GetValue<string>())).ToHashSet();
        _parents.Retain(Keys(parents, "id")); _memberships.Retain(Keys(memberships, "id")); _overrides.Retain(Keys(overrides, "categoryID"));
    }
}
