using System.Windows.Threading;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.App.Reading;

/// <summary>Only the native reader owns selection handles and notifications. A lease never grants
/// insertion or clipboard access and must revalidate exact occurrence before and after model work.</summary>
internal interface IReadingSelectionLease : IAsyncDisposable
{
    ReadingSelection Selection { get; }
    event Action? Invalidated;
    Task<bool> IsCurrentAsync(CancellationToken token);
    void Retire();
}
internal interface IReadingSelectionReader
{
    Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token);
}

internal sealed record ReadingDestination(string Label, bool UsesEndpoint, bool Authorized, string? EndpointUrl = null)
{
    internal string? Display()
    {
        if (!UsesEndpoint) return string.IsNullOrWhiteSpace(Label) ? null : Label;
        if (!Authorized || !Uri.TryCreate(EndpointUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        return "Endpoint: " + uri.GetLeftPart(UriPartial.Path);
    }
}
internal sealed record ReadingSettings(bool Enabled, bool SelectionEnabled, bool AppAllowed, LearningLanguage Target,
    ReadingDestination Translation, ReadingDestination Lookup, long Revision);
internal enum ReadingStatus { Loading, Translating, Lookup, Ready, Error }
internal sealed record ReadingState(ReadingSelection Selection, LearningLanguage? Source, LearningLanguage Target,
    ReadingRoute Route, ReadingStatus Status, string Destination, string Text, string? Message = null);
internal interface IReadingPresentation
{
    /// <summary>Displays the destination before any provider receives selected text.</summary>
    Task ShowAsync(ReadingState state);
    void Update(ReadingState state);
    void ShowCaptureError(string message);
    void Hide();
}

/// <summary>One explicit reading request. Injected generators borrow existing translation and
/// lookup owners; this coordinator creates no model, endpoint, clipboard or global listener.</summary>
internal sealed class ReadingSession : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IReadingSelectionReader _reader;
    private readonly IReadingPresentation _presentation;
    private readonly Func<ReadingSelection, ReadingLanguageResolution> _resolve;
    private readonly Func<string, LearningLanguage?, int> _wordCount;
    private readonly Func<ReadingSelection, LearningLanguage?, LearningLanguage, Action<string>, CancellationToken, Task<string>> _translate;
    private readonly Func<ReadingSelection, ReadingRoute, LearningLanguage?, LearningLanguage, CancellationToken, Task> _lookup;
    private readonly HashSet<Task> _work = [];
    private CancellationTokenSource? _cancel;
    private IReadingSelectionLease? _lease;
    private Action? _invalidated;
    private long _revision;
    private bool _disposed;
    private Task? _disposal;
    private ReadingSettings? _settings;

    internal ReadingSession(Dispatcher dispatcher, IReadingSelectionReader reader, IReadingPresentation presentation,
        Func<ReadingSelection, ReadingLanguageResolution> resolve, Func<string, LearningLanguage?, int> wordCount,
        Func<ReadingSelection, LearningLanguage?, LearningLanguage, Action<string>, CancellationToken, Task<string>> translate,
        Func<ReadingSelection, ReadingRoute, LearningLanguage?, LearningLanguage, CancellationToken, Task> lookup)
    {
        dispatcher.VerifyAccess();
        if (SynchronizationContext.Current is not DispatcherSynchronizationContext) throw new InvalidOperationException("Reading requires dispatcher ownership.");
        _dispatcher = dispatcher; _reader = reader; _presentation = presentation; _resolve = resolve; _wordCount = wordCount;
        _translate = translate; _lookup = lookup;
    }

    internal bool IsActive => _cancel is not null;
    internal void Begin(ReadingSettings settings)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        if (!settings.Enabled || !settings.SelectionEnabled || !settings.AppAllowed) { Dismiss(); return; }
        Dismiss(); _settings = settings; _cancel = new(); long revision = ++_revision;
        Track(RunAsync(settings, revision, _cancel.Token));
    }

