using System.Windows.Threading;
using Cotabby.App.UI.Learning;
using Cotabby.App.UI.Reading;
using Cotabby.App.Learning;
using Cotabby.Core.Context;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.App.Reading;

/// <summary>The reading card and its actual bundled dictionary share the capture's retirement.
/// The dictionary remains a separate transient session using the app's existing service.</summary>
internal sealed partial class ReadingPresentation : IReadingPresentation, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ReadingWindow _reading = new();
    private readonly LearningDictionaryWindow _dictionary = new();
    private readonly LearningDictionarySession _session;
    private readonly object _gate = new();
    private readonly HashSet<Task> _work = [];
    private Func<CancellationToken, Task<bool>>? _current;
    private CancellationToken _token;
    private ReadingSelection? _selection;
    private ReadingState? _readingState;
    private ReadingLookupInput? _input;
    private ReadingLookupResult? _basic;
    private IReadOnlyList<LearningDictionaryWord> _words = [];
    private bool _dictionaryMode;
    private bool _detailsRunning;
    private bool _detailsExpanded;
    private string? _details;
    private bool _basicRunning;
    private readonly Func<ReadingLookupInput, CancellationToken, Task<ReadingLookupResult>> _basicLookup;
    private readonly Func<ReadingLookupInput, ReadingLookupResult, CancellationToken, Task<string>> _detailsLookup;
    private readonly Action _configure;
    private readonly Func<ReadingLookupInput, ReadingLookupResult, LearningExplanationInput, CancellationToken, Task<LearningExplanationRecord>>? _detailsRecord;
    private readonly LearningTextCopyController _copy;
    private readonly IWritingVocabularyStore _vocabulary;
    private readonly Func<string, LearningLanguage?, IReadOnlyList<LearningTextRange>> _sentences;
    private readonly LearningLanguage _meaningLanguage;
    private VocabularyCapture? _favoriteCapture;
    private Func<bool>? _favoriteCurrent;
    private long _favoriteRevision;
    private long _copyRevision;
    private long _revision;
    private bool _disposed;
    private Task? _disposal;
    internal event Action? DismissRequested;
    internal event Action? Hidden;
    internal ReadingWindow ReadingCard => _reading;
    internal LearningDictionaryWindow DictionaryCard => _dictionary;

    internal ReadingPresentation(Dispatcher dispatcher, ILearningDictionaryProviding dictionary,
        Func<ReadingLookupInput, CancellationToken, Task<ReadingLookupResult>> basicLookup,
        Func<ReadingLookupInput, ReadingLookupResult, CancellationToken, Task<string>> detailsLookup, Action configure,
        IClipboardTextWriter clipboard, IWritingVocabularyStore vocabulary,
        Func<string, LearningLanguage?, IReadOnlyList<LearningTextRange>> sentences, LearningLanguage meaningLanguage,
        Func<ReadingLookupInput, ReadingLookupResult, LearningExplanationInput, CancellationToken, Task<LearningExplanationRecord>>? detailsRecord = null)
    {
        _dispatcher = dispatcher;
        InitializeHitTesting();
        _basicLookup = basicLookup; _detailsLookup = detailsLookup; _configure = configure;
        _detailsRecord = detailsRecord; _copy = new(clipboard);
        _vocabulary = vocabulary; _sentences = sentences; _meaningLanguage = meaningLanguage;
        _session = new(dictionary);
        _session.StateChanged += StateChanged;
        _reading.DismissRequested += () => DismissRequested?.Invoke();
        _dictionary.CloseRequested += _ => DismissRequested?.Invoke();
        _dictionary.WordRequested += (word, target) => Navigate(() => _session.SelectWordAsync(word, target));
        _dictionary.BackRequested += target => Navigate(() => _session.ReturnToExpressionAsync(target));
        _dictionary.PageRequested += (page, target) => Navigate(() => _session.SelectPageAsync(page, target));
        _dictionary.ExplanationRequested += (target, state) => Navigate(() => state.IsGenerating
            ? CancelExplanation(target, state) : _session.ExplainAsync(target, state));
        _reading.DictionaryRequested += () => Navigate(OpenDictionaryAsync);
        _reading.DetailsRequested += () => Track(DetailsAsync());
        _reading.RetryRequested += () => Track(RetryAsync());
        _reading.CopyRequested += source => Track(CopyAsync(source));
        _reading.ConfigureRequested += () => { DismissRequested?.Invoke(); _configure(); };
        _dictionary.EnableReadingMeaning();
        _dictionary.ReadingMeaningRequested += () => Navigate(ReturnToMeaningAsync);
    }
    private Task CancelExplanation(Guid target, LearningExplanationState state)
    { _session.CancelExplanation(target, state); return Task.CompletedTask; }

    public void BindAuthority(ReadingSelection selection, Func<CancellationToken, Task<bool>> current, CancellationToken token)
    { _selection = selection; _current = current; _token = token; }
    public async Task ShowAsync(ReadingState state) { ReconcileSpeech(state); _readingState = state; await _reading.ShowAsync(state); UpdateBounds(); }
    public void Update(ReadingState state)
    {
        ReconcileSpeech(state); Interlocked.Increment(ref _copyRevision); _readingState = state; _reading.Update(state);
        if (state.Route.IsTranslation && state.Status == ReadingStatus.Ready && _favoriteCapture is null)
            Track(PrepareTranslationFavoriteAsync(state));
    }
    public void ShowCaptureError(string message) { StopSpeech(); _readingState = null; _reading.ShowCaptureError(message); }
    public void Hide()
    {
        _dispatcher.VerifyAccess(); StopSpeech();
        ++_revision; Interlocked.Increment(ref _copyRevision); _current = null; _selection = null; _input = null; _basic = null; _details = null;
        Interlocked.Increment(ref _favoriteRevision); _favoriteCapture = null; _favoriteCurrent = null; _reading.ClearVocabulary();
        _detailsExpanded = false; _reading.SetDetailsExpanded(false); _readingState = null; _dictionaryMode = false;
        _session.Close(); _dictionary.HideCard(); _reading.Hide();
        Hidden?.Invoke();
    }

    internal async Task LookupAsync(ReadingSelection selection, ReadingRoute route, LearningLanguage? language,
        LearningLanguage target, Func<CancellationToken, Task<bool>> current, CancellationToken token,
        IReadOnlyList<LearningDictionaryWord> words)
    {
        _dispatcher.VerifyAccess();
        _current = current; _selection = selection; _token = token;
        var text = route.LookupText!;
        _words = words;
        var context = text == Cotabby.Core.Text.FoundationText.TrimWhitespaceAndNewlines(selection.Text) ? selection.Context : null;
        _input = new(text, language, target, route.IsSingleWord, context) { Admission = selection.Admission };
        await RunBasicAsync();
    }
    private async Task RetryAsync()
    {
        if (_basic is not null) await DetailsAsync(); else await RunBasicAsync();
    }
    private async Task RunBasicAsync()
    {
        var input = _input; var token = _token; long revision = _revision;
        if (_basicRunning || input is null) return;
        _basicRunning = true;
        try
        {
            if (!await StillCurrentAsync(revision, token)) return;
            if (_readingState is { } loading) Update(loading with { Status = ReadingStatus.Lookup, Message = null });
            var basic = await _basicLookup(input, token);
            if (!await StillCurrentAsync(revision, token)) return;
            _basic = basic; _details = null; _detailsExpanded = false; _reading.SetDetailsExpanded(false);
            if (_readingState is { } state) Update(state with { Status = ReadingStatus.Ready, Text = basic.Meaning });
            BindFavorite(ReadingVocabularyCaptureFactory.Lookup(input, basic, _meaningLanguage, _selection), revision, token,
                () => ReferenceEquals(_basic, basic) && _input == input);
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (await StillCurrentAsync(revision, token) && _readingState is { } state)
                Update(state with { Status = ReadingStatus.Error, Message = "Could not look up this meaning. Check the AI explanation model, or use Dictionary." });
        }
        finally { _basicRunning = false; }
    }

    private async Task OpenDictionaryAsync()
    {
        if (_selection is not { } selection || _input is not { } input) return;
        _dictionaryMode = true; StopSpeech(); _reading.Hide();
        var unit = new LearningUnit(0, input.Target, input.Target, input.IsWord ? LearningUnitKind.Word : LearningUnitKind.Expression,
            [new LearningTextRange(0, input.Target.Length)], _words);
        await _session.OpenAsync(unit, selection.Id.ToString(), input.Source);
    }
    private Task ReturnToMeaningAsync()
    {
        _dictionaryMode = false; _session.Close(); _dictionary.HideCard();
        return _readingState is { } state ? _reading.ShowAsync(state) : Task.CompletedTask;
    }
    private async Task<bool> StillCurrentAsync(long revision, CancellationToken token)
    {
        var current = _current;
        return current is not null && !token.IsCancellationRequested && revision == _revision && _selection?.Admission?.Invoke() != false
            && await current(token) && !token.IsCancellationRequested && revision == _revision && _selection?.Admission?.Invoke() != false;
    }
    private async Task PrepareTranslationFavoriteAsync(ReadingState state)
    {
        long revision = _revision; var token = _token;
        try
        {
            var capture = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                string text = state.Selection.Text;
                bool sentence = state.WordCount != 1 && text.Length > 0 && ".!?。！？".Contains(text[^1]);
                var ranges = sentence ? _sentences(text, state.Source) : Array.Empty<LearningTextRange>();
                token.ThrowIfCancellationRequested();
                return ReadingVocabularyCaptureFactory.Translation(state.Selection, state.Source, state.Target, state.Text,
                    state.WordCount, ranges, DateTimeOffset.UtcNow);
            }, token);
            if (ReferenceEquals(state, _readingState) && await StillCurrentAsync(revision, token) && ReferenceEquals(state, _readingState))
                BindFavorite(capture, revision, token, () => ReferenceEquals(state, _readingState));
        }
        catch (OperationCanceledException) { }
        catch { /* A missing tokenizer must not invent a vocabulary capture. */ }
    }
    private void BindFavorite(VocabularyCapture capture, long revision, CancellationToken token, Func<bool> contentCurrent)
    {
        _favoriteCapture = capture; _favoriteCurrent = contentCurrent; long binding = Interlocked.Increment(ref _favoriteRevision);
        bool Current() => !token.IsCancellationRequested && _selection?.Admission?.Invoke() != false && Interlocked.Read(ref _favoriteRevision) == binding && contentCurrent();
        async Task<bool> Preflight() => Current() && await StillCurrentAsync(revision, token) && Current();
        _reading.BindVocabulary(_vocabulary, capture, Current, Preflight);
    }
    private async Task CopyAsync(bool source)
    {
        var state = _readingState; var token = _token; long revision = _revision;
        if (state is null || (!source && state.Status != ReadingStatus.Ready)) return;
        string text = source ? state.Selection.Text : state.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!await StillCurrentAsync(revision, token) || !ReferenceEquals(state, _readingState)) return;
        long copyRevision = Interlocked.Increment(ref _copyRevision);
        bool allowed() => !Volatile.Read(ref _disposed) && !token.IsCancellationRequested
            && state.Selection.Admission?.Invoke() != false && Interlocked.Read(ref _copyRevision) == copyRevision;
        bool copied = await _copy.CopyAsync(text, allowed);
        if (allowed() && ReferenceEquals(state, _readingState)) _reading.ShowCopyOutcome(copied);
    }
    private async Task DetailsAsync()
    {
        if (_detailsRunning) return;
        _detailsRunning = true;
        try { await RunDetailsAsync(); }
        finally { _detailsRunning = false; }
    }
    private async Task RunDetailsAsync()
    {
        var input = _input; var basic = _basic; var token = _token; long revision = _revision;
        if (input is null || basic is null || !await StillCurrentAsync(revision, token)) return;
        if (_details is { } existing)
        {
            _detailsExpanded = !_detailsExpanded; _reading.SetDetailsExpanded(_detailsExpanded);
            if (_readingState is { } cached) Update(cached with { Status = ReadingStatus.Ready,
                Text = _detailsExpanded ? basic.Meaning + "\n\n" + existing : basic.Meaning });
            return;
        }
        if (_readingState is { } loading) Update(loading with { Status = ReadingStatus.Lookup });
        try
        {
            var favorite = _favoriteCapture;
            LearningExplanationRecord? record = null;
            string details;
            if (_detailsRecord is not null && favorite is not null)
            {
                var target = LearningExplanationPolicy.Input(favorite);
                record = await _detailsRecord(input, basic, target, token);
                details = record.Result.Explanation;
            }
            else details = await _detailsLookup(input, basic, token);
            if (!await StillCurrentAsync(revision, token) || !ReferenceEquals(basic, _basic) || _input != input) return;
            _details = details; _detailsExpanded = true; _reading.SetDetailsExpanded(true);
            if (_readingState is { } state) Update(state with { Status = ReadingStatus.Ready, Text = basic.Meaning + "\n\n" + details });
            if (record is not null && favorite is not null && ReferenceEquals(favorite, _favoriteCapture) && _selection?.Admission?.Invoke() != false)
            {
                BindFavorite(favorite with { Explanation = record }, revision, token, () => ReferenceEquals(_basic, basic) && _input == input);
                // This synchronous permit check is the accepted-write boundary. Once admitted,
                // the borrowed Store owns and drains the operation even if the panel retires.
                if (!token.IsCancellationRequested && _selection?.Admission?.Invoke() != false)
                    await _vocabulary.SaveExplanationAsync(record);
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (await StillCurrentAsync(revision, token) && _readingState is { } state)
                Update(state with { Status = ReadingStatus.Error, Text = basic.Meaning, Message = "Could not explain this usage. Check the local AI explanation model." });
        }
    }

    private void StateChanged(LearningDictionaryState state)
    {
        lock (_gate)
        {
            if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            try { TrackLocked(_dispatcher.InvokeAsync(() => Track(RenderAsync(state))).Task); }
            catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) { }
        }
    }
    private async Task RenderAsync(LearningDictionaryState state)
    {
        var current = _current; var selection = _selection; var token = _token; long revision = _revision;
        if (!_dictionaryMode || current is null || selection is null || token.IsCancellationRequested || !ReferenceEquals(state, _session.State)) return;
        if (!await current(token) || token.IsCancellationRequested || revision != _revision || !_dictionaryMode || !ReferenceEquals(state, _session.State)) return;
        _dictionary.ShowState(state, selection.Anchor);
        UpdateBounds();
        if (state.SelectedWord is null && _favoriteCapture is { } favorite)
        {
            var tokenCopy = token; long binding = Interlocked.Read(ref _favoriteRevision);
            bool Current() => !tokenCopy.IsCancellationRequested && selection.Admission?.Invoke() != false && Interlocked.Read(ref _favoriteRevision) == binding && _favoriteCurrent?.Invoke() == true;
            async Task<bool> Preflight() => Current() && await StillCurrentAsync(revision, tokenCopy) && Current();
            _dictionary.BindVocabulary(_vocabulary, favorite, state.TargetIdentity, Current, Preflight);
        }
    }
    private void Navigate(Func<Task> action) => Track(NavigateAsync(action));
    private async Task NavigateAsync(Func<Task> action)
    {
        var current = _current; var token = _token; long revision = _revision;
        if (current is null || token.IsCancellationRequested || !await current(token)
            || token.IsCancellationRequested || revision != _revision) return;
        await action();
    }
    private void Track(Task task) { lock (_gate) TrackLocked(task); }
    private void TrackLocked(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(t => { _ = t.Exception; lock (_gate) _work.Remove(t); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    public ValueTask DisposeAsync()
    {
        _dispatcher.VerifyAccess();
        return new(_disposal ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        lock (_gate) _disposed = true;
        Hide(); _copy.Dispose(); _session.StateChanged -= StateChanged;
        await _session.DisposeAsync();
        try { await Task.WhenAll(_reading.FavoriteWork, _dictionary.FavoriteWork); } catch { }
        while (true)
        {
            Task[] pending; lock (_gate) { if (_work.Count == 0) break; pending = _work.ToArray(); }
            try { await Task.WhenAll(pending); } catch { }
        }
        DetachSpeech(); _reading.Close(); _dictionary.Close();
    }
}
