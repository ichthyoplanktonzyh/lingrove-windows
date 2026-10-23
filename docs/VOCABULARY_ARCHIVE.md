# Serialized vocabulary archive

`Cotabby.Win32.Vocabulary.VocabularyArchive` adapts the pinned Mac archive actor to an async C#
owner. It composes the real SQLite/file adapter with the already ported save, sense, edit, pruning
and whole-graph validation policies. The [shared Store](VOCABULARY_STORE.md) now provides AppHost publication; user-visible Save/notebook remains the next layer;
this round introduces no duplicate runtime, observer, network request or per-keystroke disk activity.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac source responsibility | Windows boundary |
| --- | --- |
| `Services/Vocabulary/VocabularyArchive.swift` actor and lazy persistence | `Win32/Vocabulary/VocabularyArchive.cs`, one serial async queue and owned database |
| `load`, cached revision comparison and full semantic validation | `LoadAsync`, immutable `VocabularySnapshot` |
| `save`, exact `remove`, entry deletion and explanation attachment | Existing Core policies, followed by one validated commit |
| sense create/remove/merge/move, three-way sheet update | Existing Core draft/sense policies, preserving concurrent captures and stable IDs |
| `transactionBaseline`, `commit`, explanation pruning | Cached baseline retained through revision CAS; cache publishes only after SQLite COMMIT |
| actor lifetime | Explicit admission close, drain of admitted edits, off-UI database disposal |

Construction is inert. A null path explicitly selects an isolated in-memory test notebook; a named
path uses protected Windows file admission. AppHost owns one instance
for the application lifetime. A semaphore serializes the complete read/modify/commit operation;
SQLite/native file work runs on a worker, outside WPF. No native lock or SQL transaction crosses an
await. Failures release the queue and preserve the previous acknowledged cache.

A candidate's baseline must not be refreshed during commit: an external writer could otherwise
be mistaken for the candidate's starting revision and have its changes overwritten. Revision CAS
rejects that stale candidate; the next load refreshes the winning graph. Exact removal of a missing
capture and an explanation with no matching saved target are read-only no-ops. Explicit entry
removal still commits even for a missing ID, matching the source. Generated records retain the
original policy's protection for user-authored meanings.

`DisposeAsync` closes admission immediately, drains operations already admitted, then closes
SQLite on a worker. Hiding a view does not cancel a half-completed durable edit. Repeated disposal
shares the same task. New calls after closing receive a fixed archive failure.

The [media transaction layer](VOCABULARY_MEDIA_TRANSACTIONS.md) now validates prepared image bytes,
stages them under the exclusive archive lock and joins SQL acknowledgement with rollback and old
resource cleanup. Existing text-only edits still retain old image references without rereading every
file. The internal metadata commit boundary remains for fixtures; it is not a public media importer.
Editor image UI, review/template mutations, backup/export and CLI remain unconnected. This does not
assert cross-platform notebook file compatibility.

`vocabulary-archive-v1.json` contains 22 observations captured by executing the complete original
Mac actor, actual SQLite classes and original image storage in isolated memory mode. The verifier
adds only test driver hooks for fault/interleaving setup; it does not replace load, mutation, commit,
validation or media algorithms. The original database's diagnostic logging removal is unchanged.
The shared rich input graph is constructed in Swift; C# reconstructs the corresponding actual Mac
row/codec fixtures. Expected outcomes never come from the C# implementation.

The cases cover fresh state, favorite and usage identity, duplicate save, no-op distinctions,
sense ownership, concurrent sheet captures/conflicts, pruning, SQL-trigger rollback, stale external
revision, invalid load/commit, missing media and generated/user-authored explanation behavior.
Six additional portable tests cover inert lifetime, worker scheduling, 31 concurrent captures,
queue recovery, close/drain, and invalid-cache recovery using actual SQLite. Two Windows-only tests
exercise archive Save/dispose/reopen and two independent archive owners against named files.
Native desktop UIA, IME, insertion, ghost pixels and GPU behavior remain separate verification.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release
python3 scripts/verify-vocabulary-archive-macos-parity.py --mac-repo /path/to/macOS-lingrove
# Windows CI also executes:
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyArchiveWindowsTests
```
