# Vocabulary media transactions

`VocabularyArchive.Images.cs` adapts `Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift`
and archive `update`/`commitUnlocked` at Mac pin `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.
The existing archive now owns real [image storage](VOCABULARY_IMAGE_STORAGE.md). This connects
transaction behavior, not editor image import or visible images, backup/restore, or review mutations.

`VocabularyArchive.Windows.cs` composes SQLite and the WIC/file adapter for the same notebook path,
including real memory-mode image bytes. The constructor remains inert. It lives separately so
portable archive tests can compile the real transaction owner and inject actual memory SQLite
without pretending to execute Windows codecs. Those tests now name the memory database factory
explicitly. The internal `IVocabularyImageStorage` contract contains synchronous prepare/read/write/
remove effects only; the archive owns serialization, worker scheduling, database locks and lifetime.
There is no second store, runtime, worker observer, network request or UI publication mechanism.

`PrepareImageAsync` detaches at most 24 MiB before queuing bounded normalization. It never opens
SQLite or writes media; discarding a prepared draft leaves no resources. `ReadImageAsync` holds
the archive's shared access lock while reading/validating bytes. `UpdateAsync` snapshots an optional
dictionary of immutable prepared images before queuing the existing three-way edit. The editor's
current text-only calls remain valid; a later UI slice can pass its draft images at Save.

The exclusive `VocabularySQLiteDatabase.Access(true, ...)` spans all of these steps:

1. Validate sense content, review layouts and template media. Whole-graph validation waits for
   commit's existing stale-explanation pruning, preserving legitimate edits to their source input.
2. Bound distinct references to the original 128 MiB budget. Reference equality includes digest,
   dimensions and byte count; repeated references share the resource budget.
3. Stage only referenced supplied bytes. A newly referenced unsupplied image must already be
   readable. An existing metadata reference without supplied bytes is retained without rereading,
   even if its file has become unavailable, matching Mac. Unreferenced supplied drafts are ignored.
4. Check cancellation before every stage and immediately before committing. SQLite begins the
   durable acknowledgement boundary; a later cancellation cannot turn its success into failure.
5. Commit against the cached candidate revision, publish cache/revision only after SQL succeeds,
   then best-effort remove old files whose filenames have no remaining references.

Any precommit failure removes only writes that reported a new file. Repairs report existing and
survive SQL failure or cancellation. Cleanup failure after commit leaves an orphan and never
rolls back metadata. Same-filename references with inconsistent dimensions retain Mac's exact
metadata semantics: no new global uniqueness rule was invented, and cleanup retains by filename.
Such an inconsistent reference fails actual byte validation; this is documented behavior, not a
claim that malformed metadata has usable image bytes.

Independent review identified a pre-existing post-COMMIT `total_changes()` diagnostic query in
the SQLite adapter. Its result is now captured inside the transaction before COMMIT; no fallible
database counter query can cause media rollback after metadata has committed. Existing written-row
diagnostics retain their value, and obsolete-media enumeration is also prepared before SQL writes.

Twelve observations in `vocabulary-media-transactions-v1.json` execute the complete original Mac
archive actor, real memory SQLite and original image storage on fixed shared PNG bytes. The driver
only sets inputs and an actual failing SQL trigger; it does not replace commit, pruning, media or
cache logic. The Windows theory runs those same operations through real archive/SQLite/WIC owners.
They cover draft-only preparation, import, missing/unreferenced media, removal/shared merge, edit
conflict, SQL failure, existing-but-missing media, stale explanation pruning, resource budget and
same-filename retention. Shared observations use isolated memory mode and do not claim file locking.

Nine additional native Windows cases use private temporary named notebooks and actual files:
preparation without opening storage, reopen, new-file rollback versus repair preservation on SQL
failure and on cancellation, best-effort cleanup after committed metadata, the cross-owner lock
covering staging through commit, and revision CAS after a real external SQL change. A forwarding
adapter only pauses/cancels after real IO; no bytes or persistence results are faked. The macOS
`VocabularyContentTests` cases for discarded drafts, reopen, edit conflicts, rollback, shared image
merge/removal and corrupt-media repair are the behavior reference. This slice does not port their
backup/Anki/visible-media portions. Ordinary tests still require no model or interactive desktop.

```sh
python3 scripts/verify-vocabulary-media-transactions-macos-parity.py --mac-repo /path/to/macOS-lingrove
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release
# Windows CI executes actual archive/WIC/file integration:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyMediaTransactionTests
```
