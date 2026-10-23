# Archive export selection and media snapshots

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Services/Vocabulary/VocabularyArchive+Export.swift`, `prepareExport` and `exportSnapshot`.
The original archive/storage actor, image owner, policy, encoders and package declarations remain
the behavior reference. Destination validation, `writeExport` and ZIP are later effects.

`VocabularyArchive.Export.cs` adds `PrepareExportAsync` to the existing serialized archive owner.
The call freezes offered selection IDs before queueing. On its worker, one shared persistence access
span covers load/refresh, exact entry/sense selection, the full-reference media budget and all media
reads. This prevents edits/GC from changing the snapshot during collection, including cooperating
writers with separate archive owners over the same named notebook. Rendering happens after that
shared access span, while the archive operation still owns its serial lease. It uses detached entry
values and media bytes, so later deletion cannot change a prepared preview/save payload.

Media selection uses the existing policy: selected supplemental sense content only; no implicit
review/template collection. Complete reference values deduplicate, and the aggregate declared bytes
must fit 128 MiB before reading any image. Each image owner validates the complete reference and
returns detached bytes. Reader failures map to the fixed `missingImage` export failure, as in the
Mac catch boundary. Cancellation checks are outside that reader catch: selected IDs, before every
image, after the snapshot, package preparation and after media attachment retain their source order.
The cancelled source probes use a controlled start gate, so cancellation is set before the actor
call can begin. An empty selected scope still reports `empty` before cancellation; the pure package factory has a
separate initial cancellation contract. Preparing performs no SQL mutation and retains revision.

The returned [package](VOCABULARY_EXPORT_PACKAGE.md) owns encoded material and all captured images.
The archive still tracks the whole preparation as admitted work: closing rejects new work and drains
existing operations before closing SQLite. Cancelling a caller does not silently publish partial
media or redirect the selection to a merged alias. This slice adds no Store callback, export command,
window, destination, file write, ZIP generation, full backup, inference or network transmission.

Twenty-one fixture probes execute the complete original Mac archive with actual SQLite/ImageIO memory
owners and a fixed literal PNG. All relevant bodies remain original, including the complete ZIP
implementation only to satisfy the unexercised write method's dependency. Source preparation calls
never invoke write/ZIP. The fixtures compare prepared data digest/count, exact media names/hashes,
counts and unchanged revision. Cases include missing/mismatched media, flags and selected-sense
exclusion, shared-image dedup, order/duplicate selection, retired aliases/stale IDs, cancellation,
exact 128 MiB metadata admission versus +1 rejection, and package ownership after actual source deletion.

The exact-limit metadata case proceeds to the missing-image reader boundary; no 128 MiB PNG set is
claimed to pass image validation. The +1 case fails budget admission before reading.

Windows CI executes those twenty-one corresponding archive + WIC/media cases and an additional named
file/lock regression. Its forwarding reader pauses only after a real validated image read; a second
archive must receive `StorageBusy` when trying to delete while the first shared snapshot span is
held. After release, deletion succeeds and the package retains the original bytes. These are actual
Windows storage/codec tests; macOS can cross-compile them but cannot execute them.

Six portable App tests run real memory SQLite and the production export sequencing with an explicit
opaque media-reader boundary. They verify lazy media reads, captured queued scopes, serialized GC,
drain-on-close, fixed reader failure mapping and budget-before-read. Their literal three-byte media
values are deliberately not PNGs and make no image admission, codec or filesystem claim.

```sh
python3 scripts/verify-vocabulary-export-snapshot-macos-parity.py --mac-repo /path/to/mac-repo
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExportSnapshotLifetimeTests
# Native Windows CI:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExportSnapshotTests
```

The Windows test execution is established by final-head CI, not by cross-compilation. The
[ZIP adapter](VOCABULARY_EXPORT_ZIP.md) separately covers the stored byte format. Destination/path
admission, atomic save/cancellation, backup/restore, stale save-session guards and the export window
remain follow-up work. Physical desktop UIA/IME/overlay/insertion and model/GPU
gates remain open.
