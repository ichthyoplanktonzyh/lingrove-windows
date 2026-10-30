using System.Text.Json;
using System.Text.Json.Serialization;
using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Bounded decode of the fixed 3b Free Dictionary API response. This policy has no
/// network, audio, session or credential ownership.</summary>
public static class OnlineDictionaryPolicy
{
    public const int MaximumBytes = 1_000_000;
    public const string Template = "https://api.dictionaryapi.dev/api/v2/entries/en/{query}";
    public static LearningDictionaryResult Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaximumBytes) throw new InvalidDataException("Dictionary response is too large.");
        var records = JsonSerializer.Deserialize<Entry[]>(data) ?? throw new JsonException();
        // Swift decodes the complete typed array before applying presentation limits. C#'s
        // required members reject absent properties but still admit explicit JSON nulls.
        // Validate those throughout the graph, including records outside the visible prefix.
        foreach (var entry in records)
        {
            if (entry is null || entry.Word is null || entry.Meanings is null
                || entry.Phonetics?.Any(p => p is null) == true
                || entry.SourceUrls?.Any(url => url is null) == true
                || entry.License is { } license && (license.Name is null || license.Url is null))
                throw new JsonException("Invalid dictionary entry.");
            foreach (var meaning in entry.Meanings)
            {
                if (meaning is null || meaning.PartOfSpeech is null || meaning.Definitions is null
                    || meaning.Definitions.Any(d => d is null || d.Text is null))
                    throw new JsonException("Invalid dictionary meaning.");
            }
        }
        var values = records.Take(16).Select((entry, index) => new LearningLexiconEntry(
            $"online-{index}-{entry.Word}", "en", entry.Word, [],
            entry.Meanings.Take(32).Select(m => new LearningLexiconSense(m.PartOfSpeech,
                m.Definitions.Take(32).Select(d => d.Text).ToArray(), [])).ToArray(),
            entry.SourceUrls?.FirstOrDefault(url => DictionaryLinkPolicy.ExternalUrl(url) is not null) ?? "https://dictionaryapi.dev/",
            (entry.Phonetics ?? []).Select(p => p.Text).OfType<string>().Distinct(CanonicalStringComparer.Instance).Order(CanonicalStringComparer.Instance).ToArray(),
            entry.Meanings.SelectMany(m => m.Definitions).Select(d => d.Example).OfType<string>().Take(16).ToArray(),
            "Free Dictionary API", entry.License?.Url ?? "", entry.License?.Name ?? "")).ToArray();
        return values.Length == 0 ? LearningDictionaryResult.NotFound : new LearningDictionaryResult.Entries(values);
    }
    private sealed class Entry
    {
        [JsonPropertyName("word")] public required string Word { get; init; }
        [JsonPropertyName("meanings")] public required Meaning[] Meanings { get; init; }
        [JsonPropertyName("phonetics")] public Phonetic[]? Phonetics { get; init; }
        [JsonPropertyName("sourceUrls")] public string[]? SourceUrls { get; init; }
        [JsonPropertyName("license")] public License? License { get; init; }
    }
    private sealed class Meaning
    {
        [JsonPropertyName("partOfSpeech")] public required string PartOfSpeech { get; init; }
        [JsonPropertyName("definitions")] public required Definition[] Definitions { get; init; }
    }
    private sealed class Definition
    {
        [JsonPropertyName("definition")] public required string Text { get; init; }
        [JsonPropertyName("example")] public string? Example { get; init; }
    }
    private sealed class Phonetic { [JsonPropertyName("text")] public string? Text { get; init; } }
    private sealed class License
    {
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("url")] public required string Url { get; init; }
    }
}
