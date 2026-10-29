using System.Windows.Threading;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>The platform owner retains its host handle and proves panel ownership and the return
/// transaction. No receipt by itself authorizes a write.</summary>
internal interface IExpressionAssistEditorSession : IAsyncDisposable
{
    ExpressionEditorReceipt Receipt { get; }
    event Action? Invalidated;
    bool EnterPanel();
    Task<bool> IsUnchangedAsync(CancellationToken cancellationToken);
    bool BeginReturn();
    Task<bool> ResumeAndReplaceAsync(string text, CancellationToken cancellationToken);
    void Retire();
}

internal interface IExpressionAssistEditor
{
    Task<IExpressionAssistEditorSession?> CaptureAsync(string? trigger, nint panelHwnd, int? initiatingVirtualKey,
        Func<bool> panelOwnsKeyboard, CancellationToken cancellationToken);
}

internal interface IExpressionAssistPanel
{
    nint WindowHandle { get; }
    bool OwnsKeyboard { get; }
    bool IsVisible { get; }
    Task ShowAsync(ExpressionEditorReceipt receipt, string initialQuery);
    void Update(ExpressionAssistResult? result, string? message, bool loading);
    void Hide(bool restoringHost);
    void ShowFailure(string candidate, string message);
    void ShowUnavailable(string message);
}

