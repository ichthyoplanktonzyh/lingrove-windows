using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// Notebook-owned backup panels and replacement confirmation from the stable Mac view. Only
/// presentation is revoked on hide/retirement. Once confirmed, the shared Store owns/drains IO;
/// no view token can undo an acknowledged replacement. Track before entering any modal frame,
/// and finish owned tasks without requiring a dispatcher after shutdown.
/// </summary>
public partial class VocabularyNotebookWindow
{
    private sealed class BackupPresentation : IDisposable
    {
        internal readonly CancellationTokenSource Lifetime = new();
        public void Dispose() => Lifetime.Dispose();
    }
    private BackupPresentation? _backupPresentation;
    private VocabularyRestoreConfirmationWindow? _restoreConfirmation;
    private bool _backupPanelFailed;
    // Tests supply controlled human replies; production always uses the actual owned panels.
    internal Func<bool, CancellationToken, Task<string?>>? ChooseBackupFile { get; set; }
    internal Func<string, CancellationToken, Task<bool>>? ConfirmRestore { get; set; }
    internal Task LastBackupOperation { get; private set; } = Task.CompletedTask;
    private bool CanPresentBackup => !_retired && IsVisible && _store.IsLoaded && _backupPresentation is null;
    private bool OwnsBackup(BackupPresentation presentation) => !_retired && IsVisible
        && ReferenceEquals(_backupPresentation, presentation) && !presentation.Lifetime.IsCancellationRequested;
    private void UpdateBackupAdmission()
    {
        if (!IsInitialized) return;
        BackupButton.IsEnabled = RestoreNotebookButton.IsEnabled = CanPresentBackup;
    }
    private void OnBackupNotebook(object sender, RoutedEventArgs args) => BeginBackup(restore: false);
    private void OnRestoreNotebook(object sender, RoutedEventArgs args) => BeginBackup(restore: true);
    private void BeginBackup(bool restore)
    {
        Dispatcher.VerifyAccess(); if (!CanPresentBackup) return;
        if (_backupPanelFailed)
        {
            _backupPanelFailed = false; string? error = _store.ErrorMessage;
            StorageError.Text = error ?? ""; StorageError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        }
        var presentation = new BackupPresentation(); _backupPresentation = presentation;
        // The worker can only enqueue its first UI callback. This owner thread records the task
        // before returning to that queue, so a nested Shell/confirmation frame is already owned.
        LastBackupOperation = Task.Run(() => RunBackupAsync(presentation, restore));
        Track(LastBackupOperation); UpdateBackupAdmission();
    }
    private async Task RunBackupAsync(BackupPresentation presentation, bool restore)
    {
        var token = presentation.Lifetime.Token;
        try
        {
            var choosing = await BackupUiAsync(() => ChooseAndAdmitBackupAsync(presentation, restore), token).ConfigureAwait(false);
            Task<bool>? operation = await choosing.ConfigureAwait(false);
            if (operation is not null) await operation.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
        catch (Exception)
        {
            try
            {
                await BackupUiAsync(() =>
                {
                    if (OwnsBackup(presentation))
                    { _backupPanelFailed = true; StorageError.Text = "The backup file panel could not be opened."; StorageError.Visibility = Visibility.Visible; }
                    return true;
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
        }
        finally
        {
            try
            {
                try { await BackupUiAsync(() =>
                {
                    if (ReferenceEquals(_backupPresentation, presentation)) _backupPresentation = null;
                    UpdateBackupAdmission(); return true;
                }, CancellationToken.None).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
            }
            finally { presentation.Dispose(); }
        }
    }
    private Task<Task<bool>?> ChooseAndAdmitBackupAsync(BackupPresentation presentation, bool restore)
    {
        if (!OwnsBackup(presentation)) return Task.FromResult<Task<bool>?>(null);
        var choosing = ChooseBackupFile?.Invoke(restore, presentation.Lifetime.Token) ?? ChooseBackupFileAsync(restore, presentation.Lifetime.Token);
        // Native Show returns on this owner thread. Accept a selected backup in the same callback,
        // before the next close/hide message can cancel presentation. Async test replies must first
        // return to this thread and pass the live-presentation guard before their intent is accepted.
        if (choosing.IsCompletedSuccessfully) return AcceptBackupPathAsync(presentation, restore, choosing.Result);
        return AwaitPath();
        async Task<Task<bool>?> AwaitPath()
        {
            string? path = await choosing.ConfigureAwait(false);
            var accepted = await BackupUiAsync(() => AcceptBackupPathAsync(presentation, restore, path), presentation.Lifetime.Token).ConfigureAwait(false);
            return await accepted.ConfigureAwait(false);
        }
    }
    private Task<Task<bool>?> AcceptBackupPathAsync(BackupPresentation presentation, bool restore, string? path)
    {
        if (!OwnsBackup(presentation) || path is null) return Task.FromResult<Task<bool>?>(null);
        if (!restore) return Task.FromResult(AdmitBackup(presentation, restore: false, path));
        if (ConfirmRestore is null) return Task.FromResult(ShowRestoreConfirmation(presentation, path));
        var confirming = ConfirmRestore(path, presentation.Lifetime.Token);
        if (confirming.IsCompletedSuccessfully) return Task.FromResult(confirming.Result ? AdmitBackup(presentation, restore: true, path) : null);
        return AwaitConfirmation();
        async Task<Task<bool>?> AwaitConfirmation()
        {
            if (!await confirming.ConfigureAwait(false)) return null;
            return await BackupUiAsync(() => AdmitBackup(presentation, restore: true, path), presentation.Lifetime.Token).ConfigureAwait(false);
        }
    }
    private Task<bool>? AdmitBackup(BackupPresentation presentation, bool restore, string path)
    {
        if (!OwnsBackup(presentation)) return null;
        var admitted = restore ? _store.RestoreAsync(path) : _store.BackupAsync(path);
        _backupPresentation = null; UpdateBackupAdmission(); return admitted;
    }
    private Task<string?> ChooseBackupFileAsync(bool restore, CancellationToken token)
    {
        using var dialog = new WindowsVocabularyBackupDialog(restore);
        return Task.FromResult(dialog.Show(new WindowInteropHelper(this).EnsureHandle(), action =>
        {
            if (Dispatcher.CheckAccess()) action(); else _ = Dispatcher.BeginInvoke(action, DispatcherPriority.Send);
        }, token));
    }
    private Task<bool>? ShowRestoreConfirmation(BackupPresentation presentation, string path)
    {
        Task<bool>? admitted = null;
        var confirmation = new VocabularyRestoreConfirmationWindow(() => admitted = AdmitBackup(presentation, restore: true, path)) { Owner = this };
        _restoreConfirmation = confirmation;
        try { confirmation.ShowDialog(); return admitted; }
        finally { _restoreConfirmation = null; }
    }
    private Task<T> BackupUiAsync<T>(Func<T> operation, CancellationToken token)
    {
        if (Dispatcher.HasShutdownStarted) return Task.FromCanceled<T>(new(true));
        return Dispatcher.InvokeAsync(operation, DispatcherPriority.Normal, token).Task;
    }
    private void RetireBackupPresentation()
    {
        try { _backupPresentation?.Lifetime.Cancel(); }
        catch (ObjectDisposedException) { /* Its worker may finish after the dispatcher retires. */ }
        var confirmation = _restoreConfirmation; _restoreConfirmation = null;
        confirmation?.Close(); UpdateBackupAdmission();
    }
}
