# Canonical notebook backup and restore

Reference: macOS `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Lingrove/Services/Vocabulary/VocabularyArchive+Media.swift`, `VocabularyArchive.swift`
(`commitUnlocked(restoringBackup:)`), `VocabularyStore.swift`, `VocabularyContentModels.swift`,
and `VocabularyNotebookView.swift`. The old entry-owned backup library and historical fixtures
retain their own references; no legacy Windows file is read or converted by this workflow.

The notebook's Export and backup menu opens the native `.lingrove` Save/Open panel. Restore needs
an explicit replacement confirmation; Cancel or closing the chooser performs no Store operation.
After acceptance, the one canonical Store owns and drains the transaction even if the window closes.
A successful restore returns its actual revisioned snapshot and applies it through the browser's
existing revision gate; a failed restore leaves the previously acknowledged snapshot in place.
Backup does not advance revision for an already-loaded Store and writes only a user-chosen file.

`FrozenVocabularyBackupCodec` wraps the real current-format `VocabularyDocumentCodec` and detached
base64 image bytes in the source's version-one envelope. It retains v8 resource ownership, UUIDs,
raw uncollected source/occurrence/interpretation evidence, review plans and bindings, classification
guidance and native `SourceDate` Double values. It accepts only the current document schema in this
campaign. The transport is bounded at 192 MiB and the media set at 128 MiB. Restore validates the
complete graph, exact media names and each PNG before staging; it installs the enabled bundled
catalog and skips reconciliation against the notebook being replaced, while retaining the source's
exact-input explanation pruning. An invalid input never reaches SQL or media staging.

The Store serializes snapshot/media capture, staging and SQL through its existing named-file lock.
Only newly published image identities are rolled back on failure; successful SQL acknowledgement
precedes publication and cleanup. Destination publication uses a same-directory private staging
file and handle-bound rename. Admission protects the current database, sidecars and explicit
`media-v8` directory, and refuses the known old `vocabulary.sqlite` sibling and sidecars by path
without opening that old file. Its named old `.media` path is also refused. Existing corrupt media
at the same digest path now supports [identity-bound atomic repair](VOCABULARY_OWNED_MEDIA_REPAIR_3B.md)
on local NTFS with available TxF. Repair survives later SQL failure/cancellation; unsupported
capability or a conflicting object fails closed. Physical Shell behavior and current-format
cross-platform file interchange are not established by these checks.

Tests use isolated current-format named SQLite and synthetic images. They cover complete graph and
Double roundtrip, classification guidance and uncollected evidence, restore versus ordinary evidence
reconciliation, protected destinations, missing/malformed inputs, post-publication failure and
cancellation rollback, reopen, explicit confirmation, acknowledgement publication and accepted
restore drain. Optional 96-DPI theme captures are enabled by `LINGROVE_TEST_RENDER_DIR`; physical
Windows Shell, accessibility and desktop acceptance remain open.

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyBackupTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyBackupStoreTests|FullyQualifiedName~VocabularyExportFileTests'
```
