using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Review;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Review;

/// <summary>One UI-context owner coordinates shared preferences, notebook snapshots and an
/// injected transport. Windows borrow it rather than constructing clients. The short operation
/// lease prevents concurrent note creation; sessions will own writes and durable acknowledgement.
/// Construction is inert. Async methods retain the caller's UI context like the source MainActor.</summary>
internal sealed partial class AnkiReviewController(AnkiReviewSettings settings, VocabularyStore store,
    IAnkiConnecting client, Func<string?>? speechConfigurationId = null, Action? activateAnki = null) : INotifyPropertyChanged
{
    internal AnkiReviewSettings Settings { get; } = settings;
    internal VocabularyStore Store { get; } = store;
    internal IAnkiConnecting Client { get; } = client;
    private readonly Func<string?> _speechId = speechConfigurationId ?? (() => null);
    private readonly Action _activateAnki = activateAnki ?? (() => { });
    private readonly Dictionary<Guid, string> _failures = [];
    private readonly HashSet<Guid> _missing = [], _unresolved = [];
    private bool _openingMenuReview;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal AnkiReviewCatalog? Catalog { get; private set; }
    internal string? ConnectionMessage { get; private set; }
    internal bool Testing { get; private set; }
    internal Guid? SendingReviewId { get; private set; }
    internal Guid? SendingSenseId { get; private set; }
    internal IReadOnlyList<string> ModelFields { get; private set; } = [];
    internal IReadOnlyDictionary<Guid, string> Failures => new ReadOnlyDictionary<Guid, string>(new Dictionary<Guid, string>(_failures));
    internal IReadOnlySet<Guid> Missing => _missing.ToFrozenSet();
    internal IReadOnlySet<Guid> Unresolved => _unresolved.ToFrozenSet();
    internal bool UpdatingTemplate { get; private set; }
    internal Action? Configure { get; set; }
    private void Changed() => PropertyChanged?.Invoke(this, new(null));

    internal async Task TestConnectionAsync(string? selectedModel = null, CancellationToken cancellationToken = default)
    {
        if (Testing) return;
        Testing = true; ConnectionMessage = null; Changed();
        try
        {
            var connection = Settings.Connection(); string model = selectedModel ?? Settings.Model;
            var next = await FetchCatalogAsync(connection, cancellationToken);
            var available = await FieldsAsync(model, connection, create: false, cancellationToken: cancellationToken);
            if (!Settings.Connection().Equals(connection) || selectedModel is null && !Same(model, Settings.Model)) return;
            Catalog = next; ModelFields = available; ConnectionMessage = "Connected"; Changed();
        }
        catch (Exception error) { Catalog = null; ModelFields = []; ConnectionMessage = error.Message; Changed(); }
        finally { Testing = false; Changed(); }
    }
    internal async Task RefreshFieldsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string model = Settings.Model; var connection = Settings.Connection();
            var result = await FieldsAsync(model, connection, create: false, cancellationToken: cancellationToken);
            if (!Same(model, Settings.Model) || !Settings.Connection().Equals(connection)) return;
            ModelFields = result; Changed();
        }
        catch (Exception error) { ModelFields = []; ConnectionMessage = error.Message; Changed(); }
    }
    internal async Task<ReviewOperationScope> OperationScopeAsync(CancellationToken cancellationToken = default)
    {
        var connection = Settings.Connection(); string model = Settings.Model; var mapping = Settings.EffectiveMapping;
        string? speechId = _speechId(); string deck = Settings.Deck; bool hadSavedDeck = Settings.HasSavedDeck;
        bool recognition = Settings.DefaultRecognition, gap = Settings.DefaultGap;
        string profile = await ProfileAsync(connection, cancellationToken);
        var scope = new ReviewOperationScope(connection, profile, model, mapping, speechId, deck, hadSavedDeck, recognition, gap);
        if (!Matches(scope)) throw Conflict();
        return scope;
    }
    internal bool Matches(ReviewOperationScope scope)
    {
        try { return Settings.Connection().Equals(scope.Connection) && Same(Settings.Model, scope.Model)
            && SameMap(Settings.EffectiveMapping, scope.Mapping) && Same(_speechId(), scope.SpeechId); }
        catch { return false; }
    }
    internal async Task VerifyAsync(ReviewOperationScope scope, CancellationToken cancellationToken = default)
    {
        if (!Matches(scope) || !Same(await ProfileAsync(scope.Connection, cancellationToken), scope.Profile) || !Matches(scope)) throw Conflict();
    }
    internal void InvalidateConnection() { Catalog = null; ModelFields = []; ConnectionMessage = null; Changed(); }
    internal (VocabularyEntry Entry, VocabularySense Sense) Current(Guid entryId, Guid senseId)
    {
        var entry = Store.Entries.FirstOrDefault(e => e.Id == entryId);
        var sense = entry?.Senses.FirstOrDefault(s => s.Id == senseId);
        return entry is not null && sense is not null ? (entry, sense) : throw Conflict();
    }
    internal string Status(VocabularyEntry entry, VocabularySense sense, Guid? cardId = null)
    {
        Guid key = cardId ?? sense.Id;
        if (SendingReviewId == key) return "Sending…";
        if (_failures.ContainsKey(key)) return "Send failed";
        if (_unresolved.Contains(key)) return "Anki fields changed";
        if (_missing.Contains(key)) return "Anki note missing";
        var review = sense.ReviewValue(cardId);
        if (review is null) return "Not added";
        if (review.NeedsBindingChoice) return "Choose an Anki association";
        var binding = review.ActiveBinding;
        if (binding is null) return "Not added";
        if (binding.NoteId is null) return "Ready to send";
        ReviewCardContent content; IReadOnlyDictionary<string, string> fields;
        try { content = ReviewCardPolicy.Content(entry, sense, review.Plan); fields = AnkiReviewPolicy.Fields(content, binding.OriginSenseId, binding.Mapping, review.Plan.FieldOverrides, AnkiReviewPolicy.AudioFields(review.Plan)); }
        catch { return "Review content needs attention"; }
        if (!AnkiReviewPolicy.CurrentAudio(review.Plan, content)) return "Update available";
        return SameMap(fields, binding.Fields) ? "Synced" : "Update available";
    }
    internal bool IsSynchronized(VocabularyEntry entry, VocabularySense sense, Guid? cardId = null)
    {
        Guid key = cardId ?? sense.Id; var review = sense.ReviewValue(cardId); var binding = review?.ActiveBinding;
        if (_unresolved.Contains(key) || _missing.Contains(key) || _failures.ContainsKey(key) || review is null || binding?.NoteId is null) return false;
        try
        {
            var content = ReviewCardPolicy.Content(entry, sense, review.Plan);
            return AnkiReviewPolicy.CurrentAudio(review.Plan, content) && SameMap(AnkiReviewPolicy.Fields(content, binding.OriginSenseId,
                binding.Mapping, review.Plan.FieldOverrides, AnkiReviewPolicy.AudioFields(review.Plan)), binding.Fields);
        }
        catch { return false; }
    }
    internal async Task<string?> OpenAsync(AnkiNoteBinding binding, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = Settings.Connection(); await VerifyScopeAsync(binding, connection, cancellationToken);
            if (binding.NoteId is not { } id) return null;
            var remote = await NoteAsync(id, connection, cancellationToken);
            if (remote is null) throw new AnkiReviewException("The associated Anki note no longer exists.");
            if (!Same(remote.Fields.GetValueOrDefault(binding.IdentityField), binding.OriginSenseId.ToString("D").ToLowerInvariant()))
                throw new AnkiReviewException("The Anki note identity changed. Check its association.");
            _ = await CallAsync("guiBrowse", Params(("query", new AnkiValue.Text("nid:" + id))), connection, cancellationToken);
            return null;
        }
        catch (Exception error) { return error.Message; }
    }
    internal async Task<AnkiReviewCatalog> FetchCatalogAsync(AnkiConnection connection, CancellationToken cancellationToken = default)
    {
        string profile = await ProfileAsync(connection, cancellationToken);
        var decks = Strings(await CallAsync("deckNames", Params(), connection, cancellationToken));
        var models = Strings(await CallAsync("modelNames", Params(), connection, cancellationToken));
        return new(profile, decks, models);
    }
    internal async Task<string> ProfileAsync(AnkiConnection connection, CancellationToken cancellationToken = default)
    {
        string? name = (await CallAsync("getActiveProfile", Params(), connection, cancellationToken)).String;
        return !string.IsNullOrEmpty(name) ? name : throw Invalid();
    }
    internal async Task VerifyScopeAsync(AnkiNoteBinding binding, AnkiConnection connection, CancellationToken cancellationToken = default)
    {
        if (!Same(binding.Endpoint, connection.Endpoint) || !Same(await ProfileAsync(connection, cancellationToken), binding.Profile))
            throw new AnkiReviewException("Switch to the Anki profile associated with this sense.");
    }
    internal async Task<IReadOnlyList<string>> FieldsAsync(string model, AnkiConnection connection, bool create,
        string? expectedProfile = null, CancellationToken cancellationToken = default)
    {
        if (Same(model, ReviewLayoutPolicy.Model))
        {
            var names = Strings(await CallAsync("modelNames", Params(), connection, cancellationToken));
            if (!names.Contains(model, CanonicalStringComparer.Instance))
            {
                if (!create) return System.Array.AsReadOnly(new[] { "LingroveID", "Front", "Back" });
                if (expectedProfile is not null && (!Settings.Connection().Equals(connection) || !Same(await ProfileAsync(connection, cancellationToken), expectedProfile))) throw Conflict();
                _ = await CallAsync("createModel", Params(("modelName", new AnkiValue.Text(model)), ("inOrderFields", AnkiValue.List(["LingroveID", "Front", "Back"])),
                    ("css", new AnkiValue.Text(ReviewLayoutPolicy.Css)), ("isCloze", new AnkiValue.Boolean(false)),
                    ("cardTemplates", new AnkiValue.Sequence([AnkiValue.Strings(new Dictionary<string, string> { ["Name"] = "Card", ["Front"] = "{{Front}}", ["Back"] = "{{Back}}" })]))), connection, cancellationToken);
            }
        }
        if (Same(model, AnkiReviewPolicy.PresetName))
        {
            var names = Strings(await CallAsync("modelNames", Params(), connection, cancellationToken));
            if (!names.Contains(model, CanonicalStringComparer.Instance))
            {
                if (!create) return ReviewFieldNames();
                if (expectedProfile is not null && (!Settings.Connection().Equals(connection) || !Same(await ProfileAsync(connection, cancellationToken), expectedProfile))) throw Conflict();
                _ = await CallAsync("createModel", Params(("modelName", new AnkiValue.Text(model)), ("inOrderFields", AnkiValue.List(ReviewFieldNames())),
                    ("css", new AnkiValue.Text(AnkiNoteTemplate.Css)), ("isCloze", new AnkiValue.Boolean(false)),
                    ("cardTemplates", new AnkiValue.Sequence([Template("Recognition", AnkiNoteTemplate.RecognitionFront, AnkiNoteTemplate.RecognitionBack), Template("Gap", AnkiNoteTemplate.GapFront, AnkiNoteTemplate.GapBack)]))), connection, cancellationToken);
            }
        }
        return Strings(await CallAsync("modelFieldNames", Params(("modelName", new AnkiValue.Text(model))), connection, cancellationToken));
    }
    internal async Task<AnkiRemoteNote?> NoteAsync(long id, AnkiConnection connection, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("notesInfo", Params(("notes", new AnkiValue.Sequence([new AnkiValue.Number(id)]))), connection, cancellationToken);
        if (result.Array is not { Count: 1 } rows || rows[0].Object is not { } row) throw Invalid();
        if (row.Count == 0) return null;
        if (row.GetValueOrDefault("noteId")?.Integer != id || row.GetValueOrDefault("modelName")?.String is not { } model || row.GetValueOrDefault("fields")?.Object is not { } raw) throw Invalid();
        var fields = new Dictionary<string, string>(CanonicalStringComparer.Instance);
        foreach (var field in raw)
        {
            if (field.Value.Object?.GetValueOrDefault("value")?.String is not { } value) throw Invalid();
            fields.Add(field.Key, value);
        }
        return new(id, model, fields);
    }
    internal Task<AnkiValue> CallAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Client.PerformAsync(action, parameters, connection, cancellationToken); }
    internal static IReadOnlyList<string> Strings(AnkiValue value)
    {
        if (value.Array is not { } rows || rows.Any(r => r.String is null)) throw Invalid();
        return System.Array.AsReadOnly(rows.Select(r => r.String!).ToArray());
    }
    internal static AnkiReviewException Invalid() => new("AnkiConnect returned an invalid response.");
    internal void Begin(Guid id, Guid? cardId = null)
    {
        if (SendingSenseId is not null || UpdatingTemplate) throw new AnkiReviewException("Wait for the current Anki operation to finish.");
        Guid key = cardId ?? id; SendingSenseId = id; SendingReviewId = key; _failures.Remove(key); Changed();
    }
    internal void Finish(Guid id, Guid? cardId = null, Exception? error = null, bool missingNote = false, bool wrote = true)
    {
        Guid key = cardId ?? id;
        if (SendingReviewId == key) { SendingSenseId = null; SendingReviewId = null; }
        if (error is null or OperationCanceledException) _failures.Remove(key); else _failures[key] = error.Message;
        if (error is null) { if (!wrote) _unresolved.Add(key); else _unresolved.Remove(key); }
        if (missingNote) _missing.Add(key); else if (error is null) _missing.Remove(key);
        Changed();
    }
    private static AnkiValue Template(string name, string front, string back) => AnkiValue.Strings(new Dictionary<string, string> { ["Name"] = name, ["Front"] = front, ["Back"] = back });
    private static IReadOnlyList<string> ReviewFieldNames() => System.Array.AsReadOnly(Enum.GetValues<ReviewField>().Select(field => System.Text.Json.JsonSerializer.SerializeToElement(field).GetString()!).ToArray());
    private static IReadOnlyDictionary<string, AnkiValue> Params(params (string Name, AnkiValue Value)[] fields) => new ReadOnlyDictionary<string, AnkiValue>(fields.ToDictionary(p => p.Name, p => p.Value, CanonicalStringComparer.Instance));
    private static bool Same(string? a, string? b) => CanonicalStringComparer.Instance.Equals(a, b);
    private static bool SameMap<K>(IReadOnlyDictionary<K, string> a, IReadOnlyDictionary<K, string> b) where K : notnull => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var value) && Same(p.Value, value));
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
}
