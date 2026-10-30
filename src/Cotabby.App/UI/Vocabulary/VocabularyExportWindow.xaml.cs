using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// One owned export form adapts Mac VocabularyExportSheet. It edits only a session key, displays
/// its bounded literal preview and owns picker/cancellation lifetime. The shared Store/archive owns
/// all preparation and saving. Every admitted task is trackable before a modal picker starts, so
/// notebook retirement can cancel presentation and drain work without allowing a late Save.
/// </summary>
public partial class VocabularyExportWindow : Window
{
    private readonly VocabularyExportSession _session;
    private readonly IVocabularyExportDestinationPicker _picker;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _work = [];
    private CancellationTokenSource? _preparation;
    private TaskCompletionSource? _retirement;
    private bool _presenting = true, _retired, _forceClose, _started, _choosing, _operating;
    private string? _pickerMessage;
    private sealed record Option<T>(T Value, string Source, bool IsEnabled = true)
    {
        public string Label => InterfaceText.Get(Source, "Vocabulary");
        public override string ToString() => Label;
    }
    internal VocabularyExportSession Session => _session;
    internal Task LastPreparation { get; private set; } = Task.CompletedTask;
    internal Task LastOperation { get; private set; } = Task.CompletedTask;
    internal event Action<Task>? OperationStarted;

