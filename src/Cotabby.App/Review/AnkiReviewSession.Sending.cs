using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Review;

/// <summary>Send preserves a local pending identity before a remote note write, then records only
/// a verified receipt. Retries search the identity field, not display text. Scope/content checks
/// surround suspension points; a remote acknowledgement survives separately from the editable
/// draft so a lost reply or merged sense cannot make a retry create another note.</summary>
internal sealed partial class AnkiReviewSession
{
    internal async Task<bool> SendAsync(bool recreateMissing = false, ReviewOperationScope? expectedScope = null, CancellationToken cancellationToken = default)
    {
        if (Busy) return false;
        try { Controller.Begin(SenseId, CardId); }
        catch (Exception error) { Message = error.Message; Changed(); return false; }
        Busy = true; Message = null; Changed();
        try
        {
            _sendScope = expectedScope ?? await Controller.OperationScopeAsync(cancellationToken);
            await Controller.VerifyAsync(_sendScope, cancellationToken);
            bool result = await SynchronizeAsync(recreateMissing, cancellationToken);
            Controller.Finish(SenseId, CardId, wrote: result); return result;
        }
        catch (Exception error)
        {
            Message = error is OperationCanceledException ? null : error.Message;
            Controller.Finish(SenseId, CardId, error, MissingNote); return false;
        }
        finally { Busy = false; _sendScope = null; Changed(); }
    }
    private async Task<bool> SynchronizeAsync(bool recreateMissing, CancellationToken cancellationToken)
    {
        var content = SendingContent();
        if (_baseline?.NeedsBindingChoice == true && BindingId is null || Deck.Length == 0) throw new AnkiReviewException("Choose a deck and an Anki association.");
        await SavePlanAsync(cancellationToken);
        var scope = _sendScope ?? throw Conflict(); await Controller.VerifyAsync(scope, cancellationToken);
        var connection = scope.Connection; string profile = scope.Profile;
        var existing = Binding;
        if (existing is not null) await Controller.VerifyScopeAsync(existing, connection, cancellationToken);
        Guid identity = existing?.OriginSenseId ?? CardId ?? SenseId;
        string model = Model; var mapping = Mapping;
        var available = await Controller.FieldsAsync(model, connection, false, cancellationToken: cancellationToken);
        await ValidateImageTemplateAsync(content, connection, cancellationToken); ValidateAudioMapping();
        var audioFields = await UploadAudioAsync(content, connection, profile, cancellationToken);
        await UploadImagesAsync(content, connection, profile, cancellationToken);
        var desired = AnkiReviewPolicy.Fields(content, identity, mapping, Plan.FieldOverrides, audioFields);
        if (desired.Keys.Any(k => !Has(available, k)) || !mapping.TryGetValue(ReviewField.Identity, out string? identityField))
            throw new AnkiReviewException("The Anki note type does not contain the mapped fields.");
        if (existing is null)
        {
            var intent = AnkiNoteBinding.Create(identity, connection.Endpoint, profile, null, model, Deck, identityField, mapping, desired);
            await Controller.Store.RecordAnkiBindingAsync(EntryId, SenseId, intent, CardId);
            _baseline = Controller.Current(EntryId, SenseId).Sense.ReviewValue(CardId);
            SelectBinding(intent.Id); existing = intent;
        }
        var remote = await RemoteNoteAsync(existing, identity, identityField, model, recreateMissing, connection, cancellationToken);
        if (remote is not null)
        {
            if (!Same(remote.Model, model) || !Same(remote.Fields.GetValueOrDefault(identityField), identity.ToString("D")))
                throw new AnkiReviewException("The Anki note identity changed. Check its association.");
            var resolved = await ResolveAsync(remote, existing, desired, identityField, cancellationToken);
            if (resolved is null) return false; desired = resolved;
        }
        ValidateCurrent(content, connection);
        if (!Same(await Controller.ProfileAsync(connection, cancellationToken), profile)) throw Conflict();
        long noteId = await WriteAsync(remote, desired, content, profile, connection, model, cancellationToken);
        if (!Same(await Controller.ProfileAsync(connection, cancellationToken), profile)) throw new AnkiReviewException("Anki did not confirm the saved fields. Retry to check the note.");
        var verified = await Controller.NoteAsync(noteId, connection, cancellationToken);
        if (verified is null || !Same(verified.Model, model) || desired.Any(p => !Same(verified.Fields.GetValueOrDefault(p.Key), p.Value)))
            throw new AnkiReviewException("Anki did not confirm the saved fields. Retry to check the note.");
        var receipt = new AnkiNoteBinding(existing?.Id ?? Guid.NewGuid(), identity, connection.Endpoint, profile, noteId, model, Deck, identityField, mapping, desired, DateTimeOffset.Now);
        await Controller.Store.RecordAnkiBindingAsync(EntryId, SenseId, receipt, CardId);
        _baseline = Controller.Current(EntryId, SenseId).Sense.ReviewValue(CardId);
        RememberDefaults(scope); MissingNote = false; Conflicts = []; Changed(); return true;
    }
    private void RememberDefaults(ReviewOperationScope scope)
    {
        if (CardId is not null) return;
        var settings = Controller.Settings;
        if (Same(settings.Deck, _initialDefaultDeck) && Same(scope.DefaultDeck, _initialDefaultDeck)) settings.Deck = Deck;
        if (settings.DefaultRecognition == _initialDefaultRecognition && scope.DefaultRecognition == _initialDefaultRecognition) settings.DefaultRecognition = Plan.Recognition;
        if (settings.DefaultGap == _initialDefaultGap && scope.DefaultGap == _initialDefaultGap) settings.DefaultGap = Plan.Gap;
    }
    private ReviewCardContent SendingContent()
    {
        var source = Controller.Current(EntryId, SenseId); var content = ReviewCardPolicy.Content(source.Entry, source.Sense, Plan);
        if (Binding is { } binding && AnkiReviewPolicy.RemovesExistingCard(Plan, binding)) throw new AnkiReviewException("Manage existing card types in Anki.");
        return content;
    }
    private async Task<long> WriteAsync(AnkiRemoteNote? remote, IReadOnlyDictionary<string, string> desired, ReviewCardContent content, string profile,
        AnkiConnection connection, string model, CancellationToken cancellationToken)
    {
        if (remote is not null)
        {
            if (desired.Any(p => !Same(remote.Fields.GetValueOrDefault(p.Key), p.Value)))
            {
                await ValidateImageTemplateAsync(content, connection, cancellationToken);
                if (!Same(await Controller.ProfileAsync(connection, cancellationToken), profile)) throw Conflict();
                ValidateCurrent(content, connection);
                _ = await Controller.CallAsync("updateNoteFields", Params(("note", new AnkiValue.Map(Params(("id", new AnkiValue.Number(remote.Id)), ("fields", AnkiValue.Strings(desired)))))), connection, cancellationToken);
            }
            return remote.Id;
        }
        _ = await Controller.FieldsAsync(model, connection, true, profile, cancellationToken);
        ValidateCurrent(content, connection);
        if (!Same(await Controller.ProfileAsync(connection, cancellationToken), profile)) throw Conflict();
        await ValidateImageTemplateAsync(content, connection, cancellationToken);
        if (!Same(await Controller.ProfileAsync(connection, cancellationToken), profile)) throw Conflict();
        ValidateCurrent(content, connection);
        var options = new AnkiValue.Map(Params(("allowDuplicate", new AnkiValue.Boolean(!Same(model, AnkiReviewPolicy.PresetName) && !Same(model, ReviewLayoutPolicy.Model)))));
        var note = new AnkiValue.Map(Params(("deckName", new AnkiValue.Text(Deck)), ("modelName", new AnkiValue.Text(model)),
            ("fields", AnkiValue.Strings(desired)), ("options", options), ("tags", AnkiValue.List(["Lingrove"]))));
        var value = await Controller.CallAsync("addNote", Params(("note", note)), connection, cancellationToken);
        return value.Integer ?? throw AnkiReviewController.Invalid();
    }
    private async Task<IReadOnlyDictionary<string, string>?> ResolveAsync(AnkiRemoteNote remote, AnkiNoteBinding? existing,
        IReadOnlyDictionary<string, string> desired, string identityField, CancellationToken cancellationToken)
    {
        var changed = AnkiReviewPolicy.Conflicts(existing?.Fields ?? new Dictionary<string, string>(), desired, remote.Fields);
        if (changed.Count == 0) return desired;
        if (!Equals(_conflictRemote, remote) || changed.Any(c => !_keepRemote.ContainsKey(c.Id)))
        { Conflicts = changed; _keepRemote.Clear(); _conflictRemote = remote; Changed(); return null; }
        var result = new Dictionary<string, string>(desired, Cotabby.Core.Text.CanonicalStringComparer.Instance);
        var overrides = new Dictionary<string, string>(Plan.FieldOverrides, Cotabby.Core.Text.CanonicalStringComparer.Instance);
        foreach (var conflict in changed.Where(c => _keepRemote[c.Id]))
        {
            if (Same(conflict.Id, identityField)) throw Conflict(); overrides[conflict.Id] = conflict.Remote; result[conflict.Id] = conflict.Remote;
            Plan = Plan with { FieldOverrides = overrides };
        }
        await SavePlanAsync(cancellationToken); return result;
    }
    internal void ValidateCurrent(ReviewCardContent content, AnkiConnection connection)
    {
        var current = Controller.Current(EntryId, SenseId);
        if (!Equals(current.Sense.ReviewValue(CardId), _baseline) || _sendScope is null || !Controller.Matches(_sendScope)
            || !Controller.Settings.Connection().Equals(connection) || !Equals(ReviewCardPolicy.Content(current.Entry, current.Sense, Plan), content)) throw Conflict();
    }
    private async Task<AnkiRemoteNote?> RemoteNoteAsync(AnkiNoteBinding? existing, Guid identity, string field, string model,
        bool recreate, AnkiConnection connection, CancellationToken cancellationToken)
    {
        if (existing?.NoteId is { } id && await Controller.NoteAsync(id, connection, cancellationToken) is { } remote) return remote;
        if (existing?.NoteId is not null && !recreate) { MissingNote = true; throw new AnkiReviewException("The associated Anki note no longer exists."); }
        if (field.IndexOfAny(['"', '\\', ':']) >= 0) throw AnkiReviewController.Invalid();
        var matches = await Controller.CallAsync("findNotes", Params(("query", new AnkiValue.Text("\"" + field + ":" + identity.ToString("D") + "\""))), connection, cancellationToken);
        if (matches.Array is not { } rows || rows.Count > 32 || rows.Any(r => r.Integer is null)) throw AnkiReviewController.Invalid();
        var found = new List<AnkiRemoteNote>();
        foreach (var row in rows)
            if (row.Integer is { } noteId && await Controller.NoteAsync(noteId, connection, cancellationToken) is { } note && Same(note.Model, model) && Same(note.Fields.GetValueOrDefault(field), identity.ToString("D"))) found.Add(note);
        if (found.Count > 1) throw new AnkiReviewException("Several Anki notes use this sense ID. Resolve duplicates in Anki.");
        return found.FirstOrDefault();
    }
}
