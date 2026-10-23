# Archive editing and commit drafts

`Core/Vocabulary/VocabularyArchiveDraftPolicy.cs` adapts the deterministic editing/removal and
explanation-pruning portions of the stable Mac `Services/Vocabulary/VocabularyArchive.swift` at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. The future app-owned serialized archive supplies the
latest document plus the editor's original entry, then runs representability checks, pruning, full
semantic validation and a revision-compared durable commit before publishing. Returning a detached
draft here acknowledges no write and installs no store, SQLite connection, codec or Save button.

Update merges expression/dictionary-form fields and stable-ID senses against three values: baseline,
current and edited. An unchanged edited field keeps the current value; a changed field is accepted
when the current value equals the baseline or the same edit. Different concurrent edits conflict.
Text/array equality retains Swift canonical equivalence and optional empty/absent distinctions
without normalizing retained spelling. Timestamps/creation and immutable entry identity come from
current data. The caller supplies `now` in C# instead of reading the wall clock in pure rules.

A sense editor changes only the Mac-owned editable fields: core meaning/language/authorship, note,
tags, authored content and examples. Current review, aliases, alternate meanings and standalone
state remain current; a stale sheet cannot recreate a removed standalone favorite or overwrite a
new review receipt. Concurrently appended senses survive; new sense IDs cannot reuse a live or
retired alias. Deleting an unchanged sense is allowed, while deleting a concurrently edited sense
conflicts. Meaning edits or explicit adoption transfer ownership to the user; changed standalone
meanings append a variant when the sense is currently saved without context.

Usage identity distinguishes edits/deletions from concurrently added captures. New usages deduplicate
by the exact existing usage key and keep the live capture ID. An edited existing usage compares the
whole value, so a translation arriving concurrently with a contextual-meaning edit produces a
conflict. A changed source with automatically cleared meaning resets authorship to captured; an
explicit meaning/language edit belongs to the user. Deleted or moved usages are not recreated by a
stale editor.

Exact favorite removal follows the current destination/alias and keeps annotated identities. It
removes an empty unannotated captured sense and, if necessary, its empty entry. Source conditions
for notes/tags/user authorship/aliases/review/content remain intact; generated core wording alone
is not a retention flag. Explicit sense creation/removal and entry removal retain source ID and
timestamp behavior. No list operation is a database transaction in this library.

Commit pruning compares each saved record to its current semantic input. General records require
current standalone ownership; usage records follow the moved usage. Changed text/headword/form
cannot relabel an old answer as current. Pruning changes neither meaning nor timestamps. Before
pruning, the internal representability pass rejects null required nodes, malformed UTF-16 and
undefined enum integers that Swift cannot represent, including nested explanation input/result
values. It checks no product text budgets. Well-formed stale records reach pruning; malformed
records are rejected rather than silently removed. Full semantic validation still follows pruning.

The twelfth Swift verifier compiles the original private `mergeSenses`, generic `merge` and
`mergeExamples` bodies in a value wrapper. Its public probe only calls those original private
methods. A second probe wraps the exact original `commitUnlocked` pruning loop with local input and
return statements. There are no storage fakes, replacement merge policies or model/runtime calls.
`fixtures/parity/vocabulary-edit-v1.json` contains 24 actual Mac observations, mirrored by C# input
construction calling production rules. Source Date() versus C# supplied time is observed only as
whether the sense updated timestamp changed; timestamp equality/retention has additional direct
cases. These probes cover sense/usage helpers and pruning, not full Mac Update orchestration,
removal, actor serialization or SQLite commit execution.

Thirteen standalone regression cases adapt the pinned Mac archive tests for stale editors,
concurrent captures, conflict preservation, exact removals, authorship and moved explanations, plus
.NET malformed-value admission. Failures leave the original graph untouched. Existing capture,
model, save/sense and whole-graph validation corpora keep their expected values.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
python3 scripts/verify-vocabulary-edit-macos-parity.py --mac-repo /path/to/macOS-lingrove
```

Relational projection, codec/date/schema handling, SQLite file safety/revision comparison, media
staging/rollback and acknowledged store/UI updates still remain. None is implied by the pure draft
operations or the passing helper probes.
