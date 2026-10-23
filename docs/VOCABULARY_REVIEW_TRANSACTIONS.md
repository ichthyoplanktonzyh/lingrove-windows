# Original review and template persistence

Reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

This slice adapts `VocabularyArchive.swift` saveReview/recordAnkiBinding, the three mutation
methods in `VocabularyArchive+ReviewCards.swift`, and their five `VocabularyStore.swift` wrappers.
It supplies durable review/template transactions for later preparation/settings UI and send
sessions. It does not add controls, generate audio, contact Anki or acknowledge an unverified
remote write.

## Ownership and rules

`Core/Vocabulary/VocabularyReviewDraftPolicy` owns deterministic document edits. It takes the
latest immutable document plus a detached review/template/receipt and returns a candidate without
I/O or timestamp changes. `Win32/Vocabulary/VocabularyArchive.Review.cs` borrows the existing
serialized archive worker and media/SQL commit boundary. `App/Vocabulary/VocabularyStore.Review.cs`
admits and drains the whole operation, publishing only acknowledged immutable snapshots. Windows
and sessions continue to borrow that single Store.

Saving a review compares only the named card's complete current review against the captured
expected value. Unrelated card/entry edits survive. A new card may append when expected is absent;
its plan ID must match the supplied independent card ID and validation requires its layout.
Legacy review and independent cards remain separate. Draft saves/removals require the exact current
sense ID. Removing a card never removes legacy review or another card, and the final removal keeps
an empty card array rather than turning it into absence.

Receipt recording instead resolves a merged-sense alias, requires an existing review, and preserves
its latest plan and retained plans. Matching binding ID updates in place; another binding appends
and clears an ambiguous active choice. Exactly one binding becomes active. Deleted/missing senses
or reviews are never recreated by an acknowledgement. Notebook validation retains the existing
origin-identity, field, endpoint, profile and size rules before SQL.

Templates replace by stable UUID in position or append, validate before media commit, and retain
the source64-template/1024-byte-name/layout budgets. Removal preserves absent versus empty template
collections and still commits a new revision when the ID is missing. Review/template changes do
not touch entry/sense timestamps. Their image references use the existing staged media, shared
lock, SQL acknowledgement and orphan cleanup; supplied prepared-image maps are copied before queue
admission so a closing view cannot alter them.

These Store methods intentionally differ from ordinary edit commands: failure propagates to the
preparation/settings caller and leaves shared Store error state unchanged. Success applies the
acknowledged revision and clears a previous shared error as normal. Shutdown closes new admission,
drains already admitted operations and only then closes the archive. Cancellation remains bounded
by the existing image/SQL commit point; a completed SQL commit cannot be revoked by view dismissal.

## Evidence and limits

`vocabulary-review-v1.json` contains56 traces through the complete original Mac archive, real
temporary SQLite/media owner and exact five Store wrappers. The existing read-only source harness
omits unrelated Store presentation methods and diagnostics; no exercised mutation/commit body is
replaced. Fixture seeding commits through the real archive. Every action compares the visible
Store graph, actual disk graph/revision delta, loaded/pending/error state and thrown result. The
Store template-list projection has no optionality; its absence is recorded from the actual disk
document. JSONDecoder supplies native strings throughout.

Only schema-known unordered enum dictionaries (`mapping`, `audioReceipts`) and the `audioKinds`
set are canonicalized for observation. All document/card/part/template arrays, content text,
nil/empty states and scalar strings keep their order/value. Both runners lock all56 case IDs.
Coverage includes CAS conflict/no overwrite, unrelated-card concurrency, binding replay/ambiguity,
merged aliases, missing owners, invalid rollback, template replacement/order/limits/cancellation,
and shared-error retention versus successful publication. The portable App suite executes actual
production Store/archive code over isolated memory SQLite; a further deterministic close/drain
case verifies that admitted work completes and later admission fails.

Three extra Windows cases use actual named SQLite, WIC and media files: card/template image
sharing survives reopen until the final owner is removed; an injected review SQL failure rolls
back a newly staged image; and cancellation after actual staging preserves the prior SQL state.
They use disposable folders and the existing synthetic PNG, without opening a product window.

Local focused validation passes57 App cases and56 original traces. Expected full suites are
Core3720, Inference553, App559 and WPF500, with50 Mac source verifiers. Final-head Windows execution
and CI remain required before merge. These tests establish transactions and injected failures,
not native picker/editor interaction, an Anki transaction, process-crash recovery or a user-host
checkpoint. No model, socket, installer or uploaded artifact participates.
