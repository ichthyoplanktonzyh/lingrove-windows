using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Review;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Review;

namespace Cotabby.App.UI.Review;

/// <summary>The batch sheet owns only presentation and a cancellable queue; all writes reuse the
/// single canonical session and app Feature. Close joins accepted work before retiring children.</summary>
internal sealed class CanonicalAnkiReviewBatchWindow : Window
{
    private readonly CanonicalAnkiReviewBatchSession _batch;
    private readonly AnkiReviewFeature _feature;
    private readonly Action _configure;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StackPanel _rows = new();
    private readonly TextBox _deck = new();
    private readonly TextBlock _destination = new() { TextWrapping = TextWrapping.Wrap }, _message = new() { TextWrapping = TextWrapping.Wrap }, _count = new();
    private readonly Button _apply = new() { Content = "Apply to selected" }, _reload = new() { Content = "Refresh connection" },
        _stop = new() { Content = "Stop" }, _close = new() { Content = "Close", IsCancel = true }, _send = new() { Content = "Send remaining", IsDefault = true };
    private ReviewOperationScope? _scope;
    private Task? _aux, _stopWork, _retirement;
    private CanonicalAnkiReviewWindow? _preparation;
    private bool _retiring, _allowClose, _retired;
    private string? _uiError;
    private Exception? _stopFailure;
    internal Task? LastOperation => _aux ?? _batch.Work;
    internal CanonicalAnkiReviewWindow? Preparation => _preparation;
    internal Button SendButton => _send;
    internal CanonicalAnkiReviewBatchSession Batch => _batch;
    internal StackPanel Rows => _rows;
    internal TextBlock MessageText => _message;
    internal CanonicalAnkiReviewBatchWindow(CanonicalAnkiReviewBatchSession batch, AnkiReviewFeature feature, Action configure)
    {
        _batch = batch; _feature = feature; _configure = configure; Title = "Review cards"; Width = 760; Height = 740; MinWidth = 580; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        VocabularyNotebookTheme.Follow(this); var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var heading = VocabularyInputControls.Heading("Review cards"); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); _count.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(_count, Dock.Left); footer.Children.Add(_count);
        var buttons = VocabularyInputControls.Footer(_stop, _close, _send); footer.Children.Add(buttons);
        var form = new StackPanel(); root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = form });
        form.Children.Add(VocabularyInputControls.Group(("Destination", _destination), ("", _reload), ("Deck", _deck), ("", _apply)));
        form.Children.Add(VocabularyInputControls.Caption("Leave the deck empty to keep each plan."));
        if (_batch.EmptySenseCount > 0) form.Children.Add(VocabularyInputControls.Caption("Some selected senses have no cards. Add cards in the notebook first."));
        form.Children.Add(_rows); form.Children.Add(_message);
        foreach (var button in new[] { _apply, _reload, _stop }) VocabularyNotebookTheme.Button(button); VocabularyNotebookTheme.Brush(_message, TextBlock.ForegroundProperty, "OrangeBrush");
        _batch.PropertyChanged += BatchChanged; _feature.Settings.PropertyChanged += SettingsChanged;
        _send.Click += (_, _) => StartAux(token => StartBatchAsync(null, token));
        _apply.Click += (_, _) => _batch.StartApply(_deck.Text); _reload.Click += (_, _) => StartAux(LoadScopeAsync);
        _stop.Click += (_, _) => Stop(); _close.Click += (_, _) => Close();
        Loaded += (_, _) => StartAux(LoadScopeAsync); Closing += OnClosing; Closed += (_, _) => RetirePresentation();
        Render();
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_retiring || e.PropertyName is not (nameof(AnkiReviewSettings.Address) or nameof(AnkiReviewSettings.Model) or nameof(AnkiReviewSettings.Mapping))) return;
        _scope = null; Render();
    }
    private void BatchChanged(object? sender, PropertyChangedEventArgs e) { if (!_retiring) Render(); }
    private void Render()
    {
        _destination.Text = _scope is { } scope ? $"{scope.Connection.Endpoint}\nProfile: {scope.Profile}" : "Connection not loaded. Refresh before sending.";
        _count.Text = $"{_batch.CompleteCount} / {_batch.Items.Count}"; _message.Text = _uiError ?? _batch.Message ?? ""; _rows.Children.Clear();
        foreach (var item in _batch.Items)
        {
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = item.Expression, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(VocabularyInputControls.Caption(item.Meaning)); panel.Children.Add(VocabularyInputControls.Caption(item.Status.Label()));
            if (item.Message is { Length: > 0 } issue) panel.Children.Add(VocabularyInputControls.Caption(issue));
            var actions = new WrapPanel(); var prepare = Button("Prepare", () => StartAux(token => PrepareAsync(item.Id, token))); prepare.IsEnabled = !_retiring && !_batch.Busy && _aux is null && item.Status != ReviewBatchStatus.Unavailable; actions.Children.Add(prepare);
            if (item.Status is ReviewBatchStatus.Failed or ReviewBatchStatus.Cancelled or ReviewBatchStatus.Attention)
            { var retry = Button("Retry", () => StartAux(token => StartBatchAsync(item.Id, token))); retry.IsEnabled = !_retiring && !_batch.Busy && _aux is null && _scope is not null; actions.Children.Add(retry); }
            panel.Children.Add(actions);
            var card = new Border { Child = panel, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12) };
            VocabularyNotebookTheme.Brush(card, Border.BackgroundProperty, "CardBg"); VocabularyNotebookTheme.Brush(card, Border.BorderBrushProperty, "CardBorder"); _rows.Children.Add(card);
        }
        bool idle = !_retiring && !_batch.Busy && _aux is null; _apply.IsEnabled = idle; _reload.IsEnabled = idle; _deck.IsEnabled = idle;
        _stop.Visibility = _batch.Busy ? Visibility.Visible : Visibility.Collapsed; _close.Visibility = _batch.Busy ? Visibility.Collapsed : Visibility.Visible; _close.IsEnabled = !_retiring && _aux is null;
        _send.IsEnabled = idle && _scope is not null && _batch.Items.Any(item => item.Status is not (ReviewBatchStatus.Synced or ReviewBatchStatus.Skipped or ReviewBatchStatus.Unavailable));
    }
    private static Button Button(string label, Action action) { var value = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0) }; VocabularyNotebookTheme.Button(value); value.Click += (_, _) => action(); return value; }
    private async Task StartBatchAsync(Guid? only, CancellationToken token)
    {
        var shown = _scope ?? throw new AnkiReviewException("Refresh the Anki connection before sending."); ReviewOperationScope? current = null;
        await _feature.RunAsync(async featureToken => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, featureToken); current = await _feature.Controller.CanonicalScopeAsync(linked.Token); await _feature.Controller.CanonicalVerifyAsync(current, linked.Token); });
        if (current is null || !current.Connection.Equals(shown.Connection) || !Cotabby.Core.Text.CanonicalStringComparer.Instance.Equals(current.Profile, shown.Profile) || !_feature.Controller.Matches(shown))
        { _scope = null; throw new AnkiReviewException("Anki connection or profile changed. Refresh before sending."); }
        _batch.Start(current, only);
    }
    private async Task LoadScopeAsync(CancellationToken token)
    {
        ReviewOperationScope? result = null;
        await _feature.RunAsync(async featureToken => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, featureToken); var scope = await _feature.Controller.CanonicalScopeAsync(linked.Token); await _feature.Controller.CanonicalVerifyAsync(scope, linked.Token); result = scope; });
        if (!_retiring && result is not null && _feature.Controller.Matches(result)) _scope = result;
    }
    private async Task PrepareAsync(Guid id, CancellationToken token)
    {
        var session = await _batch.PrepareAsync(id, token); if (session is null || _retiring) { session?.Dispose(); return; }
        CanonicalAnkiReviewWindow child;
        try { child = new CanonicalAnkiReviewWindow(session, _feature.RunAsync, _configure) { Owner = this }; }
        catch { session.Dispose(); throw; }
        _preparation = child;
        try { child.ShowDialog(); } finally { await child.RetireAsync(); _preparation = null; }
        if (!_retiring) await _batch.RefreshAsync(id, token);
    }
    private void StartAux(Func<CancellationToken, Task> action)
    {
        if (_retiring || _aux is not null || _batch.Busy) return;
        _uiError = null; var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _aux = completion.Task; Render(); _ = Run();
        async Task Run() { try { await action(_lifetime.Token); } catch (OperationCanceledException) { } catch (Exception error) { if (!_retiring) _uiError = error.Message; } finally { _aux = null; completion.TrySetResult(); if (!_retiring) Render(); } }
    }
    private void Stop()
    {
        if (_stopWork is not null) return;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _stopWork = done.Task; _ = Run();
        async Task Run()
        {
            try { await _batch.StopAsync(); done.TrySetResult(); }
            catch (Exception error) { _stopFailure = error; _uiError = error.Message; done.TrySetResult(); }
            finally { _stopWork = null; if (!_retiring) Render(); }
        }
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_allowClose) return; e.Cancel = true; if (!_batch.Busy && !_retiring) _ = RetireAsync(); }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess(); if (_retirement is not null) return _retirement;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _retirement = done.Task; _ = RetireCoreAsync(done); return done.Task;
    }
    private async Task RetireCoreAsync(TaskCompletionSource done)
    {
        _retiring = true; var errors = new List<Exception>();
        try { _lifetime.Cancel(); } catch (Exception error) { errors.Add(error); }
        try { await _batch.StopAsync(); } catch (Exception error) { errors.Add(error); }
        try { if (_preparation is { } child) await child.RetireAsync(); } catch (Exception error) { errors.Add(error); }
        try { if (_aux is { } auxiliary) await auxiliary; } catch (Exception error) { errors.Add(error); }
        try { if (_batch.Work is { } work) await work; } catch (Exception error) { errors.Add(error); }
        try { if (_stopWork is { } stop) await stop; } catch (Exception error) { errors.Add(error); }
        if (_stopFailure is { } stopFailure) errors.Add(stopFailure);
        try { _allowClose = true; Close(); } catch (Exception error) { errors.Add(error); }
        try { RetirePresentation(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count == 0) done.TrySetResult(); else done.TrySetException(errors);
    }
    private void RetirePresentation()
    {
        if (_retired) return; _retired = true;
        _batch.PropertyChanged -= BatchChanged; _feature.Settings.PropertyChanged -= SettingsChanged; _lifetime.Dispose();
    }
}
