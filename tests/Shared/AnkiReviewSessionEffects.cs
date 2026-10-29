using System.Text.Json;
using Cotabby.Core.Review;
using Cotabby.Core.Speech;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.Speech;
using System.Text;

namespace Cotabby.ReviewTesting;

/// <summary>Stateful synthetic Anki effects mirror the pinned ReviewFakeAnki test actor. They
/// record media/notes and can lose or hold a committed add reply. Product admission, conflict,
/// cancellation, scope checks and persistence belong to the real session/controller/archive.</summary>
internal sealed class SessionTestServer : IAnkiConnecting
    {
        internal int AddCount => _adds;
        internal Func<Task>? AfterAdd { get; set; }
        public List<object> Calls { get; } = [];
        private readonly Dictionary<long, Dictionary<string, string>> _notes = [];
        private readonly Dictionary<long, string> _models = [];
        private readonly Dictionary<string, IReadOnlyList<string>> _fields = [];
        private long _next = 1000;
        private readonly Dictionary<long, string> _decks = [];
        private readonly Dictionary<string, byte[]> _media = [];
        private IReadOnlyDictionary<string, AnkiValue> _templates = AnkiNoteTemplate.Templates, _flexibleTemplates = ReviewLayoutPolicy.Templates;
        private string _css = AnkiNoteTemplate.Css;
        private int _adds, _updates;
        private bool _sawMedia;
        public string Profile { get; set; } = "Acceptance";
        public IReadOnlyList<string> Decks { get; set; } = new[] { "Default", "Other" };
        public bool LoseAdd { get; set; }
        public bool FailAdd { get; set; }
        public string? ProfileAfterMetadata { get; set; }
        public string? ProfileAfterMedia { get; set; }
        public Action? AfterMetadata { get; set; }
        public Action? CancelAfterMedia { get; set; }
        public Action? AfterNote { get; set; }
        public Func<Task>? AfterMetadataAsync { get; set; }
        public void SetFields(string model, IReadOnlyList<string> fields) => _fields[model] = fields;
        public void Edit(long id, string field, string text) { if (_notes.TryGetValue(id, out var values)) values[field] = text; }
        public void Delete(long id) => _notes.Remove(id);
        public void RemoveMedia() => _media.Clear();
        public void Legacy() { _templates = AnkiNoteTemplate.LegacyTemplates; _css = AnkiNoteTemplate.LegacyCss; SetFields(AnkiReviewPolicy.PresetName, Enum.GetValues<ReviewField>().Where(f => f is not (ReviewField.ExpressionAudio or ReviewField.SentenceAudio)).Select(f => JsonSerializer.SerializeToElement(f).GetString()!).ToArray()); }
        public void Customize() { var values = new Dictionary<string, AnkiValue>(_templates); values["Recognition"] = AnkiValue.Strings(new Dictionary<string, string> { ["Front"] = "custom", ["Back"] = "custom" }); _templates = values; }
        public void UnsafeFlexible() { var values = new Dictionary<string, AnkiValue>(_flexibleTemplates); values["Card"] = AnkiValue.Strings(new Dictionary<string, string> { ["Front"] = "{{Back}}", ["Back"] = "{{Back}}" }); _flexibleTemplates = values; }
        public object Snapshot() => new { add = _adds, update = _updates, notes = _notes.OrderBy(p => p.Key).Select(p => new { id = p.Key, fields = new Dictionary<string, string>(p.Value), model = _models[p.Key], deck = _decks[p.Key] }).ToArray(), media = _media.ToDictionary(p => p.Key, p => new { bytes = p.Value.Length, sha256 = SpeechExportPolicy.Digest(p.Value) }) };
        private static string Str(IReadOnlyDictionary<string, AnkiValue> p, string key) => p.GetValueOrDefault(key)?.String ?? "";
        public async Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken cancellationToken = default)
        {
            Calls.Add(new { action, @params = parameters }); AnkiValue result;
            switch (action)
            {
                case "getActiveProfile": result = new AnkiValue.Text(Profile); break;
                case "deckNames": result = AnkiValue.List(Decks); break;
                case "modelNames": result = AnkiValue.List(new[] { AnkiReviewPolicy.PresetName }.Concat(_fields.Keys.Order(StringComparer.Ordinal)).ToArray()); break;
                case "modelFieldNames":
                    if (ProfileAfterMetadata is { } profile) { Profile = profile; ProfileAfterMetadata = null; }
                    if (AfterMetadata is { } hook) { AfterMetadata = null; hook(); }
                    result = AnkiValue.List(_fields.GetValueOrDefault(Str(parameters, "modelName")) ?? Enum.GetValues<ReviewField>().Select(f => JsonSerializer.SerializeToElement(f).GetString()!).ToArray()); break;
                case "modelTemplates": result = new AnkiValue.Map(Str(parameters, "modelName") == ReviewLayoutPolicy.Model ? _flexibleTemplates : _templates); break;
                case "modelStyling": result = AnkiValue.Strings(new Dictionary<string, string> { ["css"] = _css }); break;
                case "findNotes": result = new AnkiValue.Sequence(_notes.Where(p => Str(parameters, "query").Contains(p.Value.GetValueOrDefault("LingroveID") ?? "never-match", StringComparison.Ordinal)).Select(p => (AnkiValue)new AnkiValue.Number(p.Key)).ToArray()); break;
                case "notesInfo":
                    result = new AnkiValue.Sequence(parameters["notes"].Array!.Select(v =>
                    {
                        long id = v.Integer!.Value;
                        return _notes.TryGetValue(id, out var fields) ? new AnkiValue.Map(new Dictionary<string, AnkiValue> { ["noteId"] = new AnkiValue.Number(id), ["modelName"] = new AnkiValue.Text(_models[id]),
                            ["fields"] = new AnkiValue.Map(fields.ToDictionary(p => p.Key, p => (AnkiValue)AnkiValue.Strings(new Dictionary<string, string> { ["value"] = p.Value }))) }) : (AnkiValue)new AnkiValue.Map(new Dictionary<string, AnkiValue>());
                    }).ToArray()); break;
                case "addNote":
                    if (FailAdd) { FailAdd = false; throw new AnkiReviewException("The operation couldn’t be completed. (NSURLErrorDomain error -1004.)"); }
                    var note = parameters["note"].Object!; long next = ++_next; _notes[next] = note["fields"].Object!.ToDictionary(p => p.Key, p => p.Value.String!); _models[next] = Str(note, "modelName"); _decks[next] = Str(note, "deckName"); _adds++;
                    if (AfterAdd is { } after) { AfterAdd = null; await after(); }
                    if (LoseAdd) { LoseAdd = false; throw new AnkiReviewException("The operation couldn’t be completed. (NSURLErrorDomain error -1005.)"); }
                    result = new AnkiValue.Number(next); break;
                case "updateNoteFields":
                    var update = parameters["note"].Object!; foreach (var p in update["fields"].Object!) _notes[update["id"].Integer!.Value][p.Key] = p.Value.String!; _updates++; result = AnkiValue.Null; break;
                case "createModel": _fields[Str(parameters, "modelName")] = parameters["inOrderFields"].Array!.Select(v => v.String!).ToArray(); result = AnkiValue.Null; break;
                case "modelFieldAdd": _fields[Str(parameters, "modelName")] = (_fields.GetValueOrDefault(Str(parameters, "modelName")) ?? []).Append(Str(parameters, "fieldName")).ToArray(); result = AnkiValue.Null; break;
                case "updateModelTemplates": _templates = parameters["model"].Object!["templates"].Object!; result = AnkiValue.Null; break;
                case "updateModelStyling": _css = parameters["model"].Object!["css"].String!; result = AnkiValue.Null; break;
                case "storeMediaFile": string filename = Str(parameters, "filename"); _media[filename] = Convert.FromBase64String(Str(parameters, "data")); if (ProfileAfterMedia is { } changed) { Profile = changed; ProfileAfterMedia = null; } _sawMedia = true; result = new AnkiValue.Text(filename); break;
                case "getMediaFilesNames": result = AnkiValue.List(_media.Keys.Where(k => k == Str(parameters, "pattern")).ToArray()); break;
                default: throw new InvalidOperationException("Unexpected synthetic Anki action: " + action);
            }
            if (action == "notesInfo" && AfterNote is { } noteHook) { AfterNote = null; noteHook(); }
            if (action == "modelFieldNames" && AfterMetadataAsync is { } metadataHook) { AfterMetadataAsync = null; await metadataHook(); }
            if (action == "getActiveProfile" && _sawMedia && CancelAfterMedia is { } cancel) { CancelAfterMedia = null; _sawMedia = false; cancel(); }
            return result;
        }
    }

/// <summary>The original in-memory exporter effect: each request produces its numbered Float32
/// sample, encoded by the real export policy. Pausing is caller-cancellable and emits no file.
/// It supplies no plan, media-reuse or fingerprint policy beyond the original fake text digest.</summary>
internal sealed class SessionTestSpeech : ISpeechExporting
{
    public int Exports { get; private set; }
    public bool Pause { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string ExportConfigurationId => "fixture";
    public SpeechExportRequest ExportRequest(string text, LearningLanguage? language, string? context = null) => new(text, language ?? throw new InvalidOperationException("Missing language"), SpeechExportPolicy.Digest(Encoding.UTF8.GetBytes(text)), new SpeechExportRequest.Source.System("fixture"));
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
