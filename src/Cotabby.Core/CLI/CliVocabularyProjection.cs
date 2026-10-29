using System.Text;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Notebook;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.CLI;

/// <summary>Source collected-only status and vocab projections, independent of UI/store lifetime.</summary>
public static class CliVocabularyProjection
{
    public static JsonObject Status(F.VocabularyDocument document, ulong revision)
    {
        var entries = document.CollectedEntries;
        return new() { ["storage"] = "sqlite", ["schemaVersion"] = 5, ["revision"] = revision, ["entryCount"] = entries.Count,
            ["catalogEntryCount"] = document.Catalog.Count, ["sourceCount"] = document.Sources.Count, ["occurrenceCount"] = document.Occurrences.Count,
            ["interpretationCount"] = document.Interpretations.Count, ["collectionCount"] = document.Collections.Count,
            ["resources"] = new JsonArray(CliArguments.Resources.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["senseCount"] = entries.Sum(e => e.Senses.Count), ["catalogSenseCount"] = document.Catalog.Sum(e => e.Senses.Count),
            ["languages"] = new JsonArray(LearningLanguages.All.Select(x => (JsonNode?)JsonValue.Create(x.Code())).ToArray()),
            ["kinds"] = new JsonArray(Enum.GetValues<VocabularyEntryKind>().Select(x => (JsonNode?)JsonValue.Create(x.ToString().ToLowerInvariant())).ToArray()) };
    }
    public static JsonObject List(F.VocabularyDocument document, ulong revision, IVocabularyTextSearching search, string? query = null, string? language = null, int limit = 50, int offset = 0)
    {
        if (limit is < 1 or > 200 || offset < 0 || Encoding.UTF8.GetByteCount(query ?? "") > 4096 || language is not null && language != "und" && LearningLanguages.FromCode(language) is null)
            throw new CliException(CliErrorCode.InvalidInput, "Use a supported language, a limit of 1–200 and a nonnegative offset.");
        var entries = document.CollectedEntries.Where(entry => (language is null || (entry.Language?.Code() ?? "und") == language) && VocabularyFrozenEntrySearch.Matches(entry, query ?? "", search)).ToArray();
        var page = entries.Skip(Math.Min(offset, entries.Length)).Take(limit).Select(entry => new JsonObject { ["entryID"] = entry.Id.ToString("D").ToUpperInvariant(),
            ["expression"] = Summary(entry.Expression), ["expressionTruncated"] = entry.Expression.EnumerateRunes().Skip(200).Any(), ["language"] = entry.Language?.Code() ?? "und",
            ["kind"] = entry.Kind.ToString().ToLowerInvariant(), ["senseCount"] = entry.Senses.Count,
            ["senses"] = new JsonArray(entry.Senses.Take(50).Select(sense => (JsonNode?)new JsonObject { ["senseID"] = sense.Id.ToString("D").ToUpperInvariant(),
                ["meaning"] = Summary(sense.Meaning), ["meaningTruncated"] = sense.Meaning.EnumerateRunes().Skip(200).Any(), ["usageCount"] = sense.Examples.Count }).ToArray()) }).ToArray();
        return new() { ["revision"] = revision, ["total"] = entries.Length, ["offset"] = offset, ["entries"] = new JsonArray(page) };
    }
    public static JsonObject Get(F.VocabularyDocument document, ulong revision, Guid id)
    {
        var entry = document.CollectedEntries.FirstOrDefault(e => e.Id == id) ?? throw new CliException(CliErrorCode.NotFound, "Entry not found."); CliReadBudget.Validate(entry);
        var options = new VocabularyExportConfiguration { Explanation = true };
        var value = F.FrozenVocabularyExportPolicy.Document([entry], options).Entries[0];
        return new() { ["revision"] = revision, ["entry"] = JsonNode.Parse(VocabularyPayloadCodec.EncodePortableValue(value, CliInputContract.MaximumBytes)) };
    }
    private static string Summary(string text) { var builder = new StringBuilder(); foreach (var rune in text.EnumerateRunes().Take(200)) builder.Append(rune); return builder.ToString(); }
}
