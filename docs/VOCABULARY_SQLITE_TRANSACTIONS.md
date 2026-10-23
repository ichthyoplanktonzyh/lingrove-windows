# Vocabulary SQLite transactions

This slice executes real SQLite transactions against isolated in-memory databases. It does not
connect a user file, persistent archive, AppHost store, Save button, notebook or CLI listener. The subsequent [Windows file adapter](VOCABULARY_WINDOWS_FILES.md) adds named-file identity,
exclusive publication and advisory locking as a separate boundary. In-memory success cannot establish restart or disk durability.

The behavior reference is `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

| Pinned Mac source/test | Windows boundary |
| --- | --- |
| `Services/Vocabulary/VocabularySQLiteConnection.swift` | `Win32/Vocabulary/VocabularySQLiteConnection.cs` |
| `Services/Vocabulary/VocabularySQLiteDatabase.swift` | `Win32/Vocabulary/VocabularySQLiteDatabase.cs` |
| `Models/Vocabulary/VocabularyCLIReceipt.swift` | Opaque owned receipt value beside the database, no CLI listener |
| `Services/Vocabulary/VocabularySQLiteFileLock.swift` | Nested access policy adapted; native named-file lock remains pending |
| `CotabbyTests/Services/Vocabulary/VocabularySQLiteTests.swift` | `App.Tests/VocabularySQLiteParityTests.cs`, `VocabularySQLiteTests.cs` |

The future archive exclusively owns one synchronous database/connection. A monitor serializes
complete operations, including nested access, and rejects a shared-to-write upgrade. Statements,
native borrowed spans and transactions never cross an await. SQLitePCL safe handles are confined to
the connection; rows copy borrowed bytes before stepping/finalizing. SQL values retain their actual
type, strict UTF-8, embedded NULs, empty TEXT and empty BLOB. Native failures use fixed vocabulary
errors, busy/locked use `StorageBusy`, and no SQL error text or notebook content is exposed.

The schema retains application ID `0x4C47564E`, SQL version 2, nine relational tables, deferred cascade
foreign keys, owner/order indexes, unique lookup keys, singleton revision/template state and opaque
CLI receipts. SQL version is independent of document version 3. Admission checks application ID,
accepted versions 1/2, `quick_check` and foreign keys before reconfiguration; v1 adds only the receipt
table transactionally. It never silently recreates a rejected store. Memory connections retain
`journal_mode=memory`; WAL and `synchronous=FULL` become durability settings only for future files.

Writes compare typed rows before encoding, acquire `BEGIN IMMEDIATE`, compare the actual revision
inside the transaction and reject stale/Int64-exhausted revisions. Changed/deleted lookup keys are
released before their replacement owners; changed rows are upserted parent first; moved usages and
reviews are reparented before old owners are deleted child first. Receipt, graph and incremented
revision commit together. Failure, including a deferred FK error at COMMIT, rolls back everything.
A note edit writes one sense plus state; a no-op writes only state. Last-written count updates only
after success. The 64 MiB native SQLite length/binding budget stays here, outside the pure codec.

Read uses one transaction for revision, template presence and all rows. SQL types, leaf codec and
lossless relational reconstruction are checked; whole-graph semantic admission remains the future
archive's responsibility, just as on Mac. Invalid ranges can reconstruct and must still be rejected
by `VocabularyArchiveValidation` before publishing. Corrupt payloads are retained rather than fixed.

The fifteenth Swift verifier compiles the complete original database, connection, file-lock and
receipt sources with the pinned model/projection dependencies and the SDK SQLite3 module. Only the
Logging import and two logging calls are excluded, with exact-count guards; no exercised SQL,
transaction, decoder or lock rule is replaced. Its 20 native in-memory observations cover changed
rows, moves, key replacement, rollback, stale/overflow revisions, receipts, malformed SQL rows and
nested access. C# starts from the same native codec bytes and relational graph inputs from the
preceding corpora; SQLite expected results come only from the new Swift driver. Thirteen standalone
cases add v1 admission, foreign key checks, key swaps, deferred COMMIT failure, native value types,
malformed text, byte budgets, isolated ownership and receipt byte copying.

`SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 supplies the native SQLite implementation. Its raw API preserves
the source's explicit type checks and length limit; it is not an ORM or an alternative data model.
The package metadata and [upstream API](https://github.com/ericsink/SQLitePCL.raw/tree/v3.0.5) identify
the pinned implementation; [SQLite's transaction contract](https://sqlite.org/lang_transaction.html)
defines COMMIT/rollback behavior. Ordinary dependency restore is the only download involved.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release
python3 scripts/verify-vocabulary-sqlite-macos-parity.py --mac-repo /path/to/macOS-lingrove
```

The subsequent [Windows file adapter](VOCABULARY_WINDOWS_FILES.md) supplies creation/reopen,
reparse/file identity and cross-process locking. Durable archive cache publication, media,
Save/notebook UI and complete Mac storage compatibility remain open.
