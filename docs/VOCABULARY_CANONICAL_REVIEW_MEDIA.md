# Canonical review media transactions

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/Vocabulary/VocabularyArchive+Media.swift`, `VocabularyArchive.swift` commit,
`VocabularyImageStorage.swift`, and the corresponding vocabulary media tests. The single canonical
Store owns the database and `media-v8` directory next to `vocabulary-v8.sqlite`; no legacy media
path is opened. Construction and unopened disposal do not access the directory. Existing Windows
private ACL, file identity, reparse refusal, bounded reads, WIC PNG validation and atomic staging
are reused under the database's cross-process lock.

Review and template writes retain supplied detached images before admission. Under one exclusive
span the Store reads the latest graph, builds its draft, validates content and the 128 MiB unique
media limit, stages or verifies new references, checks cancellation and commits SQL. Failure rolls
back only files created by that attempt, identified by their actual Windows volume/file ID. Existing
valid files survive. Corrupt existing media is rejected: replacing it safely by handle has not yet
been implemented, so the source's repair behavior is an explicit gap.
After SQL acknowledgement, files with no remaining filename reference are removed best-effort and
the revision is published. A cleanup failure cannot undo an acknowledged write. The reference walk
includes collection content, current and preserved plans of legacy review and independent cards,
and templates. The operation never removes raw evidence. The `ReadMediaSnapshotAsync` export
boundary reads the graph and validated detached image bytes under the same storage span; individual
image reads also hold the database lock. Prepared-image inputs and returned byte arrays are copied.

This implements review/template image transactions, not the notebook editor, backup/export UI or
production activation of those features. Windows image preparation/decoding retains its documented
native subset; profiled/compressed variants remain incomplete. Existing referenced image files are
not revalidated by every unrelated mutation, as in the source. Remote Anki audio receipts are
metadata and are not local media files managed by this Store. No model or user notebook is involved.

Windows isolated named-file tests cover real PNG staging, shared-reference reuse, reopen/read,
last-reference cleanup, SQL rollback preserving reused media, cancellation after staging, private
staging-name collision and graph/media snapshot copying. Identity regressions replace a leaf with
another file containing identical bytes before rollback or cleanup; the replacement survives. A
delete-handle test also verifies that replacement is denied between identity check and disposition.
Failures after publication but before the write receipt is returned also trigger identity-bound
best-effort rollback; a replacement leaf is preserved.
They do not establish physical UI or
process-crash recovery. Native Windows ACL/reparse coverage of the reused image effect remains in
`VocabularyImageStorageTests`.

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalVocabularyMediaTests|FullyQualifiedName~CanonicalWritingFavoriteViewTests'
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyReviewStoreTests'
```

Independent review accepted the file-identity and post-publication failure repairs after isolated
WPF checks passed 35/35. Final integration source `ad4407f` on Windows x64, .NET SDK 10.0.403,
ran `dotnet build Cotabby.sln -c Release --verbosity quiet` with ordinary dependency restore:
zero warnings/errors. (The preceding no-restore attempt had missing assets in this checkout.)
Compiled WPF filter `CanonicalVocabularyMediaTests|VocabularyImageStorageTests|CanonicalVocabularyCompositionTests|CanonicalWritingFavoriteViewTests`
passed 37/37; Inference filter `FrozenVocabularyReviewStoreTests|VocabularyWindowsFileTests` passed
42/42. Both used Release, `--no-build --no-restore`, telemetry disabled and clipboard reads disabled.
No model, endpoint or existing notebook was used. These checks do not activate canonical review UI.