/// <summary>One dispatcher-owned explicit session; all async effects are tracked through disposal.
/// User text is temporary, never a model history or a write until the editor owner verifies return.</summary>
internal sealed class ExpressionAssistSession : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IExpressionAssistEditor _editor;
    private readonly IExpressionAssistPanel _panel;
    private readonly Func<ExpressionAssistRequest, CancellationToken, Task<ExpressionAssistResult>> _generate;
    private readonly Func<(LearningLanguage Target, LearningLanguage Explanation)> _languages;
    private readonly HashSet<Task> _work = [];
    private CancellationTokenSource? _sessionCancellation, _requestCancellation;
    private IExpressionAssistEditorSession? _capture;
    private Action? _editorInvalidated;
    private ExpressionAssistResult? _result;
    private string _query = "";
    private bool _composing, _acquiring, _committing, _disposed;
    private long _revision;
    private Task? _disposal;

    internal ExpressionAssistSession(Dispatcher dispatcher, IExpressionAssistEditor editor, IExpressionAssistPanel panel,
        Func<ExpressionAssistRequest, CancellationToken, Task<ExpressionAssistResult>> generate,
        Func<(LearningLanguage Target, LearningLanguage Explanation)> languages)
    {
        dispatcher.VerifyAccess();
        if (SynchronizationContext.Current is not DispatcherSynchronizationContext)
            throw new InvalidOperationException("Expression sessions require a dispatcher synchronization context.");
        _dispatcher = dispatcher; _editor = editor; _panel = panel; _generate = generate; _languages = languages;
    }

    internal bool IsActive => _sessionCancellation is not null;

    internal void Begin(string? trigger, int? initiatingVirtualKey = null)
    {
        Verify();
        if (_disposed || IsActive) return;
        _sessionCancellation = new(); _acquiring = true;
        Track(BeginAsync(trigger, initiatingVirtualKey, _sessionCancellation.Token));
    }

    private async Task BeginAsync(string? trigger, int? initiatingVirtualKey, CancellationToken token)
    {
        IExpressionAssistEditorSession? capture = null;
        bool transferred = false;
        try
        {
            capture = await _editor.CaptureAsync(trigger, _panel.WindowHandle, initiatingVirtualKey, () => _panel.OwnsKeyboard, token);
            token.ThrowIfCancellationRequested();
            if (capture is null) { Invalidate(); return; }
            _capture = capture;
            transferred = true;
            _editorInvalidated = () =>
            {
                void RetireIfCurrent() { if (ReferenceEquals(_capture, capture)) Invalidate(); }
                if (_dispatcher.CheckAccess()) RetireIfCurrent(); else _dispatcher.BeginInvoke(RetireIfCurrent);
            };
            capture.Invalidated += _editorInvalidated;
            _query = trigger is null ? capture.Receipt.Source : "";
            await _panel.ShowAsync(capture.Receipt, _query);
            token.ThrowIfCancellationRequested();
            if (!_panel.IsVisible || !_panel.OwnsKeyboard || !capture.EnterPanel() || !await capture.IsUnchangedAsync(token))
            { Invalidate(); return; }
            _acquiring = false;
            if (!string.IsNullOrWhiteSpace(_query)) Request(false);
        }
        catch (OperationCanceledException) { }
        catch (NotSupportedException)
        {
            if (!token.IsCancellationRequested)
            {
                Invalidate();
                _panel.ShowUnavailable("This trigger is not supported here. Choose ASCII trigger symbols or use the expression shortcut.");
            }
        }
        catch { if (!token.IsCancellationRequested) Invalidate(); }
        finally
        {
            if (capture is not null && !transferred) { capture.Retire(); await capture.DisposeAsync(); }
        }
    }

    internal void QueryChanged(string query, bool composing)
    {
        Verify(); if (_capture is null || _committing || (_query == query && _composing == composing)) return;
        _query = query; _composing = composing; Request(false);
    }
    internal void Submit() { Verify(); Request(true); }

    private void Request(bool immediate)
    {
        ++_revision; CancelAndDispose(_requestCancellation); _requestCancellation = null;
        _result = null;
        _panel.Update(null, null, false);
        if (_capture is null || _committing || _acquiring || _composing || !_panel.OwnsKeyboard || string.IsNullOrWhiteSpace(_query)) return;
        _requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation!.Token);
        Track(RequestAsync(_capture, _query, _revision, immediate, _requestCancellation.Token));
    }

    private async Task RequestAsync(IExpressionAssistEditorSession capture, string query, long revision, bool immediate, CancellationToken token)
    {
        try
        {
            if (!immediate) await Task.Delay(TimeSpan.FromMilliseconds(450), token);
            if (!Current(capture, revision, token)) return;
            if (!await capture.IsUnchangedAsync(token)) { if (Current(capture, revision, token)) Invalidate(); return; }
            if (!Current(capture, revision, token)) return;
            var receipt = capture.Receipt; var languages = _languages();
            var request = ExpressionAssistPolicy.Request(query, receipt.Original.Value, receipt.ReplacementRange.Start,
                receipt.ReplacementRange.Length, languages.Target, languages.Explanation);
            if (receipt.IsTrigger) request = request with { SelectedText = "" };
            _panel.Update(null, null, true);
            var result = await _generate(request, token);
            if (!Current(capture, revision, token)) return;
            if (!await capture.IsUnchangedAsync(token)) { if (Current(capture, revision, token)) Invalidate(); return; }
            if (Current(capture, revision, token)) { _result = result; _panel.Update(result, result.Clarification, false); }
        }
        catch (OperationCanceledException) { }
        catch { if (Current(capture, revision, token)) _panel.Update(null, "Expression assistance could not complete. Press Enter to retry.", false); }
    }

    internal void Accept(ExpressionAssistCandidate candidate)
    {
        Verify();
        if (_result?.Candidates.Contains(candidate) == true) Finish(candidate.Text);
    }
    internal void Cancel() { Verify(); Finish(null); }
    internal void FocusLost() { Verify(); if (!_acquiring && !_committing) Invalidate(); }
    private void Finish(string? text)
    {
        if (_capture is not { } capture || _committing || _composing || _acquiring || !_panel.OwnsKeyboard) return;
        var replacement = text ?? (capture.Receipt.IsTrigger ? "" : null);
        if (replacement is null) { Invalidate(); return; }
        _committing = true; ++_revision; CancelAndDispose(_requestCancellation); _requestCancellation = null;
        if (!capture.BeginReturn())
        {
            Invalidate();
            if (text is not null) _panel.ShowFailure(text, "The insertion could not be verified. Check the editor before copying this candidate.");
            return;
        }
        _panel.Hide(true);
        Track(FinishAsync(capture, replacement, text is not null, _sessionCancellation!.Token));
    }
    private async Task FinishAsync(IExpressionAssistEditorSession capture, string replacement, bool candidate, CancellationToken token)
    {
        bool verified = false;
        try { verified = await capture.ResumeAndReplaceAsync(replacement, token); }
        catch (OperationCanceledException) { }
        catch { }
        if (!ReferenceEquals(_capture, capture)) return;
        bool cancelled = token.IsCancellationRequested;
        Invalidate();
        if (!verified && candidate && !cancelled)
            _panel.ShowFailure(replacement, "The insertion could not be verified. Check the editor before copying this candidate.");
    }

    internal void Invalidate()
    {
        Verify(); ++_revision;
        CancelAndDispose(_requestCancellation); _requestCancellation = null;
        CancelAndDispose(_sessionCancellation); _sessionCancellation = null;
        var capture = _capture; _capture = null;
        if (capture is not null)
        {
            if (_editorInvalidated is not null) capture.Invalidated -= _editorInvalidated;
            _editorInvalidated = null; capture.Retire(); Track(capture.DisposeAsync().AsTask());
        }
        _acquiring = _committing = _composing = false; _query = ""; _result = null;
        _panel.Hide(false);
    }
    private bool Current(IExpressionAssistEditorSession capture, long revision, CancellationToken token) =>
        !_disposed && !token.IsCancellationRequested && !_composing && !_committing && _panel.IsVisible && _panel.OwnsKeyboard
        && revision == _revision && ReferenceEquals(_capture, capture);
    private void Track(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(t => { _ = t.Exception; _dispatcher.BeginInvoke(() => _work.Remove(t)); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private void Verify() => _dispatcher.VerifyAccess();
    private void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null) return;
        var cancellation = source.CancelAsync();
        Track(DisposeCancellationAsync(source, cancellation));
    }
    private static async Task DisposeCancellationAsync(CancellationTokenSource source, Task cancellation)
    { try { await cancellation.ConfigureAwait(false); } finally { source.Dispose(); } }

    public ValueTask DisposeAsync()
    {
        Verify();
        if (_disposal is not null) return new(_disposal);
        _disposed = true; Invalidate();
        return new(_disposal = DrainAsync());
    }
    private async Task DrainAsync()
    {
        while (_work.Count != 0)
        {
            var tasks = _work.ToArray();
            try { await Task.WhenAll(tasks); } catch { }
            foreach (var task in tasks) _work.Remove(task);
        }
    }
}
