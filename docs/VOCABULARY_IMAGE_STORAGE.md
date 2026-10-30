# Vocabulary image storage library

This adapts the complete `Cotabby/Services/Vocabulary/VocabularyImageStorage.swift` at Mac pin
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. It supplies real image bytes and filesystem behavior;
the [archive transaction layer](VOCABULARY_MEDIA_TRANSACTIONS.md) now owns it; notebook image UI
remains the next slice. Preparing an image, constructing
storage, or failing to read missing media creates no directory or file. Nothing is sent or logged.

`WindowsVocabularyImageStorage` is an internal synchronous owner, intended to live once inside the
serialized archive. Null notebook selects private in-memory bytes; a named notebook selects
`<database filename>.media` in its parent directory. Inputs and returned memory bytes are detached.
`Read` validates bounded reference metadata, exact length, SHA-256, PNG signature, frame-zero type
and dimensions using the [native preparation boundary](VOCABULARY_IMAGE_PREPARATION.md).
It reads at most expected length plus one through one regular-file handle, even if the file grows
after its size was checked. Hash names are derived only after Core metadata admission.

`Write` validates a detached byte copy before filesystem mutation. A matching existing image is
reused without changing its timestamp. New files stage with a protected current-user ACL from
creation, flush before atomic same-volume publication, and return `true`. An existing corrupt
regular private file can be atomically repaired on supported local NTFS/TxF, returning `false`;
see the subsequent [identity-bound repair](VOCABULARY_OWNED_MEDIA_REPAIR_3B.md) for the shared
file-backed writer, exact newer reference and fail-closed capability limits. This preserves the
Mac transaction rule: a later metadata failure removes newly created media but retains repairs.
No-overwrite publication handles a concurrent winner by checking its actual bytes; an unsafe leaf
is never repaired. Random staging files are cleaned on success or failure. `Remove` is best effort;
cleanup cannot roll back already acknowledged metadata.

`VocabularyMediaDirectory` owns one operation's no-delete directory handle and checks directory
identity, ACL and both requested/canonical names. The media root and image leaf reject reparse
points. This stricter media-root rule is separate from the database adapter's supported parent
aliases. The existing `VocabularyWindowsFile` exposes its unchanged identity/ACL checks internally
so both owners use the same Windows admission rules. Its database/sidecar behavior is unchanged.

Windows ACLs adapt new Mac 0700 directories and 0600 files; existing unsafe permissions fail closed
without being modified. A repair publishes a newly private file. Read/repair/remove use regular
private leaf handles; native IO errors become fixed invalid-content failures. Operations belong off
the dispatcher and have no await while native handles are live. The archive integration
first admits its database parent and holds `VocabularySQLiteDatabase.Access(true, ...)` across
media staging, SQL commit and rollback/cleanup. The storage class alone is not an independent
cross-process transaction or a public arbitrary-path importer.

Evidence is deliberately scoped. Twelve observations in `vocabulary-image-storage-v1.json` execute
the complete original Mac storage, unchanged, with fixed shared PNG bytes in disposable memory or
temporary directories. They cover new/reused media, detached reads, reopening, corrupt repair,
removal, inert missing reads, invalid bytes, mismatched metadata, directory leaves, root/leaf links,
and oversized files. The Windows tests perform those same operations with actual WIC/file IO.
Read failure projects as unavailable because Mac filesystem failures include native Foundation
errors; no claim is made that native error types or encoder bytes match.

Seven additional Windows cases check private root/file ACLs and no-rewrite reuse, public directory and
file permission rejection, pinned-root rename prevention and ancestor-alias retarget detection,
four owners under the existing archive lock publishing one complete PNG, and invalid digest path
admission, plus a forced CREATE_NEW collision proving cleanup never deletes a file it did not
create. The collision fixes an independent review finding. These are native tests using actual files. The [archive transaction tests](VOCABULARY_MEDIA_TRANSACTIONS.md) separately cover SQL rollback and
shared-reference cleanup. Backup/restore,
editor import/paste/drop, visible image loading and review/export media remain unconnected. Crash
or hardware durability, adversarial mutation by the same Windows principal, broad codec/color
equivalence and interactive desktop behavior remain outside this evidence.

```sh
python3 scripts/verify-vocabulary-image-storage-macos-parity.py --mac-repo /path/to/macOS-lingrove
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# Windows CI executes the actual files/ACL/WIC operations:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyImageStorageTests
```
