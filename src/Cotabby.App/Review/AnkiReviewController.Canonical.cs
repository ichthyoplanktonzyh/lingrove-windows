using Cotabby.Core.Review;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Review;

/// <summary>Canonical sessions borrow the same connection owner and operation lease. The legacy
/// notebook guard remains on legacy entry points and is never changed by this adapter.</summary>
internal sealed partial class AnkiReviewController
{
    internal Task<ReviewOperationScope> CanonicalScopeAsync(CancellationToken token) => OperationScopeCoreAsync(token);
    internal Task CanonicalVerifyAsync(ReviewOperationScope scope, CancellationToken token) => VerifyCoreAsync(scope, token);
    internal Task<IReadOnlyList<string>> CanonicalFieldsAsync(string model, AnkiConnection connection, bool create, string? profile, CancellationToken token, Func<Task>? beforeWrite = null) => FieldsCoreAsync(model, connection, create, profile, token, beforeWrite);
    internal Task<AnkiRemoteNote?> CanonicalNoteAsync(long id, AnkiConnection connection, CancellationToken token) => NoteCoreAsync(id, connection, token);
    internal void BeginCanonical(Guid senseId, Guid? cardId) => BeginCore(senseId, cardId);
    internal Task UpdateCanonicalPresetTemplateAsync(CancellationToken token, Func<Task> beforeWrite) => UpdatePresetTemplateCoreAsync(token, beforeWrite);
    internal async Task VerifyCanonicalBindingAsync(F.AnkiNoteBinding binding, AnkiConnection connection, CancellationToken token)
    {
        if (!Same(binding.Endpoint, connection.Endpoint) || !Same(await ProfileAsync(connection, token), binding.Profile))
            throw new AnkiReviewException(AnkiReviewFailure.ProfileMismatch);
    }
    internal string CanonicalStatus(F.VocabularyEntry entry, F.VocabularySense sense, Guid cardId)
    {
        if (SendingReviewId == cardId) return "Sending…";
        if (_failures.ContainsKey(cardId)) return "Send failed";
        if (_unresolved.Contains(cardId)) return "Anki fields changed";
        if (_missing.Contains(cardId)) return "Anki note missing";
        var review = sense.ReviewCards?.FirstOrDefault(value => value.Plan.Id == cardId);
        if (review is null) return "Not added";
        if (review.NeedsBindingChoice) return "Choose an Anki association";
        var binding = review.ActiveBinding;
        if (binding is null) return "Not added";
        if (binding.NoteId is null) return "Ready to send";
        try
        {
            var content = CanonicalReviewCardPolicy.Content(entry, sense, review.Plan);
            if (!AnkiReviewPolicy.CurrentAudio(review.Plan, content)) return "Update available";
            var fields = AnkiReviewPolicy.Fields(content, binding.OriginSenseId, binding.Mapping, review.Plan.FieldOverrides, AnkiReviewPolicy.AudioFields(review.Plan));
            return SameMap(fields, binding.Fields) ? "Synced" : "Update available";
        }
        catch { return "Review content needs attention"; }
    }
    private async Task EnsureNavigationCurrentAsync(AnkiConnection connection, CancellationToken token, Func<CancellationToken, Task<bool>> current)
    {
        if (!Settings.Connection().Equals(connection) || !await current(token) || !Settings.Connection().Equals(connection)) throw Conflict();
    }
    private async Task VerifyNavigationAsync(F.AnkiNoteBinding binding, AnkiConnection connection, CancellationToken token, Func<CancellationToken, Task<bool>> current)
    {
        await EnsureNavigationCurrentAsync(connection, token, current);
        await VerifyCanonicalBindingAsync(binding, connection, token);
        await EnsureNavigationCurrentAsync(connection, token, current);
    }
    private async Task<long> NavigationNoteAsync(F.AnkiNoteBinding binding, AnkiConnection connection, CancellationToken token, Func<CancellationToken, Task<bool>> current)
    {
        await VerifyNavigationAsync(binding, connection, token, current);
        if (binding.NoteId is not { } id) throw Invalid();
        var note = await CanonicalNoteAsync(id, connection, token);
        await EnsureNavigationCurrentAsync(connection, token, current);
        if (note is null) throw new AnkiReviewException(AnkiReviewFailure.MissingNote);
        if (!Same(note.Model, binding.Model) || !Same(note.Fields.GetValueOrDefault(binding.IdentityField), binding.OriginSenseId.ToString("D")))
            throw new AnkiReviewException(AnkiReviewFailure.NoteIdentityChanged);
        await VerifyNavigationAsync(binding, connection, token, current); return id;
    }
    internal async Task<string?> OpenCanonicalAsync(F.AnkiNoteBinding binding, Func<CancellationToken, Task<bool>> current, CancellationToken token = default)
    {
        try
        {
            var connection = Settings.Connection(); long id = await NavigationNoteAsync(binding, connection, token, current);
            _ = await CallAsync("guiBrowse", Params(("query", new AnkiValue.Text("nid:" + id))), connection, token); return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return DisplayFailure(error); }
    }
    internal async Task<string?> ReviewCanonicalDeckAsync(F.AnkiNoteBinding binding, Func<CancellationToken, Task<bool>> current, CancellationToken token = default)
    {
        try
        {
            var connection = Settings.Connection(); long id = await NavigationNoteAsync(binding, connection, token, current);
            var ids = await CallAsync("findCards", Params(("query", new AnkiValue.Text("nid:" + id))), connection, token);
            await EnsureNavigationCurrentAsync(connection, token, current);
            if (ids.Array is not { Count: > 0 } cards) throw Invalid();
            var info = await CallAsync("cardsInfo", Params(("cards", new AnkiValue.Sequence(cards))), connection, token);
            await EnsureNavigationCurrentAsync(connection, token, current);
            if (info.Array?.FirstOrDefault()?.Object?.GetValueOrDefault("deckName")?.String is not { } deck) throw Invalid();
            var decks = Strings(await CallAsync("deckNames", Params(), connection, token));
            await EnsureNavigationCurrentAsync(connection, token, current);
            if (!decks.Contains(deck, Cotabby.Core.Text.CanonicalStringComparer.Instance)) throw Invalid();
            await VerifyNavigationAsync(binding, connection, token, current);
            if (await CallAsync("guiDeckOverview", Params(("name", new AnkiValue.Text(deck))), connection, token) is not AnkiValue.Boolean { Value: true }) throw Invalid();
            await EnsureNavigationCurrentAsync(connection, token, current);
            _activateAnki(); return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return DisplayFailure(error); }
    }

}
