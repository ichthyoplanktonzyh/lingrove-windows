# Canonical review draft transactions

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/Vocabulary/VocabularyArchive.swift` (`saveReview`, `recordAnkiBinding`),
`VocabularyArchive+ReviewCards.swift` (`removeReview`, `saveTemplate`, `removeTemplate`),
`VocabularyStore.swift` wrappers, and `Lingrove/Services/Review/AnkiReviewSession.swift`.
Related source tests include `LingroveTests/Services/Review/AnkiReviewTests.swift`.

`Core/Vocabulary/Frozen/VocabularyReviewDraftPolicy` accepts the latest detached canonical graph
and returns an immutable candidate and `NeedsCommit`. It edits only collection-owned review/card
fields or the template list; raw catalog, sources, occurrences and interpretations remain intact.
Save compares the complete current named review with the captured baseline and requires the exact
collected sense; a named card's plan ID must match its card ID. Receipt recording resolves the
merged-sense alias, updates the matching binding or appends one, and preserves the latest plan and
other bindings. An ambiguous multi-binding addition clears the active choice. Frozen bindings
retain `SourceDate`, including its native Double value; the old DateTimeOffset type is not used.
Removal compares the exact sense/card baseline and retains an empty card array. Templates replace
in place or append and use the existing source-derived validation. Removing a missing template
still returns `NeedsCommit = true`, including when the list was absent, matching the source call to
commit. All five source operations require a commit even if their candidate equals the input.

The Core regression tests exercise those source-derived cases, including independent card edits,
stale CAS, uncollected senses, merged receipts, binding ambiguity, original evidence retention,
template order/validation and nil-versus-empty states. They do not execute Swift, SQLite, media,
the shared Store, UI or an Anki request. A later archive owner must hold one serialized write span,
validate the candidate, atomically stage/rollback referenced images, commit and publish only after
acknowledgement. This pure boundary does not introduce a second Store or activate any review UI.

Validation on Windows:

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyReviewDraftTests'
```
