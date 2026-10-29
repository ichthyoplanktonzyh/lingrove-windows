
using System.Windows.Threading;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.EntryInput;

namespace Cotabby.App.Vocabulary;

// The app-owned archive adapter must plan against its current immutable Frozen document. Preview
// reports its revision; AddEntries re-plans atomically and acknowledges publication before returning.
// No implementation or notebook lifetime is owned by a sheet.
internal interface IVocabularyEntryInputOwner
{
    Task<VocabularyInputPreview> PreviewAsync(VocabularyWordList table, VocabularyWordListConfiguration configuration, CancellationToken token);
    IDisposable Subscribe(Action<ulong> changed);
    Task<VocabularyInputResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token);
}
internal sealed record VocabularyInputSelection(Guid EntryId, Guid SenseId);
internal sealed record VocabularyInputResult(int AddedEntries, int AddedSenses, int SkippedRows, int AddedExamples,
    int AddedTranslations, int ChangedCollections, VocabularyInputSelection? Selection)
{
    internal bool HasChanges => AddedSenses > 0 || AddedExamples > 0 || AddedTranslations > 0 || ChangedCollections > 0;
}
internal sealed record VocabularyInputPreviewResult(ulong Revision, VocabularyInputResult Result);
internal sealed record VocabularyInputPreview(IReadOnlyList<VocabularyEntryInput> Inputs, VocabularyInputPreviewResult Plan);

/// <summary>A sheet-local, dispatcher-owned lifetime. Generation guards stale file and preview completions;
/// an accepted commit drains without cancellation and its acknowledgement remains authoritative.</summary>
internal sealed class VocabularyEntryImportSession
{
    private readonly Dispatcher _dispatcher;
    private readonly IVocabularyEntryInputOwner _owner;
    private readonly IVocabularyWordListReader _reader;
    private CancellationTokenSource? _work;
    private readonly IDisposable _subscription;
    private long _generation;
    private ulong? _latestRevision;
    private readonly object _revisionGate = new();
    private ulong _pendingRevision;
    private bool _revisionQueued, _revisionRetired;
    internal VocabularyEntryImportSession(Dispatcher dispatcher, IVocabularyEntryInputOwner owner, IVocabularyWordListReader reader)
    {
        _dispatcher = dispatcher; _owner = owner; _reader = reader; dispatcher.VerifyAccess();
        _subscription = owner.Subscribe(QueueRevision);
    }
    internal VocabularyWordList? Table { get; private set; }
    internal VocabularyWordListConfiguration? Configuration { get; private set; }
    internal VocabularyInputPreview? Preview { get; private set; }
    internal bool Preparing { get; private set; }
    internal bool Saving { get; private set; }
    internal bool Active { get; private set; } = true;
    internal string? Failure { get; private set; }
    internal event Action? Changed;

