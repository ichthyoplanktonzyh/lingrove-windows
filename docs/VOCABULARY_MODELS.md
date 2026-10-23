# Notebook and review value graph

This stage ports the stable Mac notebook/review value graph and its read-only projections, plus
the pure review-binding merge rule. It retains relationships needed before implementing Save and
archive transactions; it does not connect a database, notebook UI, Save command, Anki transport,
image/audio preparation, backup or schema migration. Existing entry/sense/store parity rows remain
incomplete. In-memory schema marker 3 does not establish Mac Codable/SQLite compatibility.

The reference remains `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

| Mac source | Windows value boundary |
| --- | --- |
| `Models/Vocabulary/VocabularyModels.swift` | `Core/Vocabulary/VocabularyNotebookModels.cs` owns entry, sense, document and revision snapshot, complementing the existing capture/usage values. A future app-owned store publishes these immutable graphs. |
| `Models/Review/ReviewModels.swift`, durable review/plan/binding/field values | `Core/Review/ReviewModels.cs` retains independent plan IDs, origin sense ID, remote endpoint/profile/note identity, logical mapping, acknowledged fields and sync time, with no API key or connection behavior. |
| `Models/Review/ReviewLayout.swift`, durable layouts/parts/templates and Sense extensions | `Core/Review/ReviewLayoutModels.cs` and Sense methods preserve ordered slots, block references, custom content and all accepted layouts, including preserved plans. Resolved preview material/template application is separate work. |
| `Models/Review/ReviewAudioModels.swift`, kind/receipt only | `Core/Review/ReviewAudioModels.cs` retains fingerprints, text hash, remote scope and filename. Prepared bytes/generation/upload are absent. |
| `Support/Review/AnkiReviewPolicy.swift`, `merged` only | `Core/Review/AnkiReviewPolicy.cs` supplies a pure replacement review value to a future sense transaction; it performs no RPC or endpoint admission. |

`VocabularyEntry` owns stable senses, not one flattened translation. A sense retains authorship,
meaning language, notes, tags, standalone/alternate meanings, usages, explanations, merged-ID aliases,
authored content, legacy review and flexible cards. Entry ID/language/kind and sense ID are immutable;
edits return values retaining them. Created timestamps are retained, and updated timestamps are
explicit values. `VocabularySnapshot` carries an unsigned 64-bit store revision separately from
record identity and document version. These values impose no persistence/date encoding.

Entry `Examples` concatenates senses/usages in order, retaining duplicates. Entry `Explanations`
traverses each sense's direct records then its usage records, keeping the first occurrence of each
record UUID and its original value/order. Empty output is absent. Those projections never mutate
nested owners or create a second owner of a usage.

Legacy `Review` and optional `ReviewCards` coexist. No automatic legacy migration occurs. Flexible
cards follow plan UUID, not their displayed name or list position. Sense `ReviewValue` and
`WithReview` map the Mac getter/setter to immutable snapshot replacement: null card ID addresses
legacy review, a known ID replaces that slot, and a missing ID appends. This is value manipulation,
not validation/authorization of a notebook operation. `ReviewLayouts` visits legacy then flexible
cards, each current plan then all preserved plans; retained plans still participate in media graphs.

Optional presence participates in equality: absent versus empty content/cards/templates, absent
versus empty layout, absent versus empty answer/name/deck, absent versus false include-content, and
absent versus empty audio kinds/receipts all differ. An absent answer follows the source; an empty
answer is an explicit review override. Meaning wording and review answer remain independent.

`CollectionValues` is internal snapshot plumbing, not a new product/storage abstraction. It copies
list/map/set inputs on construction and `with` assignments, preserving ordered arrays and unordered
map/set value equality/hashing. String equality follows Swift canonical equivalence without changing
retained spelling/UTF-8. Maps use canonical key comparison and reject ambiguous canonically equal
.NET keys with a fixed error instead of dropping a user's field. A notebook decoder/validator still
needs explicit whole-graph bounds, foreign-ID checks, version handling and conflict protection.

Review merge keeps the target plan, existing retained plans, then the source plan and its retained
plans. Bindings deduplicate only within the same endpoint/profile when either a non-null note ID
matches or origin sense ID matches. The first binding survives; its original sense ID is retained.
Multiple bindings clear active selection and require a choice. A single binding becomes active;
empty bindings have no active value. The executable corpus also checks canonical-equivalent
endpoint/profile strings and different source binding UUIDs naming the same remote note. Null source/target reviews retain the other immutable value.
This is not an acknowledged Anki write or proof of the current server profile.

`fixtures/parity/vocabulary-models-v1.json` has 31 source-policy probes. The ninth Swift verifier
compiles complete pinned notebook/content/review/layout declarations (including native Codable
conformance), original audio values excluding the speech-byte draft, explanation values and the
exact original merge function. Driver code only constructs fixed values and observes real defaults,
optional equality, collection equality, entry/layout projections, review slots and merges. It does
not encode/decode a notebook, and these observations are not a cross-platform storage roundtrip.

Twenty-seven standalone cases additionally check detached drafts/readonly snapshots, canonical
hash consistency and raw preservation, explicit UUID creation, authored metadata, stable Int64 note
IDs, optional differences, source-history retention and binding identity boundaries. Further native
archive/media/restart, complete model/codec roundtrip, template portability/binding, localized labels,
review resolution, durable Save/merge/move operations and Anki conflict/send verification remain unfinished. The next [save/sense rule slice](VOCABULARY_SENSE_RULES.md) now supplies pure draft mutations; it does not yet publish a durable transaction.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
python3 scripts/verify-vocabulary-models-macos-parity.py --mac-repo /path/to/macOS-lingrove
```

No user library is read/written, no service is contacted, and no model/package/upload is involved.