    internal VocabularyExportWindow(VocabularyStore store, VocabularyExportRequest request, IVocabularyExportDestinationPicker? picker = null)
        : this(request, (dispatcher, value) => new(dispatcher, store, value), picker) { }
    internal VocabularyExportWindow(Cotabby.Core.Vocabulary.Frozen.ICanonicalVocabularyExportStore store, VocabularyExportRequest request, IVocabularyExportDestinationPicker? picker = null)
        : this(request, (dispatcher, value) => new(dispatcher, value, store.PrepareExportAsync, store.WriteExportAsync), picker) { }
    private VocabularyExportWindow(VocabularyExportRequest request, Func<Dispatcher, VocabularyExportRequest, VocabularyExportSession> createSession, IVocabularyExportDestinationPicker? picker)
    {
        _session = createSession(Dispatcher, request); _picker = picker ?? new WindowsVocabularyExportDestinationPicker();
        InitializeComponent(); InterfaceText.TranslateInitialTree(this, "Vocabulary"); InterfaceText.ApplyDirection(this); WindowTheme.Follow(this);
        ScopeChoice.ItemsSource = new[] { new Option<VocabularyExportScope>(VocabularyExportScope.All, "Entire notebook", request.All.Count > 0),
            new(VocabularyExportScope.Filtered, "Current results", request.Filtered.Count > 0), new(VocabularyExportScope.Selected, "Selected senses", request.Selected.Count > 0) };
        FormatChoice.ItemsSource = new[] { new Option<VocabularyExportFormat>(VocabularyExportFormat.Text, "TXT"),
            new(VocabularyExportFormat.Csv, "CSV"), new(VocabularyExportFormat.Json, "JSON") };
        TextStyleChoice.ItemsSource = new[] { new Option<VocabularyExportTextStyle>(VocabularyExportTextStyle.Notes, "Learning notes"),
            new(VocabularyExportTextStyle.Expressions, "Expression list") };
        _session.Changed += Render; Closing += OnClosing; Closed += (_, _) => Retire();
        Loaded += (_, _) => { if (!_started && !_retired) { _started = true; Prepare(); } };
        Render();
    }
    private void OnOptionsChanged(object sender, RoutedEventArgs args)
    {
        if (_presenting || !IsInitialized || _retired || _choosing || _operating) return;
        if (ScopeChoice.SelectedValue is not VocabularyExportScope scope || FormatChoice.SelectedValue is not VocabularyExportFormat format
            || TextStyleChoice.SelectedValue is not VocabularyExportTextStyle style) return;
        // Capture controls before either session setter notifies Render; its old key must not
        // reset the user's other control values while this event is still being admitted.
        var configuration = new VocabularyExportConfiguration { Format = format, TextStyle = style, Meanings = MeaningsChoice.IsChecked == true,
            Examples = ExamplesChoice.IsChecked == true, Notes = NotesChoice.IsChecked == true, Explanation = ExplanationChoice.IsChecked == true,
            Content = ContentChoice.IsChecked == true, Images = ImagesChoice.IsChecked == true, SpreadsheetCompatibility = SpreadsheetChoice.IsChecked == true };
        _session.Scope = scope; _session.Configuration = configuration; Prepare();
    }
    private void Prepare()
    {
        CancelPreparation(); _pickerMessage = null;
        var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _preparation = work;
        LastPreparation = _session.PrepareAsync(work.Token); Track(LastPreparation);
        _ = LastPreparation.ContinueWith(_ => work.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private void Render()
    {
        if (_retired || !IsInitialized) return;
        _presenting = true;
        try
        {
            var c = _session.Configuration; ScopeChoice.SelectedValue = _session.Scope; FormatChoice.SelectedValue = c.Format; TextStyleChoice.SelectedValue = c.TextStyle;
            MeaningsChoice.IsChecked = c.Meanings; ExamplesChoice.IsChecked = c.Examples; NotesChoice.IsChecked = c.Notes;
            ExplanationChoice.IsChecked = c.Explanation; ContentChoice.IsChecked = c.Content; ImagesChoice.IsChecked = c.Images; SpreadsheetChoice.IsChecked = c.SpreadsheetCompatibility;
            TextStyleRow.Visibility = c.Format == VocabularyExportFormat.Text ? Visibility.Visible : Visibility.Collapsed;
            SpreadsheetChoice.Visibility = c.Format == VocabularyExportFormat.Csv ? Visibility.Visible : Visibility.Collapsed;
            ContentOptions.Visibility = c.IsExpressionList ? Visibility.Collapsed : Visibility.Visible; ImagesChoice.IsEnabled = c.Content;
            FormContent.IsEnabled = !_session.Saving && !_choosing && !_operating; CancelButton.IsEnabled = !_choosing;
            ExportButton.IsEnabled = _session.CanExport && !_choosing && !_operating;
            PreparationProgress.Visibility = _session.Loading ? Visibility.Visible : Visibility.Collapsed;
            BusyText.Text = InterfaceText.Get(_session.Saving ? "Saving…" : _session.Loading ? "Preparing preview…" : "", "Vocabulary");
            PreviewText.Text = _session.Package?.Preview ?? "";
            CountsText.Text = _session.Package is { } p ? InterfaceText.Format("Expressions: {0:N0}     Senses: {1:N0}     Usages: {2:N0}", p.EntryCount, p.SenseCount, p.ExampleCount) : "";
            FailureText.Text = _pickerMessage ?? _session.Message ?? ""; FailureText.Visibility = FailureText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _presenting = false; }
    }
    private void OnExport(object sender, RoutedEventArgs args)
    {
        if (_retired || _choosing || _operating || !_session.CanExport || _session.Package is not { } package) return;
        _pickerMessage = null; _choosing = _operating = true; Render();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LastOperation = completed.Task; Track(LastOperation); // Admission precedes a modal Shell call.
        _ = ChooseAndSaveAsync(package, completed);
    }
    private async Task ChooseAndSaveAsync(VocabularyExportPackage package, TaskCompletionSource completed)
    {
        try
        {
            string? path = await _picker.ChooseAsync(this, package, _lifetime.Token).ConfigureAwait(false);
            Task<bool>? saving = null;
            await OnUiAsync(() =>
            {
                _choosing = false;
                if (!_retired && _session.IsActive && path is not null) saving = _session.SaveAsync(package, path, _lifetime.Token);
                Render();
            }).ConfigureAwait(false);
            if (saving is not null && await saving.ConfigureAwait(false))
                await OnUiAsync(() => { if (!_retired && _session.IsActive) Close(); }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            await OnUiAsync(() => { if (!_retired) { _pickerMessage = "Could not choose an export destination. Try again."; Render(); } }).ConfigureAwait(false);
        }
        finally
        {
            try { await OnUiAsync(() => { _choosing = _operating = false; Render(); }).ConfigureAwait(false); }
            finally { completed.TrySetResult(); }
        }
    }
    private void OnCancel(object sender, RoutedEventArgs args) { if (!_retired && !_choosing) Close(); }
    private void OnClosing(object? sender, CancelEventArgs args) { if (_choosing && !_forceClose) args.Cancel = true; }
    private void CancelPreparation()
    { var old = _preparation; _preparation = null; try { old?.Cancel(); } catch (ObjectDisposedException) { } }
    private void Retire() => _ = RetireAsync();
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess();
        if (_retirement is not null) return _retirement.Task;
        var done = _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = RetireCoreAsync(done); return done.Task;
    }
    private async Task RetireCoreAsync(TaskCompletionSource done)
    {
        var errors = new List<Exception>(); _retired = true; _session.Changed -= Render;
        try { _session.Cancel(); } catch (Exception e) { errors.Add(e); }
        try { _lifetime.Cancel(); } catch (Exception e) { errors.Add(e); }
        try { CancelPreparation(); } catch (Exception e) { errors.Add(e); }
        try { _forceClose = true; Close(); } catch (Exception e) { errors.Add(e); }
        try { await DrainAsync(); } catch (Exception e) { errors.Add(e); }
        _lifetime.Dispose();
        if (errors.Count == 0) done.TrySetResult(); else done.TrySetException(new AggregateException(errors));
    }
    internal Task DrainAsync() { lock (_work) return Task.WhenAll(_work.ToArray()); }
    private void Track(Task task)
    {
        lock (_work) _work.Add(task);
        _ = task.ContinueWith(done => { _ = done.Exception; lock (_work) _work.Remove(done); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (OperationStarted is { } handlers) foreach (Action<Task> handler in handlers.GetInvocationList()) try { handler(task); } catch { }
    }
    private async Task OnUiAsync(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (Dispatcher.CheckAccess()) { action(); return; }
        try { await Dispatcher.InvokeAsync(action, DispatcherPriority.DataBind).Task.ConfigureAwait(false); }
        catch (OperationCanceledException) when (Dispatcher.HasShutdownStarted) { }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
}
