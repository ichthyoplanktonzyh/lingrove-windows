# Full-notebook restore transaction

Reference: pinned macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift` (`restore`, `imageReferences`,
`commitWithImages`) and `VocabularyImageStorage.swift` (`boundedRead`, `validate`).
`CotabbyTests/Services/Vocabulary/VocabularyContentTests.swift` supplies the original reopen,
malformed/missing-media, stable review identity and damaged-media repair scenarios.

This archive slice adapts replacement of an entire notebook. [Store commands](VOCABULARY_BACKUP_STORE.md) now publish the acknowledged result and media token;
file chooser, replacement confirmation and backup/restore UI remain separate work. It does not merge records,
regenerate IDs, contact Anki or add cloud synchronization.

## Responsibilities and ordering

`VocabularyArchiveMediaPolicy` owns the original complete media walk through sense content,
legacy and preserved review plans, review cards and global templates. Archive garbage collection,
backup and restore use it so they retain the same resources. It preserves duplicate/source order;
each operation decides when it needs a set.

`VocabularyRestorePolicy` owns deterministic admission: envelope version must be 1, the entire
document must pass the existing graph validator, image filenames must exactly match the embedded
map, and the sum of actual embedded bytes must be at most 128 MiB. Returned references still need
native PNG validation. Decoding or constructing a backup alone never establishes admission.

`WindowsVocabularyBackupInput` owns one worker-only read-only disk handle to an explicitly chosen
source. It rejects directory/reparse leaves, checks 192 MiB before allocating, enforces the same
ceiling while reading, and denies concurrent write/delete access until the read completes. The Mac
URL regular-file guard rejects a symbolic leaf as `oversizedArchive`, before its additional
`O_NOFOLLOW` bounded read. Windows uses `FILE_FLAG_OPEN_REPARSE_POINT` and checks the opened handle.
Hard links remain valid inputs. No source ACL change, output file or notebook mutation occurs.

`VocabularyArchive.RestoreAsync` belongs to the existing serialized, draining archive owner. It
reads and decodes the package, runs pure admission, validates every embedded PNG's digest, byte
count, dimensions and container, then enters the existing `CommitWithImages` transaction. It
validates every resource before staging any resource. Cancellation begins at that original
staging boundary, so malformed/version failures retain their precedence even for a cancelled
call. Cancellation before SQL rolls back newly introduced files; an acknowledged SQL edit remains
successful. Revision conflicts preserve an external writer and roll back this restore's new media.
The commit publishes the complete graph and stable review/Anki identities before orphan cleanup.

Windows maps raw source IO and decoding failures to a fixed `InvalidArchive` without local path or
payload text. The corpus retains Mac's actual `io`/`decode` classes and maps them explicitly in the
counterpart tests; it does not pretend those platform exception types are identical. Existing
native PNG and private media/SQLite adapters retain their previously documented platform limits.

## Evidence and limits

The 37th actual-source verifier compiles the complete original archive, SQLite, ImageIO and media
implementations. Its 27 locked cases cover empty/plain/full graphs; shared, legacy, preserved,
card and template media; replacing an existing notebook; repairing damaged media; missing/extra
and traversal keys; corrupt bytes, digest/dimensions/count mismatches; version-before-graph
failure; invalid schema/graph/JSON; non-regular and oversized sources; symbolic/hard links; and
missing input. Packages are canonicalized only for stable fixture bytes after the real Codable
encoder. The exercised restore and IO bodies are unchanged. Setup uses private temporary files
and the real media writer/SQL commit.

Twenty-two portable cases verify the admission prefix. They deliberately admit the four packages
whose PNG bytes must fail later; pure policy tests do not claim WIC execution. Twenty-seven native
counterparts exercise real Windows handles/WIC/SQLite, comparing the complete post-restore graph,
reopen, revision delta, readable/present media, source preservation and staging cleanup. Five extra
cases cover cancelled admission/staging, stale-writer rollback and retired-owner rejection.
The size case sets the file length to 192 MiB + 1 without generating a payload and verifies
rejection at source admission; it does not assert filesystem allocation or measure process memory.
The exact 192 MiB boundary and 128 MiB aggregate boundary are not executed by this corpus.

Local validation: 22 targeted Core tests and the 27-case actual Mac verifier pass; the full Release
Windows-targeting solution cross-build passes with 0 warnings/errors. Native Windows execution is
pending CI, and physical chooser/confirmation interaction remains a desktop gate. No installer,
artifact upload, downloaded model or network test is part of this work.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyRestore
python3 scripts/verify-vocabulary-restore-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# Windows CI only:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyRestoreTests
```
