using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Tests.Shared;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Fixed graph inputs and semantic observation adapters only. Expected values come from
/// actual pinned Swift methods. This adapter is deliberately not a production export byte codec.</summary>
public sealed class VocabularyExportProjectionParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-projection-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsExportSelectionAndProjection(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var actual = JsonSerializer.SerializeToNode(Observe(row));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static object Observe(JsonElement row)
    {
        string operation = row.GetProperty("operation").GetString()!, mode = row.GetProperty("mode").GetString()!;
        var c = Configuration(row);
        if (operation == "configuration") return new { format = c.Format.Code(), extension = c.Format.FileExtension(),
            style = c.TextStyle.ToString().ToLowerInvariant(), meanings = c.Meanings, examples = c.Examples, notes = c.Notes,
            explanation = c.Explanation, content = c.Content, images = c.Images, spreadsheetCompatibility = c.SpreadsheetCompatibility,
            expressionList = c.IsExpressionList, includesImages = c.IncludesImages, maximumDataBytes = VocabularyExportPolicy.MaximumDataBytes };
        if (operation == "request")
        {
            var a = new VocabularySelection(Id(1), Id(2)); var b = new VocabularySelection(Id(5), Id(6));
            var all = new List<VocabularySelection> { a, b }; var filtered = new List<VocabularySelection> { b }; var selected = new List<VocabularySelection> { a };
            var request = new VocabularyExportRequest(all, filtered, selected, VocabularyExportScope.Filtered);
            all.Clear(); filtered.Clear(); selected.Clear();
            object Facts(VocabularyExportScope scope) => request.Selections(scope).Select(s => new[] { Text(s.EntryId), Text(s.SenseId) });
            return new { all = Facts(VocabularyExportScope.All), filtered = Facts(VocabularyExportScope.Filtered), selected = Facts(VocabularyExportScope.Selected), initial = request.InitialScope.ToString().ToLowerInvariant() };
        }
        var entries = ExportEntries();
        if (operation == "selection")
        {
            VocabularySelection[] selections = [new(Id(1), Id(2))];
            switch (mode)
            {
                case "empty": case "cancelledEmpty": selections = []; break;
                case "missingEntry": selections = [new(Id(99), Id(2))]; break;
                case "missingSense": selections = [new(Id(1), Id(99))]; break;
                case "alias": selections = [new(Id(1), Id(90))]; break;
                case "duplicateEntry": entries.Add(entries[0]); break;
                case "ordered": selections = [new(Id(5), Id(6)), new(Id(1), Id(4)), new(Id(1), Id(2)), new(Id(1), Id(4))]; break;
            }
            try
            {
                var token = new CancellationToken(mode.StartsWith("cancelled", StringComparison.Ordinal));
                return VocabularyExportPolicy.Selected(entries, selections, token).Select(entry => new { entry = Text(entry.Id), senses = entry.Senses.Select(s => Text(s.Id)) });
            }
            catch (VocabularyExportException error) { return new { error = error.Failure.ToString().ToLowerInvariant() }; }
            catch (OperationCanceledException) { return new { error = "cancelled" }; }
        }
        var sense = entries[0].Senses[0];
        switch (mode)
        {
            case "empty": entries.Clear(); break;
            case "absentContent": sense = sense with { Content = null }; break;
            case "emptyContent": sense = sense with { Content = [] }; break;
            case "variants":
                sense = sense with { Meaning = "é", MeaningLanguage = LearningLanguage.French,
                    StandaloneMeanings = [new("e\u0301", LearningLanguage.French), new("é", LearningLanguage.English), new("other", LearningLanguage.French), new("", null), new(" ", null)],
                    AlternateMeanings = [new("other", LearningLanguage.French), new("e\u0301", null), new("é", null), new("other wording", LearningLanguage.French)],
                    Examples = [sense.Examples[0] with { Translation = "é", TranslationLanguage = null,
                        TranslationVariants = [new("e\u0301", null), new("é", LearningLanguage.French), new(" ", null), new("other", null), new("other", null)] }] }; break;
            case "unicodeRanges":
                sense = sense with { Examples = [new(Id(7), "😀 bank bank", "", [new(3, 4), new(8, 4)], VocabularyExampleCompleteness.Fragment, "", DateTimeOffset.UnixEpoch.AddMilliseconds(1125))] }; break;
            case "styledText":
                sense = sense with { Content = [new(Id(24), new VocabularyBlockContent.Text(new("literal <&>\n拾语", VocabularyTextStyle.Heading, true, true)))] }; break;
            case "reviewOnly":
                var review = sense.Review!;
                sense = sense with { Content = [], Review = review with { Plan = review.Plan with { Layout = new([new(Id(51), Core.Review.ReviewContentSource.Custom,
                    content: [new(Id(61), new VocabularyBlockContent.Image(Image(), "private review"))])]) } } }; break;
        }
        if (entries.Count != 0) entries[0] = entries[0] with { Senses = new[] { sense }.Concat(entries[0].Senses.Skip(1)).ToArray() };
        if (operation == "projection") return DocumentFacts(VocabularyExportPolicy.Document(entries, c));
        if (operation == "images") return VocabularyExportPolicy.Images(entries, c).OrderBy(i => i.Digest, StringComparer.Ordinal)
            .ThenBy(i => i.Width).ThenBy(i => i.Height).ThenBy(i => i.ByteCount).Select(ImageFacts);
        throw new InvalidOperationException("Unknown export probe.");
    }
    private static VocabularyExportConfiguration Configuration(JsonElement row)
    {
        bool Flag(string name, bool fallback) => row.TryGetProperty(name, out var value) ? value.GetBoolean() : fallback;
        return new() { Format = row.TryGetProperty("format", out var format) ? Enum.Parse<VocabularyExportFormat>(format.GetString()!, true) : VocabularyExportFormat.Text,
            TextStyle = row.TryGetProperty("style", out var style) ? Enum.Parse<VocabularyExportTextStyle>(style.GetString()!, true) : VocabularyExportTextStyle.Notes,
            Meanings = Flag("meanings", true), Examples = Flag("examples", true), Notes = Flag("notes", true), Explanation = Flag("explanation", false),
            Content = Flag("content", true), Images = Flag("images", false), SpreadsheetCompatibility = Flag("spreadsheetCompatibility", true) };
    }
    internal static List<VocabularyEntry> ExportEntries()
    {
        var d = VocabularyReferenceGraph.Document(); var first = d.Entries[0]; var sense = first.Senses[0];
        sense = sense with { MergedFrom = [Id(90)], Examples = [sense.Examples[0] with { SourceApplicationId = "private.host", MeaningOrigin = VocabularyMeaningOrigin.Ai }],
            Content = [new(Id(20), new VocabularyBlockContent.Text(new("literal"))), new(Id(21), new VocabularyBlockContent.Image(Image(), "literal caption")),
                new(Id(22), new VocabularyBlockContent.Image(Image(), "second occurrence")), new(Id(23), new VocabularyBlockContent.Image(Image(2), "different metadata"))] };
        return [first with { Senses = [sense, new(Id(4), "second", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)] },
            new(Id(5), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [new(Id(6), "third", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)];
    }
    private static VocabularyImageReference Image(int width = 1) => new(new('a', 64), width, 1, 70);
    private static object ImageFacts(VocabularyImageReference image) => new { digest = image.Digest, width = image.Width, height = image.Height, byteCount = image.ByteCount };
    private static object DocumentFacts(VocabularyExportDocument d) => new { format = d.Format, version = d.Version, entries = d.Entries.Select(e => new {
        id = Text(e.Id), expression = e.Expression, dictionaryForm = e.DictionaryForm, language = e.Language.Code(), kind = e.Kind.ToString().ToLowerInvariant(), senses = e.Senses.Select(SenseFacts) }) };
    private static object SenseFacts(VocabularyExportDocument.Sense s) => Fields(("id", Text(s.Id)), ("createdAt", (s.CreatedAt - DateTimeOffset.UnixEpoch).TotalSeconds),
        ("meaning", s.Meaning), ("meaningLanguage", s.MeaningLanguage?.Code()), ("otherMeanings", s.OtherMeanings?.Select(MeaningFacts)),
        ("note", s.Note), ("tags", s.Tags), ("examples", s.Examples?.Select(UsageFacts)), ("explanations", s.Explanations?.Select(ExplanationFacts)), ("content", s.Content?.Select(BlockFacts)));
    private static object UsageFacts(VocabularyExportDocument.Usage u) => Fields(("id", Text(u.Id)), ("createdAt", (u.CreatedAt - DateTimeOffset.UnixEpoch).TotalSeconds),
        ("text", u.Text), ("completeness", u.Completeness.ToString().ToLowerInvariant()), ("ranges", u.Ranges.Select(r => new { location = r.Location, length = r.Length })),
        ("translation", u.Translation), ("translationLanguage", u.TranslationLanguage?.Code()), ("otherTranslations", u.OtherTranslations.Select(MeaningFacts)),
        ("meaning", u.Meaning), ("meaningLanguage", u.MeaningLanguage?.Code()), ("explanations", u.Explanations?.Select(ExplanationFacts)));
    private static object MeaningFacts(VocabularyMeaning m) => Fields(("text", m.Text), ("language", m.Language?.Code()));
    private static object ExplanationFacts(VocabularyExportDocument.Explanation e) => new { language = e.Language.Code(), meaning = e.Meaning, detail = e.Detail };
    private static object BlockFacts(VocabularyExportDocument.Block b) => Fields(("id", Text(b.Id)), ("text", b.Text is { } t ? new { text = t.Text, style = t.Style.ToString().ToLowerInvariant(), bold = t.Bold, italic = t.Italic } : null),
        ("image", b.Image is { } image ? ImageFacts(image) : null), ("filename", b.Filename), ("caption", b.Caption));
    private static Dictionary<string, object?> Fields(params (string Key, object? Value)[] fields) => fields.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value);
    private static Guid Id(int n) => VocabularyReferenceGraph.Id(n);
    private static string Text(Guid value) => value.ToString("D").ToUpperInvariant();
}