    private async Task RunAsync(ReadingSettings settings, long revision, CancellationToken token)
    {
        IReadingSelectionLease? capture = null; bool transferred = false; ReadingState? displayed = null;
        try
        {
            capture = await _reader.CaptureAsync(token);
            if (!Current(revision, token)) return;
            if (capture is null) { Dismiss(); _presentation.ShowCaptureError("Selected text is unavailable in this app."); return; }
            _lease = capture; transferred = true;
            _invalidated = () =>
            {
                void Retire() { if (ReferenceEquals(_lease, capture)) Dismiss(); }
                if (_dispatcher.CheckAccess()) Retire(); else _dispatcher.BeginInvoke(Retire);
            };
            capture.Invalidated += _invalidated;
            var selection = capture.Selection;
            ReadingSelectionPolicy.ValidateReceipt(selection);
            if (!await capture.IsCurrentAsync(token) || !Current(revision, token)) { if (Current(revision, token)) Dismiss(); return; }
            var resolution = _resolve(selection);
            var route = ReadingSelectionPolicy.Route(selection, _wordCount(selection.Text, resolution.Language));
            var destination = route.IsTranslation ? settings.Translation : settings.Lookup;
            var destinationLabel = destination?.Display();
            if (destinationLabel is null)
            {
                Dismiss(); _presentation.ShowCaptureError("Configure and explicitly select a destination for this reading task."); return;
            }
            if (route.IsTranslation && resolution.Language == settings.Target)
            {
                await _presentation.ShowAsync(new(selection, resolution.Language, settings.Target, route, ReadingStatus.Error,
                    destinationLabel, "", "The text is already in the reading target language."));
                return;
            }
            var state = new ReadingState(selection, resolution.Language, settings.Target, route, ReadingStatus.Loading, destinationLabel, "");
            await _presentation.ShowAsync(state);
            displayed = state;
            if (!Current(revision, token)) return;
            if (!await capture.IsCurrentAsync(token)) { if (Current(revision, token)) Dismiss(); return; }
            if (!Current(revision, token)) return;
            state = state with { Status = route.IsTranslation ? ReadingStatus.Translating : ReadingStatus.Lookup };
            _presentation.Update(state);
            if (!route.IsTranslation)
            {
                // The existing dictionary/meaning card owns its rich target state and controls;
                // never flatten that result into a synthetic translation string here.
                await _lookup(selection, route, resolution.Language, settings.Target, token);
                if (!Current(revision, token)) return;
                if (!await capture.IsCurrentAsync(token)) { if (Current(revision, token)) Dismiss(); return; }
                return;
            }
            int generationComplete = 0;
            int invalidPartial = 0;
            long partialSequence = 0;
            void Partial(string text)
            {
                string safe;
                try { safe = SafeOutput(text); }
                catch { Interlocked.Exchange(ref invalidPartial, 1); return; }
                if (safe.Length == 0 || Volatile.Read(ref invalidPartial) != 0) return;
                _dispatcher.BeginInvoke(() =>
                {
                    var sequence = Interlocked.Increment(ref partialSequence);
                    Track(PresentPartialAsync(safe, sequence));
                });
            }
            async Task PresentPartialAsync(string text, long sequence)
            {
                if (Volatile.Read(ref generationComplete) != 0 || !Current(revision, token)) return;
                if (!await capture.IsCurrentAsync(token)) { if (Current(revision, token)) Dismiss(); return; }
                if (Volatile.Read(ref generationComplete) == 0 && sequence == Interlocked.Read(ref partialSequence) && Current(revision, token))
                    _presentation.Update(state with { Text = text });
            }
            string result;
            try
            {
                result = await _translate(selection, resolution.Language, settings.Target, Partial, token);
            }
            finally { Interlocked.Exchange(ref generationComplete, 1); }
            if (Volatile.Read(ref invalidPartial) != 0) throw new LearningTranslationException(LearningTranslationFailure.InvalidOutput);
            result = SafeOutput(result);
            if (result.Length == 0) throw new LearningTranslationException(LearningTranslationFailure.InvalidOutput);
            if (!Current(revision, token)) return;
            if (!await capture.IsCurrentAsync(token)) { if (Current(revision, token)) Dismiss(); return; }
            if (!Current(revision, token)) return;
            _presentation.Update(state with { Status = ReadingStatus.Ready, Text = result });
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (Current(revision, token))
            {
                bool current = false;
                try { current = capture is not null && await capture.IsCurrentAsync(token); } catch { }
                if (current && displayed is not null && Current(revision, token))
                    _presentation.Update(displayed with { Status = ReadingStatus.Error, Message = "Reading translation could not complete. Retry or adjust the model settings." });
                else if (capture is null) { Dismiss(); _presentation.ShowCaptureError("Selected text is unavailable in this app."); }
                else Dismiss();
            }
        }
        finally
        {
            if (capture is not null && !transferred) { capture.Retire(); await capture.DisposeAsync(); }
        }
    }

    internal void Dismiss()
    {
        _dispatcher.VerifyAccess(); ++_revision;
        var cancellation = _cancel; _cancel = null;
        if (cancellation is not null) Track(DisposeCancellationAsync(cancellation, cancellation.CancelAsync()));
        var lease = _lease; _lease = null;
        if (lease is not null)
        {
            if (_invalidated is not null) lease.Invalidated -= _invalidated;
            _invalidated = null; lease.Retire(); Track(lease.DisposeAsync().AsTask());
        }
        _presentation.Hide();
    }
    internal void SettingsChanged(ReadingSettings settings)
    {
        _dispatcher.VerifyAccess();
        if (_settings is not null && _settings != settings) Dismiss();
        _settings = settings;
    }
    private bool Current(long revision, CancellationToken token) => !_disposed && !token.IsCancellationRequested && _revision == revision;
    private static string SafeOutput(string text)
    {
        if (text is null || new System.Text.UTF8Encoding(false, true).GetByteCount(text) > LearningExpressionOutputPolicy.MaximumOutputBytes)
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOutput);
        return LearningExpressionOutputPolicy.ValidatedText(text);
    }
    private static async Task DisposeCancellationAsync(CancellationTokenSource source, Task cancellation)
    { try { await cancellation.ConfigureAwait(false); } finally { source.Dispose(); } }
    private void Track(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(t => { _ = t.Exception; _dispatcher.BeginInvoke(() => _work.Remove(t)); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    public ValueTask DisposeAsync()
    {
        _dispatcher.VerifyAccess();
        if (_disposal is not null) return new(_disposal);
        _disposed = true; Dismiss(); return new(_disposal = DrainAsync());
    }
    private async Task DrainAsync()
    {
        while (_work.Count != 0)
        {
            var tasks = _work.ToArray(); try { await Task.WhenAll(tasks); } catch { }
            foreach (var task in tasks) _work.Remove(task);
        }
    }
}
