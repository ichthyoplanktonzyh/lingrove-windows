using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>The real private Mac merge helpers/pruning own expected results; construction alone is mirrored.</summary>
public sealed class VocabularyEditParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-edit-v1.json"))).RootElement.Clone();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsConcurrentSenseUsageMergeAndPruning(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var actual = JsonSerializer.SerializeToNode(Observe(row.GetProperty("operation").GetString()!));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static object Observe(string mode)
    {
        VocabularySense[] a = [Sense()]; var current = a.ToArray(); var edited = a.ToArray();
        try
        {
            if (mode.StartsWith("prune-", StringComparison.Ordinal))
            {
                var e = Entry(a); var original = a[0];
                a[0] = original with { Explanations = [Record(100, e, original, null)], Examples = [original.Examples[0] with { Explanations = [Record(101, e, original, original.Examples[0])] }] };
                switch (mode)
                {
                    case "prune-moved": var s = Sense(4) with { Examples = a[0].Examples }; a = [a[0] with { Examples = [] }, s]; break;
                    case "prune-edited": a[0] = a[0] with { Examples = [a[0].Examples[0] with { Text = "The bank changed." }] }; break;
                    case "prune-general": a[0] = a[0] with { IsSavedWithoutContext = true }; break;
                    case "prune-form": a[0] = a[0] with { IsSavedWithoutContext = true }; e = e with { DictionaryForm = "other" }; break;
                    default: throw new InvalidOperationException("Unknown prune fixture.");
                }
                return new { senses = View(VocabularyArchiveDraftPolicy.PruneExplanations(new() { Entries = [e with { Senses = a }] }).Entries[0].Senses) };
            }
            switch (mode)
            {
                case "merge-current-append": current = [current[0], Sense(4)]; edited[0] = edited[0] with { Note = "Edited note" }; break;
                case "merge-conflict-note": current[0] = current[0] with { Note = "Live" }; edited[0] = edited[0] with { Note = "Edited" }; break;
                case "merge-same-note": current[0] = current[0] with { Note = "Same" }; edited[0] = edited[0] with { Note = "Same" }; break;
                case "merge-unchanged": current[0] = current[0] with { Note = "Live" }; break;
                case "merge-delete-changed": current[0] = current[0] with { Note = "Live" }; edited = []; break;
                case "merge-new-alias": current[0] = current[0] with { MergedFrom = [Id(99)] }; edited = [edited[0], Sense(99)]; break;
                case "merge-new": edited = [edited[0], Sense(4)]; break;
                case "merge-delete": a = [a[0], Sense(4)]; current = a.ToArray(); edited = [a[0]]; break;
                case "merge-meaning": edited[0] = edited[0] with { Meaning = "My meaning", MeaningLanguage = LearningLanguage.Chinese }; break;
                case "merge-adopt": edited[0] = edited[0] with { MeaningOrigin = VocabularyMeaningOrigin.User }; break;
                case "merge-standalone": a[0] = a[0] with { IsSavedWithoutContext = true }; current = a.ToArray(); edited = a.ToArray(); edited[0] = edited[0] with { Meaning = "My general" }; break;
                case "merge-edit-clear": a[0] = a[0] with { Examples = [a[0].Examples[0] with { MeaningOrigin = VocabularyMeaningOrigin.User }] }; current = a.ToArray(); edited = a.ToArray(); edited[0] = edited[0] with { Examples = [edited[0].Examples[0] with { Text = "The bank changed.", Meaning = "" }] }; break;
                case "merge-usage-meaning": edited[0] = edited[0] with { Examples = [edited[0].Examples[0] with { Meaning = "My contextual" }] }; break;
                case "merge-usage-conflict": current[0] = current[0] with { Examples = [current[0].Examples[0] with { Translation = "Live" }] }; edited[0] = edited[0] with { Examples = [edited[0].Examples[0] with { Meaning = "Edited" }] }; break;
                case "merge-usage-deleted": current[0] = current[0] with { Examples = [] }; edited[0] = edited[0] with { Examples = [edited[0].Examples[0] with { Meaning = "Edited" }] }; break;
                case "merge-new-duplicate": current[0] = current[0] with { Examples = [current[0].Examples[0], Usage(4, "A bank opened.", 2)] }; edited[0] = edited[0] with { Examples = [edited[0].Examples[0], Usage(5, "A bank opened.", 2)] }; break;
                case "merge-usage-delete-conflict": current[0] = current[0] with { Examples = [current[0].Examples[0] with { Translation = "Live" }] }; edited[0] = edited[0] with { Examples = [] }; break;
                case "merge-no-standalone": a[0] = a[0] with { IsSavedWithoutContext = true, StandaloneMeanings = [new("general", null)] }; current = a.ToArray(); edited = a.ToArray(); current[0] = current[0] with { IsSavedWithoutContext = false, StandaloneMeanings = [] }; edited[0] = edited[0] with { Examples = [] }; break;
                case "merge-canonical": a[0] = a[0] with { Note = "cafe\u0301", Tags = ["cafe\u0301"] }; current = a.ToArray(); edited = a.ToArray(); current[0] = current[0] with { Note = "Live", Tags = ["Live"] }; edited[0] = edited[0] with { Note = "café", Tags = ["café"] }; break;
                case "merge-content-conflict": current[0] = current[0] with { Content = [] }; edited[0] = edited[0] with { Content = [new(Id(20), new VocabularyBlockContent.Text(new("literal")))] }; break;
                default: throw new InvalidOperationException("Unknown merge fixture.");
            }
            var result = VocabularyArchiveDraftPolicy.Update(Entry(edited), Entry(a), new() { Entries = [Entry(current)] }, Now.AddSeconds(1));
            return new { senses = View(result.Entries[0].Senses) };
        }
        catch (VocabularyException error) when (error.Failure == VocabularyFailure.EditConflict) { return new { error = "editConflict" }; }
    }
    private static object View(IReadOnlyList<VocabularySense> senses) => senses.Select(s => new { id = s.Id.ToString("D"), meaning = s.Meaning,
        language = Code(s.MeaningLanguage), origin = Code(s.MeaningOrigin), note = s.Note, tags = s.Tags, standalone = s.IsSavedWithoutContext,
        variants = s.StandaloneMeanings.Select(meaning => meaning.Text), records = s.Explanations.Select(record => record.Id.ToString("D")),
        content = s.Content?.Select(block => block.Id.ToString("D")), updated = s.UpdatedAt != Now,
        usages = s.Examples.Select(u => new { id = u.Id.ToString("D"), text = u.Text, translation = u.Translation, meaning = u.Meaning,
            origin = Code(u.MeaningOrigin), records = u.Explanations.Select(record => record.Id.ToString("D")) }) });
    private static string? Code<T>(T value) { var json = JsonSerializer.SerializeToElement(value); return json.ValueKind == JsonValueKind.Null ? null : json.GetString(); }
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12", CultureInfo.InvariantCulture));
    private static VocabularyExample Usage(int n = 3, string text = "The bank.", int offset = 4) => new(Id(n), text, "", [new(offset, 4)], VocabularyExampleCompleteness.Sentence, "draft", Now);
    private static VocabularySense Sense(int n = 2) => new(Id(n), "", Now, Now) { Examples = n == 2 ? [Usage()] : [] };
    private static VocabularyEntry Entry(IReadOnlyList<VocabularySense> senses) => new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, senses, Now, Now);
    private static LearningExplanationRecord Record(int n, VocabularyEntry entry, VocabularySense sense, VocabularyExample? usage) => new(Id(n),
        LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, usage, sense)), LearningLanguage.Chinese, new("meaning", "explanation"), "fixture", Now);
}
