using System.ComponentModel;
using Cotabby.Core.Review;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Review;

/// <summary>One UI-context batch sheet owns a frozen queue and a single cancellable serial task.
/// Each attempt reconstructs the existing single-sense transaction from durable Store state.
/// Synced rows stay excluded from retries; failure/conflict does not stop other rows. The sheet
/// must stop and join Work before releasing its borrowed controller/Store. This owner performs
/// no independent note protocol, media IO or implicit rollback.</summary>
internal sealed class AnkiReviewBatchSession : INotifyPropertyChanged
{
    internal AnkiReviewController Controller { get; }
    internal int EmptySenseCount { get; }
    private ReviewBatchItem[] _items;
    internal IReadOnlyList<ReviewBatchItem> Items => Array.AsReadOnly(_items);
    internal bool Busy { get; private set; }
    internal string? Message { get; private set; }
    internal Task? Work { get; private set; }
    private CancellationTokenSource? _cancellation;
    private readonly Dictionary<Guid, (VocabularySense Sense, string Expression)> _preparationBaselines = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new(null));

    internal AnkiReviewBatchSession(AnkiReviewController controller, IReadOnlyList<VocabularySelection> selections, bool flexible = false)
    {
        Controller = controller;
        var entries = controller.Store.Entries;
        VocabularyEntry? Entry(VocabularySelection selection) => entries.FirstOrDefault(entry => entry.Id == selection.EntryId);
        static VocabularySense? Sense(VocabularyEntry? entry, VocabularySelection selection) => entry is not null && VocabularySensePolicy.Index(selection.SenseId, entry) is { } index ? entry.Senses[index] : null;
        EmptySenseCount = flexible ? selections.Distinct().Count(selection => Sense(Entry(selection), selection)?.ReviewCards?.Count is null or 0) : 0;
        // Serial sends follow the first occurrence in the original frozen selection order.
        var seen = new HashSet<VocabularySelection>();
        _items = selections.Where(seen.Add).SelectMany(selection =>
        {
            // A missing sense still retains its entry's display expression in the source.
            // Read the immutable graph once, separately retaining these two optional levels.
            var entry = Entry(selection); var sense = Sense(entry, selection);
            IEnumerable<Guid?> ids = flexible ? (sense?.ReviewCards ?? []).Select(card => (Guid?)card.Plan.Id) : [null];
            return ids.Select(cardId =>
            {
                var review = sense?.ReviewValue(cardId);
                var item = new ReviewBatchItem(selection, entry?.Expression ?? "", review?.Plan.Name ?? (sense is null ? "" : VocabularyNotebookPolicy.DisplayTitle(sense))) { CardId = cardId };
                if (entry is not null && sense is not null && Controller.IsSynchronized(entry, sense, cardId))
                    item = item with { Status = review?.Plan.AudioKinds?.Count > 0 ? ReviewBatchStatus.VerifyAudio : ReviewBatchStatus.Synced };
                return item;
            });
        }).ToArray();
    }
    internal int CompleteCount => _items.Count(item => item.Status == ReviewBatchStatus.Synced);
    private (VocabularyEntry Entry, VocabularySense Sense)? Find(VocabularySelection selection)
    {
        var entry = Controller.Store.Entries.FirstOrDefault(entry => entry.Id == selection.EntryId);
        return entry is not null && VocabularySensePolicy.Index(selection.SenseId, entry) is { } index ? (entry, entry.Senses[index]) : null;
    }
    internal (VocabularyEntry Entry, VocabularySense Sense)? Resolve(ReviewBatchItem item)
    {
        var source = Find(item.Selection);
        return source is { } current && (item.CardId is null || current.Sense.ReviewValue(item.CardId) is not null) ? current : null;
    }
    internal void BeginPreparation(Guid id)
    {
        if (_items.FirstOrDefault(item => item.Id == id) is { } item && Resolve(item) is { } source)
            _preparationBaselines[id] = (source.Sense, source.Entry.Expression);
    }
    internal void Refresh(Guid id)
    {
        int index = Array.FindIndex(_items, item => item.Id == id);
        if (index < 0 || Resolve(_items[index]) is not { } source) return;
        if (_preparationBaselines.Remove(id, out var before) && before.Sense == source.Sense && CanonicalStringComparer.Instance.Equals(before.Expression, source.Entry.Expression)) return;
        Replace(index, _items[index] with
        {
            Expression = source.Entry.Expression, Meaning = source.Sense.ReviewValue(_items[index].CardId)?.Plan.Name ?? VocabularyNotebookPolicy.DisplayTitle(source.Sense),
            Status = Controller.IsSynchronized(source.Entry, source.Sense, _items[index].CardId) ? ReviewBatchStatus.Synced : ReviewBatchStatus.Ready, Message = null,
        });
    }
    private void Replace(int index, ReviewBatchItem value)
    {
        var next = (ReviewBatchItem[])_items.Clone(); next[index] = value; _items = next; Changed();
    }
    internal void Start(Guid? only = null) => StartOwned(token => RunAsync(only, token));
    internal void StartApply(string deck, int cards, int audio) => StartOwned(token => ApplyAsync(deck, cards, audio, token));
    private void StartOwned(Func<CancellationToken, Task> operation)
    {
        if (Busy) return;
        Busy = true; var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Work = ExecuteAsync(); Changed(); admitted.SetResult();
        async Task ExecuteAsync()
        {
            // Reserve the handle before allowing execution, like Task creation on MainActor.
            // Task.Yield alone cannot guarantee this ordering without a UI context; this gate
            // protects assignment while the caller still owns UI-context serialization.
            await admitted.Task;
            try { await operation(cancellation.Token); }
            finally { cancellation.Dispose(); _cancellation = null; Work = null; Busy = false; Changed(); }
        }
    }
    // CancelAsync marks cancellation immediately and lets the UI observe callback failures;
    // awaiting it does not replace joining Work, which owns the accepted transaction lifetime.
    internal Task StopAsync() => _cancellation?.CancelAsync() ?? Task.CompletedTask;
    private async Task ApplyAsync(string deck, int cards, int audio, CancellationToken cancellationToken)
    {
        for (int index = 0; index < _items.Length; index++)
        {
            if (cancellationToken.IsCancellationRequested) { Replace(index, _items[index] with { Status = ReviewBatchStatus.Cancelled }); continue; }
            if (Resolve(_items[index]) is not { } source) { Replace(index, _items[index] with { Status = ReviewBatchStatus.Unavailable }); continue; }
            var session = new AnkiReviewSession(Controller, source.Entry, source.Sense, cardId: _items[index].CardId);
            await session.LoadAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) { Replace(index, _items[index] with { Status = ReviewBatchStatus.Cancelled }); continue; }
            ApplyOptions(session, deck, cards, audio);
            if (await session.SaveAsync(cancellationToken)) Refresh(_items[index].Id);
            else Replace(index, _items[index] with { Status = ReviewBatchStatus.Failed, Message = session.Message });
        }
    }
    private static void ApplyOptions(AnkiReviewSession session, string deck, int cards, int audio)
    {
        if (FoundationText.TrimWhitespaceAndNewlines(deck).Length > 0 && session.Binding?.NoteId is null) session.Deck = deck;
        if (session.CardId is not null) return;
        if (cards > 0) session.Plan = session.Plan with { Recognition = cards != 2, Gap = cards != 1 };
        if (audio > 0) session.Plan = session.Plan with { AudioKinds = audio switch
        {
            2 => new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression }, 3 => new HashSet<ReviewAudioKind> { ReviewAudioKind.Sentence },
            4 => new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression, ReviewAudioKind.Sentence }, _ => new HashSet<ReviewAudioKind>(),
        } };
    }
    private async Task RunAsync(Guid? only, CancellationToken cancellationToken)
    {
        Message = null; Changed();
        try
        {
            var scope = await Controller.OperationScopeAsync(cancellationToken);
            var decks = AnkiReviewController.Strings(await Controller.CallAsync("deckNames", new Dictionary<string, AnkiValue>(), scope.Connection, cancellationToken));
            string defaultDeck = !scope.HadSavedDeck && !decks.Contains(scope.DefaultDeck, CanonicalStringComparer.Instance) ? decks.FirstOrDefault() ?? scope.DefaultDeck : scope.DefaultDeck;
            await Controller.VerifyAsync(scope, cancellationToken);
            var seen = _items.Where(item => item.Status == ReviewBatchStatus.Synced).Select(item => Resolve(item) is { } source ? item.CardId ?? source.Sense.Id : (Guid?)null).OfType<Guid>().ToHashSet();
            for (int index = 0; index < _items.Length; index++)
            {
                if (only is { } id && _items[index].Id != id || _items[index].Status is ReviewBatchStatus.Synced or ReviewBatchStatus.Skipped) continue;
                cancellationToken.ThrowIfCancellationRequested();
                if (Resolve(_items[index]) is not { } source) { Replace(index, _items[index] with { Status = ReviewBatchStatus.Unavailable }); continue; }
                Guid canonical = _items[index].CardId ?? source.Sense.Id;
                if (!seen.Add(canonical)) { Replace(index, _items[index] with { Status = ReviewBatchStatus.Skipped }); continue; }
                await Controller.VerifyAsync(scope, cancellationToken);
                Replace(index, _items[index] with { Status = ReviewBatchStatus.Sending, Message = null });
                var session = new AnkiReviewSession(Controller, source.Entry, source.Sense, scope, _items[index].CardId);
                var review = source.Sense.ReviewValue(_items[index].CardId);
                if (review?.ActiveBinding is null && review?.Plan.Deck is null) session.Deck = defaultDeck;
                await session.LoadAsync(cancellationToken);
                if (session.ValidationMessage is { } issue) { Replace(index, _items[index] with { Status = ReviewBatchStatus.Attention, Message = issue }); continue; }
                bool success = await session.SendAsync(expectedScope: scope, cancellationToken: cancellationToken);
                session.Audio.Discard();
                Replace(index, _items[index] with
                {
                    Status = success ? ReviewBatchStatus.Synced : cancellationToken.IsCancellationRequested ? ReviewBatchStatus.Cancelled : session.Conflicts.Count == 0 ? ReviewBatchStatus.Failed : ReviewBatchStatus.Attention,
                    Message = success ? _items[index].Message : cancellationToken.IsCancellationRequested ? null : session.Message ?? "Resolve this item's Anki field changes in preparation.",
                });
            }
        }
        catch (Exception error)
        {
            Message = error is OperationCanceledException ? null : Controller.DisplayFailure(error); Changed();
            for (int index = 0; index < _items.Length; index++)
                if (_items[index].Status is ReviewBatchStatus.Sending or ReviewBatchStatus.Ready or ReviewBatchStatus.VerifyAudio)
                    Replace(index, _items[index] with { Status = error is OperationCanceledException ? ReviewBatchStatus.Cancelled : ReviewBatchStatus.Failed, Message = Message });
        }
    }
}
