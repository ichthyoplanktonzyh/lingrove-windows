using System.Text.Json;
using Cotabby.Core.Review;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Bounded flat Mac v7/v8 document wire boundary. The internal v7 resource value is
/// not the wire version and is never sent to the older entry-owned database or backup codec.</summary>
public static class VocabularyFrozenDocumentCodec
{
    // Frozen archive backup and restore use this 192 MiB transport envelope. A standalone
    // document is bounded here too, before parsing or publishing it to a future owner.
    public const int MaximumDocumentBytes = 192 * 1024 * 1024;
    private sealed record VersionProbe(long SchemaVersion);
    private sealed record Wire7(long SchemaVersion, IReadOnlyList<VocabularyEntry> Catalog,
        IReadOnlyList<VocabularySourceSnapshot> Sources, IReadOnlyList<VocabularySourceOccurrence> Occurrences,
        IReadOnlyList<VocabularyInterpretation> Interpretations, IReadOnlyList<VocabularyCollectionItem> Collections,
        IReadOnlyList<ReviewLayoutTemplate>? ReviewTemplates, VocabularySemanticIndex Semantics);
    private sealed record Wire8(long SchemaVersion, IReadOnlyList<VocabularyEntry> Catalog,
        IReadOnlyList<VocabularySourceSnapshot> Sources, IReadOnlyList<VocabularySourceOccurrence> Occurrences,
        IReadOnlyList<VocabularyInterpretation> Interpretations, IReadOnlyList<VocabularyCollectionItem> Collections,
        IReadOnlyList<ReviewLayoutTemplate>? ReviewTemplates, VocabularySemanticIndex Semantics,
        VocabularyClassificationIndex Classification);

