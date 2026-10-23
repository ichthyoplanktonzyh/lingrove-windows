# Vocabulary save and sense rules

This slice adapts the stable Mac deterministic save, sense ownership, merge/move and explanation
attachment rules. It returns immutable drafts in Core; it does not publish a durable transaction,
connect a Save command, validate an entire archive, create files, or call a model. The product parity
row stays source-only until the archive/store and UI are connected and verified.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac source | Windows responsibility |
| --- | --- |
| `Support/Vocabulary/VocabularyPolicy.swift`, save/contains/accepts/storeExplanation | `Core/Vocabulary/VocabularyPolicy.cs` resolves the current entry, returns a replacement document, and attaches records only to exact current explanation inputs. `VocabularySaveResult` is a draft plus actual entry ID, not a persistence receipt. |
| `Support/Vocabulary/VocabularySensePolicy.swift` | `Core/Vocabulary/VocabularySensePolicy.cs` owns stable sense/usage destinations, explicit merges and moves. `VocabularyMoveResult` returns the replacement entry and actual target ID. |
| `Support/LanguageLearning/LearningExplanationPolicy.swift`, capture(entry:sense:example:) | `LearningExplanationPolicy.Capture.cs` projects a saved owner into the same semantic input as a writing capture. This projection is not commit authority. |
| `VocabularyPolicy.swift`, VocabularyError | `VocabularyException.cs` supplies fixed failure categories/messages without user text/path payloads; content validation retains its narrow exception subtype. No storage implementation follows from the storage error names. |

A future app-owned archive supplies the latest document, runs policies on a detached draft, validates
the complete resulting graph, compares the expected store revision and writes atomically before
publishing a snapshot. Swift mutates an `inout` draft; C# returns immutable replacements instead.
An exception exposes no replacement and leaves the caller's original object unchanged. The Mac
archive also discards a failed draft, so the oracle compares error categories rather than a partially
mutated, unpublished Swift value. No live editor or focus service participates in a saved transaction.

Saving uses the existing exact capture keys. Without an explicit sense destination, a new context
creates a new stable sense with an empty core summary even when generated wording matches an older
context. An explicit live/alias destination may instead add that new usage to its selected sense. Re-saving the same usage keeps
entry/sense/usage IDs and primary translation, adding distinct translation-language variants. A reused
usage UUID for a different headword/occurrence gets a new UUID while retaining its captured text,
ranges, time and provenance. Standalone translations share one saved identity and retain variants;
re-saving does not redefine a user-authored summary. Ambiguous usage-key destination candidates
never select by list order; damaged duplicate aliases still require whole-graph rejection.

Retired sense IDs resolve through aliases for destination lookup and save. Explicit merge/move
requires live IDs. Merge keeps the target summary, retains source wording as an alternate, appends
notes, deduplicates canonical-equivalent tags in Swift scalar order, and retains usage IDs, aliases,
content, review cards and review plan/binding history. It rejects duplicate exact usage keys. Empty
content/cards become explicit empty arrays, matching Mac mutation behavior. Move keeps the exact
usage ID and leaves its empty source sense with notes/review intact; deletion is a separate action.

Explanation attachment resolves current inputs at the time of the operation. A late result follows
a moved usage, while an edited/deleted usage cannot be recreated. Usage records may refresh captured
or AI meaning; user-authored meaning stays intact. General records can fill an empty automatic core
summary only once; a refresh never replaces a nonempty core summary. Records replace earlier entries
with the same input and response language, then retain the last 24 in array order. Attachment alone
changes no created/updated timestamps or translations.

.NET permits undefined enum values and malformed UTF-16 that Swift values cannot represent. New
capture admission rejects unsupported direct/nested language, kind, context and authorship enums;
attachment ignores an unsupported response language. Those checks are not full graph validation.
The canonical comparer uses NFC scalar order for valid text and one consistent ordinal category
after valid text for malformed strings. Whole-archive admission must reject malformed data before
storage; the pure sort helper remains deterministic even for a caller's invalid draft.

`fixtures/parity/vocabulary-sense-v1.json` contains 33 actual Mac observations. The tenth verifier
compiles the original complete save/sense/content policies plus their pinned value/input validation
dependencies. Drivers construct fixed values and observe production operations; no replacement
policy supplies expected results. New UUIDs are labeled `new`, with separate stable-ID relationship,
uniqueness and ownership checks. The driver never calls the included sentence-acquisition/search
methods, and these cases do not establish Windows linguistic tokenization/search parity.

Fourteen additional C# cases cover immutable failure behavior, original snapshots, alias resolution,
raw canonical spelling, provenance, bounded record ordering and .NET-only invalid values. Existing
capture/model/content fixtures remain unchanged. The ninth and tenth Swift executables share a
source-reading helper which returns pinned declarations only, with no behavior or expected outputs.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
python3 scripts/verify-vocabulary-models-macos-parity.py --mac-repo /path/to/macOS-lingrove
python3 scripts/verify-vocabulary-sense-macos-parity.py --mac-repo /path/to/macOS-lingrove
```

[Whole-graph validation](VOCABULARY_ARCHIVE_VALIDATION.md) is now a pure library. [Concurrent edit/pruning drafts](VOCABULARY_ARCHIVE_DRAFTS.md) are also ported. Codec/version handling, atomic durable
write/revision comparison, media lifetime, notebook editing and Save UI remain migration work.
