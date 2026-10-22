using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Xunit;

namespace Cotabby.Tests.Parity;

/// <summary>
/// Shared test-only Mac observations. The oracle answers recorded text facts and word ranges;
/// it contains no segmentation/casing/recognition implementation and is never registered by App.
/// </summary>
internal static class LearningLexicalCorpus
{
    public static IReadOnlyList<JsonElement> Cases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "learning-lexical-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", document.RootElement.GetProperty("macReference").GetString());
        var rows = document.RootElement.GetProperty("cases").EnumerateArray().Select(row => row.Clone()).ToArray();
        foreach (var row in rows)
        {
            bool nativeWordParity = row.GetProperty("nativeWordParity").GetBoolean();
            if (nativeWordParity) Assert.Contains(Text(row, "language"), new[] { "en", "es", "fr" });
        }
        return rows;
    }

    public static LearningLanguage Language(JsonElement row) => LearningLanguages.FromCode(Text(row, "language"))!.Value;
    public static string Text(JsonElement row, string name) => row.GetProperty(name).GetString()!;
    public static LearningTextRange Range(JsonElement row) => new(row.GetProperty("location").GetInt32(), row.GetProperty("length").GetInt32());
    public static LearningDictionaryWord Word(JsonElement row) => new(Text(row, "text"), Range(row.GetProperty("range")));
    public static LearningLexiconEntry[] Entries(JsonElement row) => row.GetProperty("entries").EnumerateArray().Select(item =>
        new LearningLexiconEntry(Text(item, "id"), Text(item, "language"), Text(item, "headword"),
            item.GetProperty("forms").EnumerateArray().Select(value => value.GetString()!).ToArray(), [], "https://example.invalid/fixture")).ToArray();

    public static void AssertRecognition(JsonElement row, ILearningLexicalText text)
    {
        var recognizer = new LearningUnitRecognizer(Entries(row), text);
        var units = recognizer.Recognize(Text(row, "text"), Language(row));
        var expected = row.GetProperty("expected");
        var expectedUnits = expected.GetProperty("units").EnumerateArray().ToArray();
        Assert.Equal(expectedUnits.Length, units.Count);
        for (int index = 0; index < units.Count; index++)
        {
            var unit = units[index]; var wanted = expectedUnits[index];
            Assert.Equal(wanted.GetProperty("id").GetInt32(), unit.Id);
            Assert.Equal(Text(wanted, "surface"), unit.Surface);
            Assert.Equal(Text(wanted, "query"), unit.Query);
            Assert.Equal(Text(wanted, "kind"), unit.Kind.ToString().ToLowerInvariant());
            Assert.Equal(wanted.GetProperty("entryID").GetString(), unit.EntryId);
            Assert.Equal(wanted.GetProperty("ranges").EnumerateArray().Select(Range), unit.Ranges);
            Assert.Equal(wanted.GetProperty("words").EnumerateArray().Select(Word), unit.Words);
        }
        foreach (var lookup in expected.GetProperty("lookups").EnumerateArray())
            Assert.Equal(lookup.GetProperty("ids").EnumerateArray().Select(value => value.GetString()),
                recognizer.Lookup(Text(lookup, "query")).Select(entry => entry.Id));
        foreach (var hit in expected.GetProperty("hits").EnumerateArray())
        {
            var unit = LearningUnitPolicy.UnitAt(hit.GetProperty("location").GetInt32(), units);
            Assert.Equal(hit.GetProperty("id").ValueKind == JsonValueKind.Null ? (int?)null : hit.GetProperty("id").GetInt32(), unit?.Id);
            Assert.Equal(hit.GetProperty("kind").GetString(), unit?.Kind.ToString().ToLowerInvariant());
        }
    }

    public static ILearningLexicalText Oracle(JsonElement row) => new ObservedText(row);

    private sealed class ObservedText(JsonElement row) : ILearningLexicalText
    {
        private readonly Dictionary<string, JsonElement> _facts = row.GetProperty("expected").GetProperty("facts").EnumerateArray()
            .ToDictionary(fact => Text(fact, "text"), StringComparer.Ordinal);
        public string Key(string value) => Text(_facts[value], "key");
        public bool IsPatternText(string value) => _facts[value].GetProperty("patternText").GetBoolean();
        public bool FirstIsUppercase(string value) => _facts[value].GetProperty("firstUppercase").GetBoolean();
        public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language)
        {
            Assert.Equal(Text(row, "text"), value); Assert.Equal(Language(row), language);
            return row.GetProperty("expected").GetProperty("words").EnumerateArray().Select(Word).ToArray();
        }
    }
}
