# Store backup and restore ownership

Reference: pinned macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Services/Vocabulary/VocabularyStore.swift` (`backup`, `restore`, `apply`, `report`,
`mediaRevision`), with the complete real archive/media/SQLite implementation.

`VocabularyStore.Backup.cs` adapts those two commands through the existing process-wide Store.
The Store owns admission, shared user-visible errors, notifications and shutdown tracking; the
[archive backup](VOCABULARY_BACKUP_ARCHIVE.md) and [restore transaction](VOCABULARY_RESTORE.md)
own IO, validation and acknowledgement. Views will borrow these commands after the separate
chooser/confirmation adaptation; they do not create another Store or archive.

Backup success deliberately does not apply a snapshot, mark an unloaded Store loaded, clear a
previous shared error, change pending capture keys or invalidate visible images. A failure reports
through the Store's fixed error boundary. Restore failure likewise preserves the acknowledged
graph and media token. Restore success first applies the acknowledged snapshot and then changes
the media token. That token changes even if a newer graph reply has already won the revision
guard: the acknowledged restore may have repaired bytes under an unchanged digest filename.
`VocabularyImageView` already observes this token and re-reads visible images when it changes.
The token getter and writer now share the Store's gate, avoiding a torn cross-thread Guid read.

Both commands use the existing admitted-work lifetime. Retirement refuses new commands and
drains admitted IO before disposing the archive. Closing a view cannot abandon a confirmed
restore or turn an acknowledged commit into cancellation. A retired UI dispatcher may discard
notifications while the durable result and Store state remain acknowledged. The Windows methods
return a Boolean acknowledgement for their existing UI command style; Mac's methods return Void.

Thirteen locked actual-source observations verify fresh/ordinary backup, protection failure,
old-error preservation, failure then successful backup, ordinary/full/empty/repeated restore,
failed restore, error recovery, damaged-image repair, and backup after restore. The verifier
executes original Store method bodies and the complete real archive, with only diagnostic logging
omitted as in the existing Store verifier. It compares full UI entries/templates, loaded/error/
pending state, revision delta, media-token change, actual image readability and semantic backup
JSON. Setup uses private temporary files and the real media/SQL owners. An absent-entry deletion
is a successful Mac no-op, so error setup uses a genuinely invalid sense operation.

The 13 Windows counterparts use real files/WIC/SQLite. Four additional cases cover a newer
snapshot winning before restore publication, a retired dispatcher, and shutdown during real
backup reads or restore writes. The two IO gates only control entry timing; they forward every
native operation and always release/drain in `finally`. No model, clipboard, network or package
generation participates.

Local full Release solution cross-build and the 13-case Mac source verifier are checked for this
slice; native Windows execution is pending CI. The separate chooser, replacement confirmation,
physical native interaction and other desktop verification gates remain open.

```sh
python3 scripts/verify-vocabulary-backup-store-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# Windows CI:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyBackupStoreTests
```
