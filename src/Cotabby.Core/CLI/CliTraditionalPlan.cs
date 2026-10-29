using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.CLI;

/// <summary>Edits only targeted raw graph records; unrelated uncollected and temporarily
/// unresolved evidence is retained until the enclosing batch's final graph validation.</summary>
internal static class CliTraditionalPlan
{
    internal static JsonObject Apply(CliResourceDraft draft, string kind, JsonObject input, int index, string key, string fingerprint,
        ulong revision, F.SourceDate now, IReadOnlyDictionary<string, VocabularyPreparedImage> prepared, CancellationToken cancellationToken)
    {
        Guid Id(string suffix) => CliIdentity.OperationId(key, fingerprint, index, suffix);
        var graph = draft.RawGraph; var document = draft.Working(); var entries = document.Entries;
        F.VocabularyEntry entry; F.VocabularySense sense; bool createdEntry = false;
        if (kind == "add")
        {
            string expression = CliExamplePolicy.Text(input["expression"]!.GetValue<string>(), 32000, true);
            string dictionary = CliExamplePolicy.Text(input["dictionaryForm"]?.GetValue<string>() ?? expression, 32000, true);
            var language = CliExamplePolicy.Language(input["language"]!.GetValue<string>()) ?? throw Invalid();
            if (!Enum.TryParse<VocabularyEntryKind>(input["kind"]!.GetValue<string>(), true, out var entryKind) || !Enum.IsDefined(entryKind)) throw Invalid();
            var candidate = new F.VocabularyEntry(Id("entry"), expression, dictionary, language, entryKind, [], now, now);
            entry = entries.FirstOrDefault(value => F.VocabularyArchiveValidation.EntryKey(value) == F.VocabularyArchiveValidation.EntryKey(candidate)) ?? candidate;
            createdEntry = entry == candidate;
            if (createdEntry && entries.Any(value => value.Id == entry.Id) || entries.SelectMany(value => value.Senses).Any(value => value.Id == Id("sense")))
                throw new CliException(CliErrorCode.Conflict, "Generated entry or sense identity already exists.");
            sense = new F.VocabularySense(Id("sense"), CliExamplePolicy.Text(input["meaning"]!.GetValue<string>(), 32000, true), now, now)
            { MeaningLanguage = CliExamplePolicy.Language(input["meaningLanguage"]?.GetValue<string>()), MeaningOrigin = VocabularyMeaningOrigin.User };
        }
        else
        {
            Guid entryId = Guid.Parse(input["entryID"]!.GetValue<string>()), senseId = Guid.Parse(input["senseID"]!.GetValue<string>());
            entry = entries.FirstOrDefault(value => value.Id == entryId) ?? throw Missing();
            sense = entry.Senses.FirstOrDefault(value => value.Id == senseId) ?? throw Missing();
        }
        var header = document.Catalog.FirstOrDefault(value => value.Id == entry.Id) ?? entry;
        var headers = header.Senses.ToList(); int senseOffset = headers.FindIndex(value => value.Id == sense.Id);
        var headerSense = senseOffset < 0 ? sense : headers[senseOffset];
        var collection = document.Collections.FirstOrDefault(value => value.SenseId == sense.Id) ?? new F.VocabularyCollectionItem(sense.Id, now);
        if (kind == "add")
        {
            collection = collection with { Note = CliExamplePolicy.Text(input["note"]?.GetValue<string>() ?? "", 262144), Tags = CliExamplePolicy.Tags(Strings(input["tags"])) };
        }
        else if (kind == "update")
        {
            if (input["expectedRevision"]!.GetValue<ulong>() != revision) throw new CliException(CliErrorCode.Conflict, "Notebook revision changed. Read the entry again.");
            if (input["meaning"] is null && input["meaningLanguage"] is null && input["note"] is null && input["tags"] is null) throw Invalid();
            if (input["meaning"] is { } meaning) headerSense = headerSense with { Meaning = CliExamplePolicy.Text(meaning.GetValue<string>(), 32000, true), MeaningOrigin = VocabularyMeaningOrigin.User };
            if (input["meaningLanguage"] is { } language) headerSense = headerSense with { MeaningLanguage = CliExamplePolicy.Language(language.GetValue<string>()), MeaningOrigin = VocabularyMeaningOrigin.User };
            if (input["note"] is { } note) collection = collection with { Note = CliExamplePolicy.Text(note.GetValue<string>(), 262144) };
            if (input["tags"] is not null) collection = collection with { Tags = CliExamplePolicy.Tags(Strings(input["tags"])) };
        }
        else if (kind == "append")
        {
            if ((input["examples"] as JsonArray)?.Count is not > 0 && (input["content"] as JsonArray)?.Count is not > 0 && (input["tags"] as JsonArray)?.Count is not > 0) throw Invalid();
            var merged = collection.Tags.ToList();
            foreach (var tag in CliExamplePolicy.Tags(Strings(input["tags"]))) if (!merged.Contains(tag, CanonicalStringComparer.Instance)) merged.Add(tag);
            collection = collection with { Tags = CliExamplePolicy.Tags(merged) };
        }
        else throw Invalid();
        var usageIds = new List<Guid>(); var examples = sense.Examples.ToList(); var allExamples = entry.Examples.ToList();
        var rawSources = graph["sources"]!.AsArray(); var rawOccurrences = graph["occurrences"]!.AsArray(); var rawClaims = graph["interpretations"]!.AsArray();
        foreach (var (exampleNode, position) in (input["examples"] as JsonArray ?? []).Select((value, offset) => (value, offset)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = CliExamplePolicy.Create(exampleNode!.AsObject(), entry.Expression, Id("example." + position), now);
            int existing = examples.FindIndex(item => StringComparer.Ordinal.Equals(CliExamplePolicy.Key(item), CliExamplePolicy.Key(value)));
            if (existing >= 0)
            {
                var merged = CliExamplePolicy.Merge(examples[existing], value); examples[existing] = merged;
                var prior = draft.Working().Interpretations.First(item => item.Id == merged.Id);
                var claim = prior with { Meaning = merged.Meaning, MeaningLanguage = merged.MeaningLanguage, MeaningOrigin = merged.MeaningOrigin,
                    Translation = merged.Translation, TranslationLanguage = merged.TranslationLanguage, TranslationVariants = merged.TranslationVariants };
                CliResourceDraft.UpsertRaw(rawClaims, "id", claim.Id, CliResourceDraft.RawValue(claim)); usageIds.Add(merged.Id);
            }
            else
            {
                if (allExamples.Any(item => StringComparer.Ordinal.Equals(CliExamplePolicy.Key(item), CliExamplePolicy.Key(value))))
                    throw new CliException(CliErrorCode.Conflict, "This usage already belongs to another sense. Append to its current owner.");
                var current = draft.Working();
                if (current.Sources.Any(item => item.Id == value.SourceId) || current.Occurrences.Any(item => item.Id == value.Id)
                    || current.Interpretations.Any(item => item.Id == value.Id))
                    throw new CliException(CliErrorCode.Conflict, "Generated evidence identity already exists.");
                var source = new F.VocabularySourceSnapshot(value.SourceId, value.Text, value.SourceKind, null, now);
                var occurrence = new R.VocabularySourceOccurrence(value.Id, value.SourceId, value.Ranges);
                var claim = new F.VocabularyInterpretation(value.Id, value.Id, now) { SenseId = sense.Id, Status = R.VocabularyInterpretationStatus.Accepted,
                    Meaning = value.Meaning, MeaningLanguage = value.MeaningLanguage, MeaningOrigin = value.MeaningOrigin, Translation = value.Translation,
                    TranslationLanguage = value.TranslationLanguage, Completeness = value.Completeness };
                CliResourceDraft.UpsertRaw(rawSources, "id", source.Id, CliResourceDraft.RawValue(source));
                CliResourceDraft.UpsertRaw(rawOccurrences, "id", occurrence.Id, CliResourceDraft.RawValue(occurrence));
                CliResourceDraft.UpsertRaw(rawClaims, "id", claim.Id, CliResourceDraft.RawValue(claim));
                examples.Add(value); allExamples.Add(value); usageIds.Add(value.Id);
            }
        }
        var blocks = (collection.Content ?? []).ToList(); var blockIds = new List<Guid>();
        foreach (var (item, position) in (input["content"] as JsonArray ?? []).Select((value, offset) => (value, offset)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = item!.AsObject(); VocabularyBlockContent content;
            if (block["text"] is { } text)
                content = new VocabularyBlockContent.Text(new VocabularyTextContent(CliExamplePolicy.Text(text.GetValue<string>(), 262144, true), Bold: block["bold"]?.GetValue<bool>() ?? false, Italic: block["italic"]?.GetValue<bool>() ?? false));
            else if (block["image"] is JsonObject image && image["path"] is null && image["data"] is not null && prepared.TryGetValue($"{index}.{position}", out var ready))
                content = new VocabularyBlockContent.Image(ready.Reference, CliExamplePolicy.Text(image["caption"]?.GetValue<string>() ?? "", 16384));
            else throw new CliException(CliErrorCode.InvalidInput, "A block needs text or an image; image paths must be resolved by the CLI.");
            var blockId = Id("block." + position); blocks.Add(new(blockId, content)); blockIds.Add(blockId);
        }
        if (blockIds.Count > 0) collection = collection with { Content = blocks };
        collection = collection with { InterpretationIds = collection.InterpretationIds.Concat(usageIds).Distinct().ToArray() };
        headerSense = headerSense with { UpdatedAt = now };
        if (senseOffset < 0) headers.Add(headerSense); else headers[senseOffset] = headerSense;
        header = header with { Senses = headers, UpdatedAt = now };
        CliResourceDraft.UpsertRaw(graph["catalog"]!.AsArray(), "id", header.Id, CliResourceDraft.RawValue(header));
        CliResourceDraft.UpsertRaw(graph["collections"]!.AsArray(), "senseID", collection.SenseId, CliResourceDraft.RawValue(collection));
        return new JsonObject { ["operation"] = index, ["entryID"] = entry.Id.ToString("D").ToUpperInvariant(), ["senseID"] = sense.Id.ToString("D").ToUpperInvariant(),
            ["createdEntry"] = createdEntry, ["usageIDs"] = new JsonArray(usageIds.Select(id => (JsonNode?)JsonValue.Create(id.ToString("D").ToUpperInvariant())).ToArray()),
            ["blockIDs"] = new JsonArray(blockIds.Select(id => (JsonNode?)JsonValue.Create(id.ToString("D").ToUpperInvariant())).ToArray()) };
    }
    private static IEnumerable<string> Strings(JsonNode? value) => value is JsonArray array ? array.Select(item => item!.GetValue<string>()) : [];
    private static CliException Invalid() => new(CliErrorCode.InvalidInput, "Invalid CLI operation.");
    private static CliException Missing() => new(CliErrorCode.NotFound, "Entry or sense no longer exists. Read it again before writing.");
}
