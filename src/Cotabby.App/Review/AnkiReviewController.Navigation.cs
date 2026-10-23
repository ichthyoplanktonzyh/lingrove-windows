using Cotabby.Core.Review;

namespace Cotabby.App.Review;

/// <summary>Explicit navigation and guarded template upgrades share the controller's transport
/// and operation lease. Navigation activates only through its injected existing-app callback.
/// Template updates preserve card identities/schedules and refuse user-customized templates.</summary>
internal sealed partial class AnkiReviewController
{
    internal async Task OpenReviewFromMenuAsync(CancellationToken cancellationToken = default)
    {
        if (_openingMenuReview) return;
        _openingMenuReview = true; ConnectionMessage = null; Changed();
        try
        {
            if (await ReviewDeckAsync(cancellationToken: cancellationToken) is { } message)
            { ConnectionMessage = message; Changed(); Configure?.Invoke(); }
        }
        finally { _openingMenuReview = false; }
    }
    internal async Task<string?> ReviewDeckAsync(AnkiNoteBinding? binding = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = Settings.Connection(); string deck = Settings.Deck;
            if (binding is not null)
            {
                await VerifyScopeAsync(binding, connection, cancellationToken);
                if (binding.NoteId is not { } id) throw Invalid();
                var note = await NoteAsync(id, connection, cancellationToken);
                if (note is null || !Same(note.Fields.GetValueOrDefault(binding.IdentityField), binding.OriginSenseId.ToString("D").ToLowerInvariant())) throw Invalid();
                var ids = await CallAsync("findCards", Params(("query", new AnkiValue.Text("nid:" + id))), connection, cancellationToken);
                if (ids.Array is not { Count: > 0 } cards) throw Invalid();
                var info = await CallAsync("cardsInfo", Params(("cards", new AnkiValue.Sequence(cards))), connection, cancellationToken);
                if (info.Array?.FirstOrDefault()?.Object?.GetValueOrDefault("deckName")?.String is not { } currentDeck) throw Invalid();
                deck = currentDeck; await VerifyScopeAsync(binding, connection, cancellationToken);
            }
            if (!Strings(await CallAsync("deckNames", Params(), connection, cancellationToken)).Contains(deck, Cotabby.Core.Text.CanonicalStringComparer.Instance)
                || await CallAsync("guiDeckOverview", Params(("name", new AnkiValue.Text(deck))), connection, cancellationToken) is not AnkiValue.Boolean { Value: true })
                throw new AnkiReviewException("Choose a deck and an Anki association.");
            _activateAnki(); return null;
        }
        catch (Exception error) { return error.Message; }
    }
    internal async Task UpdatePresetTemplateAsync(CancellationToken cancellationToken = default)
    {
        if (SendingSenseId is not null || UpdatingTemplate) throw Conflict();
        UpdatingTemplate = true;
        try
        {
            var connection = Settings.Connection(); string profile = await ProfileAsync(connection, cancellationToken);
            var fields = await FieldsAsync(AnkiReviewPolicy.PresetName, connection, create: true, cancellationToken: cancellationToken);
            var templates = await CallAsync("modelTemplates", Params(("modelName", new AnkiValue.Text(AnkiReviewPolicy.PresetName))), connection, cancellationToken);
            if (!Equals(templates, new AnkiValue.Map(AnkiNoteTemplate.LegacyTemplates)) && !Equals(templates, new AnkiValue.Map(AnkiNoteTemplate.Templates)))
                throw new AnkiReviewException("This template was customized. Manage it in Anki.");
            foreach (string field in ReviewFieldNames().Where(f => !fields.Contains(f, Cotabby.Core.Text.CanonicalStringComparer.Instance)))
            {
                if (!Same(await ProfileAsync(connection, cancellationToken), profile)) throw Conflict();
                _ = await CallAsync("modelFieldAdd", Params(("modelName", new AnkiValue.Text(AnkiReviewPolicy.PresetName)), ("fieldName", new AnkiValue.Text(field))), connection, cancellationToken);
            }
            if (!Same(await ProfileAsync(connection, cancellationToken), profile)) throw Conflict();
            _ = await CallAsync("updateModelTemplates", Params(("model", new AnkiValue.Map(Params(("name", new AnkiValue.Text(AnkiReviewPolicy.PresetName)), ("templates", new AnkiValue.Map(AnkiNoteTemplate.Templates)))))), connection, cancellationToken);
            var style = await CallAsync("modelStyling", Params(("modelName", new AnkiValue.Text(AnkiReviewPolicy.PresetName))), connection, cancellationToken);
            if (Same(style.Object?.GetValueOrDefault("css")?.String, AnkiNoteTemplate.LegacyCss))
            {
                if (!Same(await ProfileAsync(connection, cancellationToken), profile)) throw Conflict();
                _ = await CallAsync("updateModelStyling", Params(("model", new AnkiValue.Map(Params(("name", new AnkiValue.Text(AnkiReviewPolicy.PresetName)), ("css", new AnkiValue.Text(AnkiNoteTemplate.Css)))))), connection, cancellationToken);
            }
            if (!Same(await ProfileAsync(connection, cancellationToken), profile)
                || !Equals(await CallAsync("modelTemplates", Params(("modelName", new AnkiValue.Text(AnkiReviewPolicy.PresetName))), connection, cancellationToken), new AnkiValue.Map(AnkiNoteTemplate.Templates))) throw Invalid();
            await RefreshFieldsAsync(cancellationToken);
        }
        finally { UpdatingTemplate = false; }
    }
}
