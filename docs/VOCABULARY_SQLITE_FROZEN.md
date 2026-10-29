# Frozen vocabulary SQLite schema and transactions

Reference: committed Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. This is an isolated
Windows database adapter over `Cotabby.Core.Vocabulary.Frozen` values. It creates no application
Store, does not open the user's notebook, and does not activate a migration or UI.

| Frozen Mac source | Windows boundary |
| --- | --- |
| `Models/Vocabulary/VocabularyDatabaseModels.swift` | `Core/Vocabulary/Frozen/VocabularyDatabaseModels.cs` and `VocabularyDatabasePayloadCodec.cs`: 29 table/row identities, exact leaf dispatch and bounded payloads |
| `Support/Vocabulary/VocabularyDatabaseProjection.swift`, `VocabularySemanticDatabaseProjection.swift`, `VocabularyClassificationRows.swift` | `VocabularyDatabaseProjection.cs`: immutable headers/links, reconstruction and full re-projection consistency |
| `Services/Vocabulary/VocabularySQLiteDatabase.swift`, `VocabularySQLiteMeaningSchema.swift`, `VocabularySQLiteClassificationSchema.swift` | `Win32/Vocabulary/FrozenVocabularySQLiteDatabase.cs`: schema 5, indexes, deferred relationships, serialized revision/CAS transactions and 4-to-5 classification migration |
| `VocabularySQLiteConnection.swift`, `VocabularySQLiteFileLock.swift` | existing Windows SQLite connection and private-file identity/lock primitives are reused without using the historical document/SQL codec |
| `LingroveTests/Services/Vocabulary/VocabularySQLiteTests.swift`, `VocabularyClassificationPersistenceTests.swift`, `VocabularySemanticTests.swift` | focused Core projection and isolated Windows SQLite tests, not native Swift execution |

The canonical application ID is `0x4C47564E`, SQL schema version is 5, independently of portable
version 8. Tables preserve independent lexical records, source snapshots, occurrences, candidate/
accepted interpretations, collection links, annotations, personal content/reviews, semantic nodes,
roles/patterns/evidence/cues and classification frameworks/categories/edges/memberships/overrides.
Nullable index values remain SQL NULL, not empty strings. Parent/order/extra columns are checked by
reconstructing and reprojecting every row; malformed rows, orphaned children, inconsistent indexes,
wrong payload types and hidden nested children fail without being repaired. Whole graph admission
also runs before a database write and before a read snapshot is returned. Typed payloads share the
source-shaped leaf serializer and `SourceDate` Double representation, never the old v3 document
or v7 resource codec. The SQLite value limit remains 64 MiB. Like the frozen Mac database, this slice has no aggregate
table/row read budget; a maliciously large file can require substantial memory before admission.
A bounded production read strategy remains a gate before connecting this adapter to user data.

Creation stages a complete private file and publishes it exclusively; open uses the existing retained
Windows file-identity and cross-process lock. The owner serializes operations and disallows read-to-
write lock upgrades. A write projects old and new graphs, checks revision within `BEGIN IMMEDIATE`,
releases changed entry lookup keys and preferred-label slots, upserts in source table order, deletes
in reverse order, and commits optional receipt plus incremented revision atomically. `LastWrittenRows`
is published only after commit. Failure, including a deferred foreign-key failure, rolls back all rows
and revision. Cancellation is checked before work, across changed/deleted rows and before commit;
a single SQLite call and lock acquisition are not interruptible. A successful return acknowledges
commit; cancellation never reports failure after commit.

Schema 4 is checked before an additive, single-transaction classification upgrade to schema 5;
incompatible or incomplete schema and unknown versions are rejected without rewriting the file.
Schema 2 is never relabeled or migrated. Unlike the frozen Mac's version/quick-check admission,
Windows also compares table/index/trigger/view definitions against the source-shaped schema, ignoring
whitespace/case outside SQL literals. This stricter platform safety boundary rejects even extra
indexes/views/triggers or semantically equivalent alternative DDL; cross-platform schema compatibility
requires native Mac fixture verification before activation. The retained timestamp values are never
converted through `DateTimeOffset` on SQL read/write.

Windows tests use isolated temporary private directories and in-memory connections. They cover a
complete graph touching every table, named-file reopen, Double timestamp identity, schema-4 migration
and rollback, schema-2 refusal, missing schema objects, key and preferred-label replacement, stale
revision, deferred constraint rollback, and cancellation after rows are written. Core tests cover
row payload roundtrips and re-projection failure cases. No Swift toolchain was available on this
host; the frozen Swift SQLite suite and cross-runtime schema/payload verification remain open. The
adapter is not connected to AppHost, a production Store, media ownership, backup/restore, or user data.
