# Notebook backup and confirmed restore

Reference: pinned macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/UI/Vocabulary/VocabularyNotebookView.swift` toolbar, `backup()`, `chooseBackup()` and
the `Replace notebook?` alert. The [shared Store](VOCABULARY_BACKUP_STORE.md) and archive retain
their separately verified transaction responsibilities.

The existing Windows notebook toolbar now offers `Full notebook backup…` and `Restore notebook…`
beside its export command. This follows the existing Windows wrapping toolbar layout; Mac groups
these same commands in an export/backup menu. Both require a loaded Store, including an empty
notebook. They operate on the complete notebook rather than the current filters, selected rows
or batch. Backup captures the current archive when the chosen destination is handed to the Store.

`VocabularyNotebookWindow.Backup.cs` owns one transient panel/confirmation presentation. A native
[Save/Open adapter](VOCABULARY_BACKUP_DIALOGS.md) returns the path; restore then opens the owned
replacement confirmation with the original message about images and Anki associations. Cancel is
the default action. Merely selecting a backup cannot replace anything.

An accepted Save result admits backup in the same UI callback. The actual confirmation's Restore
button synchronously reports intent to the notebook, which admits restore before `DialogResult`
closes the modal window. No later presentation-token callback can revoke that handoff. After
admission, the shared Store owns IO and its shutdown drain; the panel slot is released so later
commands can queue like Mac Tasks. The notebook still tracks every admitted operation until it
finishes. Closing or hiding before acceptance cancels only the active panel/confirmation and
rejects late replies, including after reopening the reusable notebook.

The notebook starts orchestration on a worker whose first action can only post to its UI owner.
It synchronously records that task before returning to the message queue, so native or WPF modal
frames cannot outrun lifetime tracking. Each presentation owns a disposable cancellation source.
Async test-controlled human replies return to the UI and recheck that source before being
accepted. Real native Save and the real confirmation accept synchronously. After admission,
archive work and cleanup do not depend on a live dispatcher; UI updates may be discarded during
shutdown. Panel startup errors stay local and sanitized, and retry restores the current shared
Store error instead of clearing it.

`VocabularyRestoreConfirmationWindow` owns only the literal confirmation view and a synchronous
intent callback. It has no Store, archive, bytes or path authority; its notebook owner captures
the exact source and decides whether it is still an active presentation.

Fourteen WPF cases run the compiled notebook and confirmation with actual Store/files/media/SQL.
Only human file-panel replies are controlled; no native Shell modal Show runs. Cases cover loaded
empty admission, cancellation/retry, full backup while filtered, exact path and explicit confirm,
the actual owned Cancel/Restore buttons, confirmation followed immediately by closing, accepted
Save/Restore replies followed by a queued owner hide, late picker/confirmation replies, retirement
closing a confirmation, draining already confirmed native restore IO, and sanitized panel retry.
The native-write gate forwards actual media IO and always releases/drains in `finally`.

With the twelve [native panel startup cases](VOCABULARY_BACKUP_DIALOGS.md), this round adds 26
WPF cases (463 total). Existing export-dialog tests are unchanged. Local Windows-targeting Release
cross-build passes with 0 warnings/errors; native execution is pending CI. Physical Shell
navigation/user cancellation and an interactive Windows backup/restore walkthrough remain desktop
gates, alongside the broader UIA/IME/ghost/insertion/GPU gaps. No installer, uploaded artifact,
downloaded model or network test is added.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# Windows CI, actual WPF dialogs with controlled file-panel replies:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyNotebookBackupTests
```
