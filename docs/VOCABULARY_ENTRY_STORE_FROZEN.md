# Frozen import Store candidate

The isolated candidate follows `Lingrove/Services/Vocabulary/VocabularyArchive+EntryImport.swift`,
`VocabularyStore.swift`, and `VocabularyWordListImportSession.swift` at frozen Mac
`35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. Core exposes immutable revisioned snapshot,
preview, result and subscription contracts. Win32 owns the database, serializes operations,
loads and re-plans the input batch inside the existing database write access, and commits only
when the actual plan changed. No-op preserves the revision. Successful results carry the actual
entry/sense IDs, and notification is enqueued only after database acknowledgement. Preview IDs
are provisional; UI navigation must use the result of the committed operation. Read and preview
are cancellable; after commit a late cancellation cannot turn the acknowledgement into failure.

One bounded notification queue coalesces intermediate snapshots for slow observers; delivered
snapshots remain ordered by revision. Listeners execute serially on a worker outside the SQLite
operation and must marshal to the UI thread. An in-flight callback may finish after unsubscription.
Disposal rejects new work, waits for accepted database work, and drains queued callbacks. A
callback must not synchronously wait for disposal of its own Store. File reading is the separate
`IVocabularyWordListReader` boundary and is not duplicated here.

This class is internal and unactivated. AppHost must eventually select one sole production Store;
the existing and frozen owners must never be activated as parallel writers. Legacy database
migration, media ownership, native cross-runtime payload compatibility, and activation of large
notebooks outside the documented read budget are not implemented here. The in-memory tests cover
re-planning stale preview inputs, no-op revision and selection identity, concurrent serialized
writes, failure/cancellation, ordered acknowledged publication and disposal drain; they do not
establish native desktop acceptance or a Swift compatibility result.