    public static VocabularyFrozenDocument Decode(ReadOnlyMemory<byte> input)
    {
        if (input.Length > MaximumDocumentBytes) throw new VocabularyException(VocabularyFailure.OversizedArchive);
        try
        {
            var bytes = FoundationJsonInput.Utf8Input(input);
            if (bytes.Length > MaximumDocumentBytes) throw new VocabularyException(VocabularyFailure.OversizedArchive);
            var version = VocabularyPayloadCodec.DecodePortableValue<VersionProbe>(bytes, strictDates: true).SchemaVersion;
            if (version is not (7 or 8)) throw new VocabularyException(VocabularyFailure.UnsupportedVersion);
            CheckCollectionBounds(bytes, version);
            VocabularyFrozenDocument document;
            if (version == 7)
            {
                var wire = VocabularyPayloadCodec.DecodePortableValue<Wire7>(bytes, strictDates: true);
                document = Create(wire.Catalog, wire.Sources, wire.Occurrences, wire.Interpretations, wire.Collections, wire.ReviewTemplates, wire.Semantics, new());
            }
            else
            {
                var wire = VocabularyPayloadCodec.DecodePortableValue<Wire8>(bytes, strictDates: true);
                document = Create(wire.Catalog, wire.Sources, wire.Occurrences, wire.Interpretations, wire.Collections, wire.ReviewTemplates, wire.Semantics, wire.Classification);
            }
            VocabularyFrozenArchiveValidation.Validate(document);
            return document;
        }
        catch (System.Text.DecoderFallbackException) { throw new VocabularyException(VocabularyFailure.InvalidArchive); }
        catch (JsonException) { throw new VocabularyException(VocabularyFailure.InvalidArchive); }
    }
    public static byte[] Encode(VocabularyFrozenDocument document)
    {
        VocabularyFrozenArchiveValidation.Validate(document);
        var r = document.Resources;
        return VocabularyPayloadCodec.EncodePortableValue(new Wire8(8, r.Catalog, r.Sources, r.Occurrences,
            r.Interpretations, r.Collections, r.ReviewTemplates, r.Semantics, document.Classification), MaximumDocumentBytes);
    }
    // These limits are the actual lifecycle, semantic and classification budgets. Check the
    // source-shaped arrays before constructing CLR graphs. Other arrays remain bounded by bytes.
    private static void CheckCollectionBounds(ReadOnlyMemory<byte> bytes, long version)
    {
        using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 512 });
        var root = parsed.RootElement;
        static JsonElement? Field(JsonElement value, string name)
        {
            if (value.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in value.EnumerateObject()) if (CanonicalStringComparer.Instance.Equals(property.Name, name)) return property.Value;
            return null;
        }
        static JsonElement? Array(JsonElement value, string name, int limit, VocabularyFailure failure = VocabularyFailure.InvalidArchive)
        {
            var item = Field(value, name);
            if (item is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > limit) throw new VocabularyException(failure);
            return item;
        }
        static void Each(JsonElement? array, Action<JsonElement> action)
        {
            if (array is { ValueKind: JsonValueKind.Array } a) foreach (var item in a.EnumerateArray()) action(item);
        }
        static void Content(JsonElement owner, string name) => _ = Array(owner, name, 64, VocabularyFailure.InvalidContent);
        static void Layout(JsonElement? candidate)
        {
            if (candidate is not { ValueKind: JsonValueKind.Object } layout) return;
            var front = Array(layout, "front", 64, VocabularyFailure.InvalidContent);
            var back = Array(layout, "back", 64, VocabularyFailure.InvalidContent);
            int count = (front is { ValueKind: JsonValueKind.Array } f ? f.GetArrayLength() : 0)
                + (back is { ValueKind: JsonValueKind.Array } b ? b.GetArrayLength() : 0);
            if (count > 64) throw new VocabularyException(VocabularyFailure.InvalidContent);
            int blocks = 0;
            void Part(JsonElement part)
            {
                var content = Array(part, "content", 64, VocabularyFailure.InvalidContent);
                if (content is { ValueKind: JsonValueKind.Array } a) blocks += a.GetArrayLength();
            }
            Each(front, Part); Each(back, Part);
            if (blocks > 64) throw new VocabularyException(VocabularyFailure.InvalidContent);
        }
        static void Plan(JsonElement plan)
        {
            Layout(Field(plan, "layout"));
            if (Field(plan, "fieldOverrides") is { ValueKind: JsonValueKind.Object } fields && fields.EnumerateObject().Select(x => x.Name).Distinct(CanonicalStringComparer.Instance).Count() > 32)
                throw new VocabularyException(VocabularyFailure.InvalidArchive);
        }
        static void Record(JsonElement record)
        {
            if (Field(record, "input") is { } input) _ = Array(input, "ranges", 64);
        }
        static void Binding(JsonElement binding)
        {
            if (Field(binding, "fields") is { ValueKind: JsonValueKind.Object } fields
                && fields.EnumerateObject().Select(x => x.Name).Distinct(CanonicalStringComparer.Instance).Count() > 32)
                throw new VocabularyException(VocabularyFailure.InvalidArchive);
        }
        static void Review(JsonElement? candidate)
        {
            if (candidate is not { ValueKind: JsonValueKind.Object } review) return;
            Each(Array(review, "bindings", 64), Binding);
            if (Field(review, "plan") is { } plan) Plan(plan);
            Each(Array(review, "preservedPlans", 64), Plan);
        }
        static void Owner(JsonElement owner)
        {
            Content(owner, "content"); Review(Field(owner, "review"));
            Each(Array(owner, "reviewCards", 64), review => Review(review));
        }
        Each(Array(root, "reviewTemplates", 64), template => Layout(Field(template, "layout")));
        Each(Array(root, "occurrences", int.MaxValue), x => _ = Array(x, "ranges", 64));
        Each(Array(root, "interpretations", int.MaxValue), x => { _ = Array(x, "translationVariants", 64); Each(Array(x, "explanations", 24), Record); });
        Each(Array(root, "collections", int.MaxValue), x => { _ = Array(x, "interpretationIDs", 10_000); _ = Array(x, "tags", 100); Owner(x); });
        Each(Array(root, "catalog", int.MaxValue), entry => Each(Array(entry, "senses", int.MaxValue), sense =>
        {
            _ = Array(sense, "examples", 0); Each(Array(sense, "explanations", 128), Record);
        }));
        if (Field(root, "semantics") is { } semantics)
        {
            Each(Array(semantics, "nodes", 100_000, VocabularyFailure.InvalidContent), node => { _ = Array(node, "labels", 128, VocabularyFailure.InvalidContent); _ = Array(node, "roles", 64, VocabularyFailure.InvalidContent); });
            Each(Array(semantics, "patterns", 500_000, VocabularyFailure.InvalidContent), pattern => { _ = Array(pattern, "slots", 64, VocabularyFailure.InvalidContent); _ = Array(pattern, "collocations", 128, VocabularyFailure.InvalidContent); });
            Each(Array(semantics, "mappings", 500_000, VocabularyFailure.InvalidContent), mapping => _ = Array(mapping, "roleBindings", 64, VocabularyFailure.InvalidContent));
            _ = Array(semantics, "relations", 500_000, VocabularyFailure.InvalidContent); _ = Array(semantics, "cues", 500_000, VocabularyFailure.InvalidContent); _ = Array(semantics, "cueTargets", 500_000, VocabularyFailure.InvalidContent);
        }
        if (version == 8 && Field(root, "classification") is { } classification)
        {
            _ = Array(classification, "frameworks", 64, VocabularyFailure.InvalidContent);
            Each(Array(classification, "categories", 20_000, VocabularyFailure.InvalidContent), category => _ = Array(category, "labels", 64, VocabularyFailure.InvalidContent));
            _ = Array(classification, "parents", 100_000, VocabularyFailure.InvalidContent); _ = Array(classification, "memberships", 500_000, VocabularyFailure.InvalidContent);
            Each(Array(classification, "overrides", 20_000, VocabularyFailure.InvalidContent), overlay => _ = Array(overlay, "labels", 64, VocabularyFailure.InvalidContent));
        }
    }
    private static VocabularyFrozenDocument Create(IReadOnlyList<VocabularyEntry> catalog, IReadOnlyList<VocabularySourceSnapshot> sources,
        IReadOnlyList<VocabularySourceOccurrence> occurrences, IReadOnlyList<VocabularyInterpretation> interpretations,
        IReadOnlyList<VocabularyCollectionItem> collections, IReadOnlyList<ReviewLayoutTemplate>? reviewTemplates,
        VocabularySemanticIndex semantics, VocabularyClassificationIndex classification) =>
        new(new VocabularyDocument { Catalog = catalog, Sources = sources, Occurrences = occurrences, Interpretations = interpretations,
            Collections = collections, ReviewTemplates = reviewTemplates, Semantics = semantics }, classification);
}
