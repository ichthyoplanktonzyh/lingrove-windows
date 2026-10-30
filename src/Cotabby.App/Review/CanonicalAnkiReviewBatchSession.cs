using System.ComponentModel;
using Cotabby.Core.Review;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Review;

/// <summary>One sheet owns a frozen queue of independent card IDs and a serial cancellable task.
/// Each attempt opens the existing canonical single-card session; the app Feature admits all work
/// and remains the only client lifetime owner. This class owns no Store or Anki transport.</summary>
internal sealed class CanonicalAnkiReviewBatchSession : INotifyPropertyChanged
{
    private readonly AnkiReviewFeature _feature;
    private readonly F.ICanonicalVocabularyStore _store;
    private readonly Dictionary<Guid, (F.VocabularySense Sense, string Expression)> _baselines = [];
    private ReviewBatchItem[] _items;
    private ReviewSpeechDisclosure? _presentedSpeech;
    internal void PresentAudioDisclosure(ReviewSpeechDisclosure? value) => _presentedSpeech = value;
    private CancellationTokenSource? _cancellation, _activeCancellation;
    internal IReadOnlyList<ReviewBatchItem> Items => Array.AsReadOnly(_items);
    internal int EmptySenseCount { get; }
    internal int CompleteCount => _items.Count(item => item.Status == ReviewBatchStatus.Synced);
    internal bool Busy { get; private set; }
    internal string? Message { get; private set; }
    internal Task? Work { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
    private CanonicalAnkiReviewBatchSession(AnkiReviewFeature feature, F.ICanonicalVocabularyStore store, F.FrozenVocabularyImportSnapshot snapshot, IReadOnlyList<VocabularySelection> selection)
    {
        _feature = feature; _store = store;
        var unique = selection.Distinct().ToArray();
        EmptySenseCount = unique.Count(value => Resolve(snapshot, value, null)?.Sense.ReviewCards?.Count is null or 0);
        _items = unique.SelectMany(value =>
        {
            var current = Resolve(snapshot, value, null);
            return (current?.Sense.ReviewCards ?? []).Select(card =>
            {
                var item = new ReviewBatchItem(value, current!.Value.Entry.Expression, card.Plan.Name ?? DisplayTitle(current.Value.Sense)) { CardId = card.Plan.Id };
                if (feature.Controller.CanonicalStatus(current.Value.Entry, current.Value.Sense, card.Plan.Id) == "Synced")
                    item = item with { Status = card.Plan.AudioKinds?.Count > 0 || card.Plan.Layout?.AudioKinds.Count > 0 ? ReviewBatchStatus.VerifyAudio : ReviewBatchStatus.Synced };
                return item;
            });
        }).ToArray();
    }
    internal static async Task<CanonicalAnkiReviewBatchSession> OpenAsync(AnkiReviewFeature feature, F.ICanonicalVocabularyStore store, IReadOnlyList<VocabularySelection> selection, CancellationToken token = default)
    {
        var frozen = selection.ToArray(); F.FrozenVocabularyImportSnapshot? snapshot = null;
        await feature.RunAsync(async featureToken => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(featureToken, token); snapshot = await store.ReadAsync(linked.Token); });
        return new(feature, store, snapshot!, frozen);
    }
    private static string DisplayTitle(F.VocabularySense sense)
    {
        if (sense.Meaning.Length > 0) return sense.Meaning;
        foreach (var block in sense.Content ?? []) if (block.Content is VocabularyBlockContent.Text text && FoundationText.TrimWhitespaceAndNewlines(text.Value.Text).Length > 0) return text.Value.Text;
        if (sense.Examples.FirstOrDefault(example => example.Meaning.Length > 0) is { } example) return example.Meaning;
        return VocabularyContentPolicy.Images(sense.Content ?? []).Count > 0 ? "Image explanation" : "Meaning pending";
    }
    private static (F.VocabularyEntry Entry, F.VocabularySense Sense)? Resolve(F.FrozenVocabularyImportSnapshot snapshot, VocabularySelection selection, Guid? card)
    {
        var entry = snapshot.Document.CollectedEntries.FirstOrDefault(value => value.Id == selection.EntryId);
        var sense = entry?.Senses.FirstOrDefault(value => value.Id == selection.SenseId || value.MergedFrom.Contains(selection.SenseId));
        return entry is not null && sense is not null && (card is null || sense.ReviewCards?.Any(value => value.Plan.Id == card) == true) ? (entry, sense) : null;
    }
    private async Task<(F.VocabularyEntry Entry, F.VocabularySense Sense)?> ResolveAsync(ReviewBatchItem item, CancellationToken token)
        => Resolve(await _store.ReadAsync(token), item.Selection, item.CardId);
    private void Replace(int index, ReviewBatchItem item) { var copy = (ReviewBatchItem[])_items.Clone(); copy[index] = item; _items = copy; Changed(); }
    internal async Task<CanonicalAnkiReviewSession?> PrepareAsync(Guid id, CancellationToken token = default)
    {
        var item = _items.FirstOrDefault(value => value.Id == id); if (item?.CardId is not { } card) return null;
        CanonicalAnkiReviewSession? result = null;
        await _feature.RunAsync(async featureToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, featureToken);
            var source = await ResolveAsync(item, linked.Token); if (source is null) { int index = Array.FindIndex(_items, value => value.Id == id); if (index >= 0) Replace(index, _items[index] with { Status = ReviewBatchStatus.Unavailable }); return; }
            _baselines[id] = (source.Value.Sense, source.Value.Entry.Expression);
            result = await CanonicalAnkiReviewSession.OpenAsync(_feature.Controller, _store, source.Value.Entry.Id, source.Value.Sense.Id, card, linked.Token);
            var opened = Resolve(result.Snapshot, item.Selection, card);
            if (opened is null)
            {
                result.Dispose(); result = null; _baselines.Remove(id);
                int index = Array.FindIndex(_items, value => value.Id == id); if (index >= 0) Replace(index, _items[index] with { Status = ReviewBatchStatus.Unavailable });
            }
            else _baselines[id] = (opened.Value.Sense, opened.Value.Entry.Expression);
        });
        return result;
    }
    internal async Task RefreshAsync(Guid id, CancellationToken token = default)
    {
        int index = Array.FindIndex(_items, item => item.Id == id); if (index < 0) return;
        await _feature.RunAsync(async featureToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, featureToken);
            var source = await ResolveAsync(_items[index], linked.Token); if (source is null) { Replace(index, _items[index] with { Status = ReviewBatchStatus.Unavailable }); return; }
            if (_baselines.Remove(id, out var before) && before.Sense == source.Value.Sense && CanonicalStringComparer.Instance.Equals(before.Expression, source.Value.Entry.Expression)) return;
            var card = source.Value.Sense.ReviewCards!.First(value => value.Plan.Id == _items[index].CardId);
            Replace(index, _items[index] with { Expression = source.Value.Entry.Expression, Meaning = card.Plan.Name ?? DisplayTitle(source.Value.Sense),
                Status = _feature.Controller.CanonicalStatus(source.Value.Entry, source.Value.Sense, card.Plan.Id) == "Synced" ? ReviewBatchStatus.Synced : ReviewBatchStatus.Ready, Message = null });
        });
    }
    internal void Start(ReviewOperationScope scope, Guid? only = null) { var shown = _presentedSpeech; StartOwned(token => RunAsync(scope, only, shown, token)); }
    internal void StartApply(string deck) => StartOwned(token => ApplyAsync(deck, token));
    private void StartOwned(Func<CancellationToken, Task> action)
    {
        if (Busy) return;
        var cancellation = new CancellationTokenSource(); _cancellation = cancellation; Busy = true;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Work = Execute(); try { Changed(); } finally { start.TrySetResult(); }
        async Task Execute()
        {
            await start.Task;
            try { await _feature.RunAsync(async featureToken => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(featureToken, cancellation.Token); _activeCancellation = linked; try { await action(linked.Token); } finally { _activeCancellation = null; } }); }
            catch (Exception error) { FailOutstanding(error); }
            finally { cancellation.Dispose(); _cancellation = null; Work = null; Busy = false; Changed(); }
        }
    }
    internal Task StopAsync()
    {
        // CancelAsync marks its own token immediately but linked registrations run later.
        // Mark the active token itself before returning so a just-resumed remote call cannot
        // race ahead of propagation and report a not-yet-verified note as successful.
        var active = _activeCancellation?.CancelAsync() ?? Task.CompletedTask;
        var owner = _cancellation?.CancelAsync() ?? Task.CompletedTask;
        return Task.WhenAll(active, owner);
    }
    private void FailOutstanding(Exception error)
    {
        Message = error is OperationCanceledException ? null : _feature.Controller.DisplayFailure(error); Changed();
        for (int i = 0; i < _items.Length; i++) if (_items[i].Status is ReviewBatchStatus.Sending or ReviewBatchStatus.Ready or ReviewBatchStatus.VerifyAudio)
            Replace(i, _items[i] with { Status = error is OperationCanceledException ? ReviewBatchStatus.Cancelled : ReviewBatchStatus.Failed, Message = Message });
    }
    private async Task ApplyAsync(string deck, CancellationToken token)
    {
        for (int i = 0; i < _items.Length; i++)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var source = await ResolveAsync(_items[i], token); if (source is null) { Replace(i, _items[i] with { Status = ReviewBatchStatus.Unavailable }); continue; }
                using var session = await CanonicalAnkiReviewSession.OpenAsync(_feature.Controller, _store, source.Value.Entry.Id, source.Value.Sense.Id, _items[i].CardId, token);
                if (Resolve(session.Snapshot, _items[i].Selection, _items[i].CardId) is null) { Replace(i, _items[i] with { Status = ReviewBatchStatus.Unavailable }); continue; }
                await session.LoadMetadataAsync(token); token.ThrowIfCancellationRequested();
                if (FoundationText.TrimWhitespaceAndNewlines(deck).Length > 0 && session.Binding?.NoteId is null) session.Deck = deck;
                if (await session.SaveAsync(token))
                {
                    // A committed save remains acknowledged even if cancellation arrives afterwards.
                    var fresh = await ResolveAsync(_items[i], CancellationToken.None);
                    if (fresh is not null) Replace(i, _items[i] with { Status = _feature.Controller.CanonicalStatus(fresh.Value.Entry, fresh.Value.Sense, _items[i].CardId!.Value) == "Synced" ? ReviewBatchStatus.Synced : ReviewBatchStatus.Ready, Message = null });
                }
                else if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                else Replace(i, _items[i] with { Status = ReviewBatchStatus.Failed, Message = session.Message });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                for (int remaining = i; remaining < _items.Length; remaining++)
                    if (_items[remaining].Status is not (ReviewBatchStatus.Synced or ReviewBatchStatus.Skipped))
                        Replace(remaining, _items[remaining] with { Status = ReviewBatchStatus.Cancelled, Message = null });
                return;
            }
            catch (Exception error) { Replace(i, _items[i] with { Status = ReviewBatchStatus.Failed, Message = _feature.Controller.DisplayFailure(error) }); }
        }
    }
    private async Task RunAsync(ReviewOperationScope scope, Guid? only, ReviewSpeechDisclosure? shown, CancellationToken token)
    {
        Message = null; Changed();
        await _feature.Controller.CanonicalVerifyAsync(scope, token);
        var decks = AnkiReviewController.Strings(await _feature.Controller.CallAsync("deckNames", new Dictionary<string, AnkiValue>(), scope.Connection, token));
        string defaultDeck = !scope.HadSavedDeck && !decks.Contains(scope.DefaultDeck, CanonicalStringComparer.Instance) ? decks.FirstOrDefault() ?? scope.DefaultDeck : scope.DefaultDeck;
        await _feature.Controller.CanonicalVerifyAsync(scope, token);
        var seen = new HashSet<Guid>();
        foreach (var item in _items.Where(item => item.Status == ReviewBatchStatus.Synced && item.CardId is not null))
            if (await ResolveAsync(item, token) is not null) seen.Add(item.CardId!.Value);
        for (int i = 0; i < _items.Length; i++)
        {
            if (only is { } id && _items[i].Id != id || _items[i].Status is ReviewBatchStatus.Synced or ReviewBatchStatus.Skipped) continue;
            token.ThrowIfCancellationRequested(); var source = await ResolveAsync(_items[i], token);
            if (source is null) { Replace(i, _items[i] with { Status = ReviewBatchStatus.Unavailable }); continue; }
            Guid cardId = _items[i].CardId!.Value; if (!seen.Add(cardId)) { Replace(i, _items[i] with { Status = ReviewBatchStatus.Skipped }); continue; }
            await _feature.Controller.CanonicalVerifyAsync(scope, token); Replace(i, _items[i] with { Status = ReviewBatchStatus.Sending, Message = null });
            try
            {
                using var session = await CanonicalAnkiReviewSession.OpenAsync(_feature.Controller, _store, source.Value.Entry.Id, source.Value.Sense.Id, cardId, token);
                session.PresentAudioDisclosure(shown);
                var opened = Resolve(session.Snapshot, _items[i].Selection, cardId);
                if (opened is null) { Replace(i, _items[i] with { Status = ReviewBatchStatus.Unavailable }); continue; }
                var saved = opened.Value.Sense.ReviewCards!.First(card => card.Plan.Id == cardId);
                if (saved.ActiveBinding is null && saved.Plan.Deck is null) session.Deck = defaultDeck;
                await session.LoadMetadataAsync(token); token.ThrowIfCancellationRequested();
                if (await session.ValidationMessageAsync(token) is { } issue) { Replace(i, _items[i] with { Status = ReviewBatchStatus.Attention, Message = issue }); continue; }
                bool success = await session.SendAsync(expectedScope: scope, cancellationToken: token);
                Replace(i, _items[i] with { Status = success ? ReviewBatchStatus.Synced : token.IsCancellationRequested ? ReviewBatchStatus.Cancelled : session.Conflicts.Count > 0 ? ReviewBatchStatus.Attention : ReviewBatchStatus.Failed,
                    Message = success || token.IsCancellationRequested ? null : session.Message ?? "Resolve this item's Anki field changes in preparation." });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { Replace(i, _items[i] with { Status = ReviewBatchStatus.Failed, Message = _feature.Controller.DisplayFailure(error) }); }
        }
    }
}
