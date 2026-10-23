# Full notebook backup snapshot and publication

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift` (`backup`, `backupSnapshot`,
`imageReferences`, `validateBackupDestination`) and the complete original archive/SQLite/image owners.

`VocabularyArchive.Backup.cs` adds the actual full-backup effect to the existing serialized archive.
It owns no second database or media service. The short destination lease validates protected names,
file identities and media-root descendants before the first notebook read. A shared storage span
then loads the acknowledged document and reads each unique image reference, including sense content,
legacy and independent review plans, preserved layouts and global templates. Orphan media is not
included. Actual media-reader failures precede the aggregate 128 MiB check, matching the original
ordering. The returned [backup value](VOCABULARY_BACKUP_CODEC.md) detaches those bytes and the graph.

After releasing that cross-owner span, the same worker encodes the complete envelope and checks its
actual encoded size against 192 MiB. It uses the [existing Windows atomic save lease](VOCABULARY_EXPORT_SAVE.md)
to stage with private ACLs, fsync, revalidate and publish by held file handle. It never writes SQL or
advances the notebook revision. Backup admission has no cancellation parameter, as the original
full-backup method does not cancel publication; archive retirement drains admitted backup and queued
edits before closing storage. The separate learning-material export retains its cancellable session.

Twenty-two fixture observations run the **complete original Mac archive, SQLite, ImageIO and atomic
file writer**. Fixture setup writes validated raw PNG bytes through the actual image owner and commits
the graph; it replaces no production method. Cases cover ordinary content, shared images, legacy and
preserved review layouts, independent cards, templates, all placements together, missing/mismatched
media, orphan exclusion, replacement, a safe sibling of the media root, all five protected notebook
names, hard/symbolic aliases and media-root/child refusal. The backup JSON and file/revision/no-stage
effects are captured from that source executable. IDs/coverage are locked in the verifier.

The Windows counterpart uses real private files, SQLite, WIC media validation and the native atomic
writer. It compares semantic backup JSON and the same file/revision outcomes. One platform error
class differs explicitly: Mac's missing-file read escapes as Foundation NSError (`io` in the corpus),
while the existing Windows media boundary sanitizes OS reader errors into fixed InvalidContent.
The test retains the original native category and asserts that Windows mapping; it does not claim
the exception types match. Both fail before creating/replacing an export. Typed invalid-content
refusals retain their existing domain failure.

Three extra Windows cases cover closed admission, one read of a shared reference across all plans
and templates, and a held native reader with queued deletion/retirement. The scheduling wrapper
forwards every read/write to the real image owner. It controls ordering only; it supplies no fake
image data. Retirement rejects new work, waits for the backup and deletion, and the saved backup keeps
its own document/media after the live notebook has removed them. These 25 cases bring the compiled
Windows suite to 388. The small corpus does not establish exact 128/192 MiB full-backup boundary
behavior or byte-identical encoding; the limits are enforced against actual Windows bytes. JSON
object order/formatting and framework Date precision remain the codec's documented boundaries.

```sh
python3 scripts/verify-vocabulary-backup-archive-macos-parity.py --mac-repo /path/to/mac-repo
# Native Windows CI, after building the solution:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyBackupArchiveTests
```

The [restore transaction](VOCABULARY_RESTORE.md) now adds bounded source reads, version/whole-graph/
exact-media admission and acknowledged staged replacement. Store commands and the UI's explicit
replacement confirmation remain work. Physical
native picker interaction and UIA/IME/ghost/insertion/GPU gates remain open. Backup writes are only
user-requested local effects; no model, clipboard, network, installer or downloadable build is added.