    internal async Task LoadAsync(string path, LearningLanguage language)
    {
        Verify(); if (!Active || Saving) return;
        var (id, token) = Begin(); Table = null; Configuration = null; Notify();
        try
        {
            var table = await _reader.ReadAsync(path, token);
            if (!Current(id)) return;
            Table = table; Configuration = VocabularyWordListPolicy.Configuration(table, language);
            await RefreshAsync(Configuration);
        }
        catch (Exception error) { Fail(id, error); }
    }
    internal async Task RefreshAsync(VocabularyWordListConfiguration configuration)
    {
        Verify(); if (!Active || Saving || Table is null) return;
        var table = Table; Configuration = configuration;
        var (id, token) = Begin(); Notify();
        try
        {
            var preview = await _owner.PreviewAsync(table, configuration, token);
            if (!Current(id)) return;
            if (_latestRevision is { } revision && preview.Plan.Revision < revision) { await RefreshAsync(configuration); return; }
            Preview = preview; Preparing = false; Notify();
        }
        catch (Exception error) { Fail(id, error); }
    }
    // Composition forwards Store revisions so a visible preview never silently describes an older notebook.
    internal Task OwnerRevisionChangedAsync(ulong revision)
    {
        Verify(); _latestRevision = Math.Max(_latestRevision ?? 0, revision); return Active && !Saving && Preview is { } preview && preview.Plan.Revision < _latestRevision && Configuration is { } config
            ? RefreshAsync(config) : Task.CompletedTask;
    }
    internal async Task<VocabularyInputResult?> CommitAsync()
    {
        Verify(); if (!Active || Saving || Preparing || Preview is not { Plan.Result.HasChanges: true } preview) return null;
        Saving = true; Failure = null; Notify(); bool acknowledged = false;
        try { var result = await _owner.AddEntriesAsync(preview.Inputs, CancellationToken.None); acknowledged = true; return result; }
        catch (Exception error) { Failure = VocabularyInputMessages.For(error); return null; }
        finally
        {
            Saving = false; Notify();
            // A revision arriving during a failed write invalidates the displayed plan. Keep the
            // failure visible and require a fresh preview before another attempt.
            if (!acknowledged && _latestRevision is { } revision && Preview?.Plan.Revision < revision && Configuration is { } config)
            {
                string? failure = Failure; await RefreshAsync(config);
                if (Active && Equals(Configuration, config) && Failure is null) { Failure = failure; Notify(); }
            }
        }
    }
    internal void Cancel()
    {
        Verify(); if (!Active || Saving) return;
        Active = false; lock (_revisionGate) { _revisionRetired = true; _pendingRevision = 0; }
        _subscription.Dispose(); _generation++; _work?.Cancel(); _work?.Dispose(); _work = null;
        Preview = null; Preparing = false; Notify();
    }
    // Worker callbacks can arrive faster than the dispatcher can run. Keep one pending operation
    // and the maximum revision; cancellation retires even a callback already copied by the Store.
    private void QueueRevision(ulong revision)
    {
        lock (_revisionGate)
        {
            if (_revisionRetired || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            _pendingRevision = Math.Max(_pendingRevision, revision);
            if (_revisionQueued) return;
            _revisionQueued = true;
            try { _dispatcher.BeginInvoke(DispatcherPriority.DataBind, DrainRevision); }
            catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            { _revisionQueued = false; _pendingRevision = 0; }
        }
    }
    private void DrainRevision()
    {
        ulong revision;
        lock (_revisionGate)
        {
            _revisionQueued = false;
            if (_revisionRetired) { _pendingRevision = 0; return; }
            revision = _pendingRevision; _pendingRevision = 0;
        }
        if (Active) _ = OwnerRevisionChangedAsync(revision);
    }
    private (long, CancellationToken) Begin()
    {
        _work?.Cancel(); _work?.Dispose(); _work = new(); _generation++;
        Preparing = true; Preview = null; Failure = null; return (_generation, _work.Token);
    }
    private bool Current(long id) => Active && id == _generation;
    private void Fail(long id, Exception error)
    { if (!Current(id)) return; Preparing = false; if (error is not OperationCanceledException) Failure = VocabularyInputMessages.For(error); Notify(); }
    private void Verify() => _dispatcher.VerifyAccess();
    private void Notify() { if (Changed is not { } handlers) return; foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch { /* Presentation cannot undo an acknowledged write. */ } }
}

internal static class VocabularyInputMessages
{
    internal static string For(Exception error) => error is not VocabularyWordListException e ? error.Message : e.Error switch
    {
        VocabularyWordListError.TooLarge => "Use a word list up to 4 MB with at most 10,000 rows.",
        VocabularyWordListError.Encoding => "Save the word list as UTF-8 or UTF-16 text and try again.",
        VocabularyWordListError.UnsupportedFile => "Choose a CSV or TXT word list.",
        VocabularyWordListError.Empty => "The word list has no entries to import.",
        VocabularyWordListError.Mapping => "Choose the column containing the expressions.",
        VocabularyWordListError.InvalidInput => "Check the expression, meaning, notes, and tags before saving.",
        VocabularyWordListError.MalformedCsv => $"Check the CSV quoting near line {e.Line}.",
        VocabularyWordListError.MissingExpression => $"An expression is missing on line {e.Line}.",
        VocabularyWordListError.Language => $"Use a supported language code on line {e.Line}, such as en or zh-Hans.",
        VocabularyWordListError.Kind => $"Use word, expression, pattern, or sentence as the type on line {e.Line}.",
        VocabularyWordListError.MissingExample => $"An example sentence is missing on line {e.Line}.",
        VocabularyWordListError.TranslationWithoutExample => "Add an example sentence before its translation.",
        VocabularyWordListError.ExampleConflict => "This example belongs to another sense. Match its saved meaning or edit it in the notebook.",
        _ => "The word list could not be imported."
    };
}
