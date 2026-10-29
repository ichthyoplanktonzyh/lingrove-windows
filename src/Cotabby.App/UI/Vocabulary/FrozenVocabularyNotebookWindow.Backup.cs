using Cotabby.App.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>The notebook owns chooser/confirmation presentation; the shared canonical Store owns
/// accepted backup and restore work. Closing cancels unaccepted presentation and joins commits.</summary>
internal sealed partial class FrozenVocabularyNotebookWindow
{
    private readonly Button _backupMenu = new() { Name = "canonicalBackupMenu", Content = InterfaceText.Get("Export and backup"), Padding = new Thickness(10, 5, 10, 5) };
    private readonly TextBlock _backupError = new() { Name = "canonicalBackupError", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _backupCancellation;
    private TaskCompletionSource? _backupCompletion;
    private VocabularyRestoreConfirmationWindow? _backupConfirmation;
    private bool _backupAccepted;
    private Exception? _backupCancellationFailure;
    internal Func<bool, CancellationToken, Task<string?>>? ChooseCanonicalBackupFile { get; set; }
    internal Func<string, CancellationToken, Task<bool>>? ConfirmCanonicalRestore { get; set; }
    internal Task? LastCanonicalBackupOperation { get; private set; }
    private void InitializeCanonicalBackup(Panel toolbar)
    {
        VocabularyNotebookTheme.Button(_backupMenu); _backupMenu.Margin = new Thickness(0, 0, 8, 8);
        VocabularyNotebookTheme.Brush(_backupError, TextBlock.ForegroundProperty, "OrangeBrush"); _backupError.Margin = new Thickness(0, 0, 8, 8);
        var menu = new ContextMenu(); var backup = new MenuItem { Header = InterfaceText.Get("Full notebook backup…"), Name = "canonicalBackup" }; var restore = new MenuItem { Header = InterfaceText.Get("Restore notebook…"), Name = "canonicalRestore" };
        backup.Click += (_, _) => StartCanonicalBackup(false); restore.Click += (_, _) => StartCanonicalBackup(true); menu.Items.Add(backup); menu.Items.Add(new Separator()); menu.Items.Add(restore);
        _backupMenu.ContextMenu = menu; _backupMenu.Click += (_, _) => { if (_backupMenu.IsEnabled) { menu.PlacementTarget = _backupMenu; menu.IsOpen = true; } };
        toolbar.Children.Add(_backupMenu); toolbar.Children.Add(_backupError); UpdateCanonicalBackup();
    }
    private void UpdateCanonicalBackup()
    { _backupMenu.IsEnabled = !_retiring && _modalDone is null && _session.Snapshot is not null && _session.Failure is null && _store is ICanonicalVocabularyBackupStore; }
    internal void StartCanonicalBackup(bool restore)
    {
        Dispatcher.VerifyAccess(); if (_retiring || !IsVisible || _modalDone is not null || _backupCompletion is not null || _session.Snapshot is null || _store is not ICanonicalVocabularyBackupStore storage) return;
        var done = _backupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously); var lifetime = _backupCancellation = new CancellationTokenSource(); _modalDone = done; _backupAccepted = false; _backupCancellationFailure = null; _backupError.Text = "";
        LastCanonicalBackupOperation = Track(Run()); UpdateCanonicalBackup();
        async Task Run()
        {
            try
            {
                string? path = await (ChooseCanonicalBackupFile?.Invoke(restore, lifetime.Token) ?? ChooseBackupAsync(restore, lifetime.Token));
                if (_retiring || _hideRequested || lifetime.IsCancellationRequested || path is null) return;
                Task? accepted = null; Task<FrozenVocabularyImportSnapshot>? restored = null;
                if (!restore) { _backupAccepted = true; _backupError.Text = "Backing up…"; accepted = storage.BackupAsync(path, CancellationToken.None); }
                else if (ConfirmCanonicalRestore is { } confirm)
                {
                    if (await confirm(path, lifetime.Token) && !_retiring && !_hideRequested && !lifetime.IsCancellationRequested) { _backupAccepted = true; _backupError.Text = "Restoring…"; accepted = restored = storage.RestoreAsync(path, CancellationToken.None); }
                }
                else
                {
                    var confirmation = new VocabularyRestoreConfirmationWindow(() => { if (!_retiring && !_hideRequested && !lifetime.IsCancellationRequested) { _backupAccepted = true; _backupError.Text = "Restoring…"; accepted = restored = storage.RestoreAsync(path, CancellationToken.None); } }) { Owner = this };
                    _backupConfirmation = confirmation; try { confirmation.ShowDialog(); } finally { _backupConfirmation = null; }
                }
                if (accepted is not null) { await accepted; if (!_retiring && restored is not null) _session.AcceptAcknowledgement(await restored); if (!_retiring) _backupError.Text = ""; }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_retiring) _backupError.Text = error.Message; }
            finally
            {
                _backupConfirmation = null; _backupCancellation = null; lifetime.Dispose(); _backupCompletion = null; _modalDone = null;
                try { if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } if (!_retiring) { _session.Refresh(); UpdateCanonicalBackup(); } } finally { done.TrySetResult(); }
            }
        }
    }
    private Task<string?> ChooseBackupAsync(bool restore, CancellationToken token)
    {
        using var dialog = new WindowsVocabularyBackupDialog(restore);
        return Task.FromResult(dialog.Show(new WindowInteropHelper(this).EnsureHandle(), action =>
        { if (Dispatcher.CheckAccess()) action(); else if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(action, DispatcherPriority.Send); }, token));
    }
    private void CancelCanonicalBackupPresentation()
    {
        if (_backupAccepted) return;
        try { _backupCancellation?.Cancel(); } catch (ObjectDisposedException) { } catch (Exception error) { _backupCancellationFailure = error; }
    }
    private async Task RetireCanonicalBackupAsync()
    {
        var errors = new List<Exception>();
        try { _backupCancellation?.Cancel(); } catch (Exception error) { errors.Add(error); }
        try { _backupConfirmation?.Close(); } catch (Exception error) { errors.Add(error); }
        try { if (_backupCompletion is { } completion) await completion.Task; } catch (Exception error) { errors.Add(error); }
        if (_backupCancellationFailure is { } cancellationFailure) errors.Add(cancellationFailure);
        if (errors.Count > 0) throw new AggregateException(errors);
    }
}
