using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;
using Cotabby.ReviewTesting;

namespace Cotabby.App.Tests;

/// <summary>Whole original session traces over actual Store/archive/SQLite. Synthetic server
/// effects preserve the original test server's add/update/identity behaviour; fixture snapshots
/// anonymize only nondeterministic UUIDs/timestamps and hash long payload strings.</summary>
public sealed class AnkiReviewSessionTests
{
    private static readonly JsonElement Corpus = ReadCorpus();
    private static JsonElement ReadCorpus()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "anki-session-v1.json")));
        return document.RootElement.Clone();
    }
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static bool Flag(JsonElement r, string key) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement r, string key, string fallback = "") => r.TryGetProperty(key, out var v) ? v.GetString() ?? fallback : fallback;
    private static VocabularyDocument Seed(JsonElement row)
    {
        var time = DateTimeOffset.UnixEpoch;
        var usage = new VocabularyExample(Id(3), "The bank opened early.", "银行很早开门。", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "银行", time);
        var sense = new VocabularySense(Id(2), "银行", time, time) { MeaningLanguage = LearningLanguage.Chinese, Note = "source note", Examples = Flag(row, "noExample") ? [] : [usage], IsSavedWithoutContext = true };
        var document = new VocabularyDocument { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [sense], time, time)] };
        if (row.TryGetProperty("savedPlan", out var saved))
        {
            var node = JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(document))!;
            var target = node["entries"]![0]!["senses"]![0]!;
            var review = new JsonObject { ["plan"] = JsonNode.Parse(saved.GetRawText()), ["bindings"] = new JsonArray(), ["preservedPlans"] = new JsonArray() };
            if (Flag(row, "card")) target["reviewCards"] = new JsonArray(review); else target["review"] = review;
            document = VocabularyBackupCodec.DecodeDocument(Encoding.UTF8.GetBytes(node.ToJsonString()));
        }
        if (Flag(row, "image")) { var owner = document.Entries[0].Senses[0] with { Content = [new(Id(20), new VocabularyBlockContent.Image(ImageReference, "river"))] }; document = document with { Entries = [document.Entries[0] with { Senses = [owner] }] }; }
        if (Flag(row, "secondSense")) { var e = document.Entries[0]; document = document with { Entries = [e with { Senses = e.Senses.Append(new VocabularySense(Id(8), "河岸", time, time) { MeaningLanguage = LearningLanguage.Chinese, IsSavedWithoutContext = false }).ToArray() }] }; }
        return document;
    }
    public static IEnumerable<object[]> Cases()
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString()); Assert.Equal("en_US", Corpus.GetProperty("referenceLocale").GetString());
        var rows = Corpus.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(57, rows.Length);
        Assert.Equal("663156fe213619f015db843864c7924f00d69b6796ca9ec9ed8ab58216a7ef22", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()! }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task CompleteOriginalSessionTrace(string id)
    {
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var images = new Images(); if (Flag(row, "imageAvailable")) images.Write(ImageBytes, ImageReference);
        var archive = new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory, images: images); await using var store = new VocabularyStore(archive, new SynchronizationContext());
        await archive.CommitAsync(Seed(row)); await store.LoadAsync();
        var app = new AppSettings(); var settings = new AnkiReviewSettings(() => app, () => { }, new Credentials()); var client = new SessionTestServer(); var speech = new Speech();
        if (row.TryGetProperty("settings", out var initial)) Preferences(initial, settings);
        var controller = new AnkiReviewController(settings, store, client, speech: Flag(row, "speech") ? speech : null); var entry = store.Entries[0];
        var session = new AnkiReviewSession(controller, entry, entry.Senses[0], cardId: Flag(row, "card") ? Id(4) : null);
        async Task<object> State()
        {
            var disk = await archive.LoadAsync(); var document = new VocabularyDocument { Entries = store.Entries, ReviewTemplates = disk.Document.ReviewTemplates };
            var planContainer = new VocabularyDocument { Entries = [new(Id(1), "x", "x", LearningLanguage.English, VocabularyEntryKind.Word,
                [new VocabularySense(Id(2), "x", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { Review = new(session.Plan) }], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)] };
            var plan = JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(planContainer))!["entries"]![0]!["senses"]![0]!["review"]!["plan"]!.DeepClone();
            return new { plan, deck = session.Deck, model = session.Model, answer = session.Answer, canGap = session.CanGap, validation = session.ValidationMessage,
                message = session.Message, busy = session.Busy, loading = session.Loading, missing = session.MissingNote, bindingID = session.BindingId?.ToString("D"),
                currentTemplate = session.CurrentPresetTemplate, legacyTemplate = session.LegacyPresetTemplate, safeGapFront = session.SafeGapFront, css = session.PresetCss,
                fields = session.AvailableFields, conflicts = session.Conflicts.Select(c => new { id = c.Id, local = c.Local, remote = c.Remote }),
                store = JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(document)), disk = JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(disk.Document)),
                settings = new { deck = settings.Deck, recognition = settings.DefaultRecognition, gap = settings.DefaultGap, hasSavedDeck = settings.HasSavedDeck },
                status = session.Current is { } current ? controller.Status(current.Entry, current.Sense, session.CardId) : "Unavailable",
                failures = controller.Failures.ToDictionary(p => p.Key.ToString("D"), p => p.Value), unresolved = controller.Unresolved.Contains(session.CardId ?? session.SenseId), controllerMissing = controller.Missing.Contains(session.CardId ?? session.SenseId),
                keepRemote = session.KeepRemote, exports = speech.Exports, generating = session.Audio.Generating is { } kind ? Kind(kind) : null,
                prepared = session.Audio.Prepared.ToDictionary(p => Kind(p.Key), p => new { fingerprint = p.Value.Fingerprint, bytes = p.Value.Audio.ByteCount, sha256 = SpeechExportPolicy.Digest(p.Value.Audio.CopyBytes()) }), server = client.Snapshot() };
        }
        var trace = new List<object> { new { state = await State(), outcome = (object?)null } };
        foreach (var a in row.GetProperty("actions").EnumerateArray())
        {
            object? outcome = null;
            switch (Text(a, "op"))
            {
                case "load": await session.LoadAsync(); break;
                case "save": outcome = await session.SaveAsync(); break;
                case "send": outcome = await session.SendAsync(recreateMissing: Flag(a, "recreate")); break;
                case "sendCancelled": using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); outcome = await session.SendAsync(cancellationToken: cancel.Token); } break;
                case "cancelAfterMedia": using (var cancel = new CancellationTokenSource()) { client.CancelAfterMedia = cancel.Cancel; outcome = await session.SendAsync(cancellationToken: cancel.Token); } break;
                case "cancelAudio":
                    using (var cancel = new CancellationTokenSource())
                    {
                        speech.Pause = true; var work = session.SendAsync(cancellationToken: cancel.Token);
                        try { await speech.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                        finally { cancel.Cancel(); }
                        outcome = await work; speech.Pause = false;
                    } break;
                case "reopen": var e = store.Entries[0]; session = new(controller, e, e.Senses[0], cardId: Flag(row, "card") ? Id(4) : null); break;
                case "openSense": var owner = store.Entries[0]; session = new(controller, owner, owner.Senses.First(v => v.Id == Id(a.GetProperty("sense").GetInt32()))); break;
                case "merge": outcome = await store.MergeSensesAsync(Id(1), Id(a.TryGetProperty("source", out var source) ? source.GetInt32() : 2), Id(a.TryGetProperty("target", out var target) ? target.GetInt32() : 8)); var merged = store.Entries[0]; session = new(controller, merged, merged.Senses[0]); break;
                case "select": session.SelectBinding(Flag(a, "none") ? null : session.SavedReview!.Bindings[a.TryGetProperty("index", out var index) ? index.GetInt32() : 0].Id); break;
                case "externalSave": var original = store.Entries[0]; var other = new AnkiReviewSession(controller, original, original.Senses.First(v => v.Id == session.SenseId), cardId: session.CardId); other.Plan = other.Plan with { Answer = Text(a, "value", "external") }; outcome = await other.SaveAsync(); break;
                case "loadNested": var loading = session; client.AfterMetadataAsync = async () => { settings.Model = "Custom"; await loading.LoadAsync(); }; await session.LoadAsync(); break;
                case "cancelAfterNote": using (var cancel = new CancellationTokenSource()) { client.AfterNote = cancel.Cancel; outcome = await session.SendAsync(cancellationToken: cancel.Token); } break;
                case "importImage": session.Plan = session.Plan with { Layout = new([new(Id(22), ReviewContentSource.Expression)], [new(Id(23), ReviewContentSource.Custom, content: [new(Id(21), new VocabularyBlockContent.Image(ImageReference, "draft"))])]) }; session.ImportedImages = new Dictionary<string, VocabularyPreparedImage> { [ImageReference.Filename] = new(ImageReference, ImageBytes) }; break;
                case "settings": Preferences(a, settings); break;
                case "afterMetadata": var draft = session; client.AfterMetadata = () => { Preferences(a, settings); if (a.TryGetProperty("answer", out var answer)) draft.Plan = draft.Plan with { Answer = answer.GetString()! }; }; break;
                case "profile": client.Profile = Text(a, "value"); break;
                case "profileMetadata": client.ProfileAfterMetadata = Text(a, "value"); break;
                case "profileMedia": client.ProfileAfterMedia = Text(a, "value"); break;
                case "decks": client.Decks = a.GetProperty("values").EnumerateArray().Select(v => v.GetString()!).ToArray(); break;
                case "fields": client.SetFields(Text(a, "model", AnkiReviewPolicy.PresetName), a.GetProperty("values").EnumerateArray().Select(v => v.GetString()!).ToArray()); break;
                case "loseAdd": client.LoseAdd = true; break;
                case "failAdd": client.FailAdd = true; break;
                case "edit": client.Edit(a.TryGetProperty("id", out var editId) ? editId.GetInt64() : 1001, Text(a, "field", "Meaning"), Text(a, "value")); break;
                case "delete": client.Delete(a.TryGetProperty("id", out var deleteId) ? deleteId.GetInt64() : 1001); break;
                case "legacy": client.Legacy(); break;
                case "customize": client.Customize(); break;
                case "unsafeFlexible": client.UnsafeFlexible(); break;
                case "removeMedia": client.RemoveMedia(); break;
                case "updateTemplate": await session.UpdateTemplateAsync(); break;
                case "keep": session.SetKeepRemote(Text(a, "field", "Meaning"), Flag(a, "value")); break;
                case "restore": session.RestorePlan(session.Plan with { Recognition = false, Deck = Text(a, "deck", "Other") }); break;
                case "name": session.Plan = session.Plan with { Name = Text(a, "value") }; break;
                case "recognition": session.Plan = session.Plan with { Recognition = Flag(a, "value") }; break;
                case "gap": session.Plan = session.Plan with { Gap = Flag(a, "value") }; break;
                case "notes": session.Plan = session.Plan with { IncludeNotes = Flag(a, "value") }; break;
                case "audio": session.Plan = session.Plan with { AudioKinds = a.GetProperty("values").EnumerateArray().Select(v => ParseKind(v.GetString()!)).ToHashSet() }; break;
                case "prepare": await session.PrepareAudioAsync(ParseKind(Text(a, "kind", "expression")), Flag(a, "audition"), Flag(a, "force")); break;
                case "override": var overrides = new Dictionary<string, string>(session.Plan.FieldOverrides) { [Text(a, "field")] = Text(a, "value") }; session.Plan = session.Plan with { FieldOverrides = overrides }; break;
                case "answer": session.Plan = session.Plan with { Answer = Text(a, "value") }; break;
                case "deck": session.Deck = Text(a, "value"); break;
                default: throw new InvalidOperationException("Unknown review session action");
            }
            trace.Add(new { state = await State(), outcome });
        }
        var ids = new Dictionary<string, string>(); var observation = JsonSerializer.SerializeToNode(new { trace, calls = client.Calls }); var actual = Canonical(observation, ids, new(), GeneratedIds(observation));
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText());
        Assert.True(JsonNode.DeepEquals(expected, actual), id + "\n" + string.Join("\n", Differences(expected, actual).Take(30)));
    }
    private static IEnumerable<string> Differences(JsonNode? expected, JsonNode? actual, string path = "$")
    {
        if (JsonNode.DeepEquals(expected, actual)) yield break;
        if (expected is JsonObject e && actual is JsonObject a)
        {
            foreach (string key in e.Select(p => p.Key).Union(a.Select(p => p.Key)).Order(StringComparer.Ordinal))
                foreach (string difference in Differences(e[key], a[key], path + "." + key)) yield return difference;
        }
        else if (expected is JsonArray es && actual is JsonArray aa && es.Count == aa.Count)
        { for (int i = 0; i < es.Count; i++) foreach (string difference in Differences(es[i], aa[i], path + "[" + i + "]")) yield return difference; }
        else yield return path + ": expected " + expected + "; actual " + actual;
    }
    private static HashSet<string> GeneratedIds(JsonNode? node)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        void Visit(JsonNode? v)
        {
            if (v is JsonObject o) foreach (var p in o) { if (p.Key is "calls" or "server" or "fields" or "fieldOverrides" or "keepRemote" or "failures") continue; if (p.Key == "id" && p.Value is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out _) && !text.StartsWith("00000000-0000-0000-0000-", StringComparison.OrdinalIgnoreCase)) result.Add(text.ToLowerInvariant()); Visit(p.Value); }
            else if (v is JsonArray a) foreach (var item in a) Visit(item);
        }
        Visit(node); return result;
    }
    private static JsonNode? Canonical(JsonNode? node, Dictionary<string, string> ids, Dictionary<double, string> times, HashSet<string> generated, string key = "")
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var p in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if ((p.Key is "syncedAt" or "createdAt" or "updatedAt") && p.Value is not null && p.Value.GetValue<double>() != -978307200) { double t = p.Value.GetValue<double>(); if (!times.TryGetValue(t, out var label)) { label = "time-" + (times.Count + 1); times.Add(t, label); } result[p.Key] = label; }
                else result[p.Key] = Canonical(p.Value, ids, times, generated, p.Key);
            }
            return result;
        }
        if (node is JsonArray array)
        {
            if (key is "mapping" or "audioReceipts")
            {
                Assert.Equal(0, array.Count % 2);
                return new JsonArray(Enumerable.Range(0, array.Count / 2).OrderBy(i => array[2 * i]!.GetValue<string>(), StringComparer.Ordinal)
                    .SelectMany(i => new[] { array[2 * i]?.DeepClone(), Canonical(array[2 * i + 1], ids, times, generated) }).ToArray());
            }
            var values = array.Select(v => Canonical(v, ids, times, generated)).ToArray();
            return new JsonArray(key == "audioKinds" ? values.OrderBy(v => v!.GetValue<string>(), StringComparer.Ordinal).ToArray() : values);
        }
        if (node is JsonValue value && value.TryGetValue<string>(out string? text))
        {
            var matches = Regex.Matches(text, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            foreach (Match match in matches.Reverse())
            {
                string lower = match.Value.ToLowerInvariant(); if (!generated.Contains(lower)) continue;
                if (!ids.TryGetValue(lower, out string? label)) { label = "generated-" + (ids.Count + 1); ids.Add(lower, label); }
                text = text[..match.Index] + label + text[(match.Index + match.Length)..];
            }
            int count = Encoding.UTF8.GetByteCount(text);
            return count > 2000 ? JsonSerializer.SerializeToNode(new { bytes = count, sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))) }) : JsonValue.Create(text);
        }
        return node?.DeepClone();
    }
    private static string Kind(ReviewAudioKind kind) => kind == ReviewAudioKind.Expression ? "expression" : "sentence";
    private static ReviewAudioKind ParseKind(string kind) => kind switch { "expression" => ReviewAudioKind.Expression, "sentence" => ReviewAudioKind.Sentence, _ => throw new InvalidOperationException() };
    private static void Preferences(JsonElement a, AnkiReviewSettings settings)
    {
        if (a.TryGetProperty("deck", out var deck)) settings.Deck = deck.GetString()!;
        if (a.TryGetProperty("model", out var model)) settings.Model = model.GetString()!;
        if (a.TryGetProperty("address", out var address)) settings.Address = address.GetString()!;
        if (a.TryGetProperty("recognition", out var recognition)) settings.DefaultRecognition = recognition.GetBoolean();
        if (a.TryGetProperty("gap", out var gap)) settings.DefaultGap = gap.GetBoolean();
    }
    private sealed class Speech : ISpeechExporting
    {
        public int Exports { get; private set; }
        public bool Pause { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ExportConfigurationId => "fixture";
        public SpeechExportRequest ExportRequest(string text, LearningLanguage language, string? context = null) => new(text, language, SpeechExportPolicy.Digest(Encoding.UTF8.GetBytes(text)), new SpeechExportRequest.Source.System("fixture"));
        public async Task<SpeechExportAudio> ExportAudioAsync(SpeechExportRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Exports++; Entered.TrySetResult();
            if (Pause) await Task.Delay(Timeout.Infinite, cancellationToken);
            byte[] bytes = new byte[2400 * sizeof(float)]; float sample = Exports / 100f;
            for (int i = 0; i < 2400; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * sizeof(float)), sample);
            return SpeechExportPolicy.Wav(bytes, 24000);
        }
        public void Audition(SpeechExportAudio audio, Guid ownerId, LearningLanguage language) { }
        public void Stop(Guid ownerId) { }
    }
    private static readonly byte[] ImageBytes = Convert.FromBase64String(Corpus.GetProperty("imagePng").GetString()!);
    private static VocabularyImageReference ImageReference => new(SpeechExportPolicy.Digest(ImageBytes), 80, 40, ImageBytes.Length);
    // The portable fixture uses this prevalidated PNG's detached bytes; WIC preparation/private
    // named-file validation have their own native tests and are not claimed by this effect fake.
    private sealed class Images : IVocabularyImageStorage
    {
        private readonly Dictionary<string, byte[]> _values = [];
        public byte[] Read(VocabularyImageReference image) => _values.TryGetValue(image.Filename, out var data) ? (byte[])data.Clone() : throw new VocabularyException(VocabularyFailure.InvalidContent);
        public bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image)
        {
            Assert.Equal(ImageReference, image); Assert.Equal(ImageBytes, data.ToArray()); bool added = !_values.ContainsKey(image.Filename); _values[image.Filename] = data.ToArray(); return added;
        }
        public void Remove(VocabularyImageReference image) => _values.Remove(image.Filename);
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Credentials : IAnkiCredentialStore { public string ReadKey() => ""; public void SaveKey(string value) { } }
}
