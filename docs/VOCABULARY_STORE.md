# Shared vocabulary store and explanation persistence

AppHost now owns one `VocabularyStore` and its one `VocabularyArchive` for the process lifetime.
This adapts the UI-facing store in the pinned Mac product; it does not introduce a new learning
workflow. Cards and the future notebook borrow this owner. The Save button/notebook UI remains
unconnected in this round; saved explanation lookup is the first consumer.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac source | Windows mapping |
| --- | --- |
| `Services/Vocabulary/VocabularyStore.swift` load, apply and change | `App/Vocabulary/VocabularyStore.cs`: single in-flight load, revision gate, exact pending-key counts |
| contains, saved/standalone keys, explanation lookup/destination | Same store over immutable Core snapshots; explicit sense aliases resolve through the existing source policy |
| user meaning and explicit explanation adoption | Stable usage identity follows a move; changed/deleted contexts are refused; explicit adoption marks authored meaning |
| save/remove/edit/sense facade | Store sequences user intent and error publication; archive alone owns persistence |
| `App/Core/CotabbyAppEnvironment.swift` cached enrichment and explainer callbacks | `App/Hosting/AppHost.cs`: cached/saved enrichment, load-persisted, lookup-saved and generated-record attachment |

`VocabularyStore` keeps immutable acknowledged snapshots and pending-key reference counts under a
small synchronization lock. Worker-side explanation lookup can safely read the same graph. Disk
operations remain in the archive queue. A delayed response with an older revision is ignored;
an equal revision may refresh presentation exactly as on Mac. Each concurrent change increments
its original capture key, so finishing one save does not clear another save's pending state.

Notifications post to the supplied UI synchronization context and views read the latest snapshot.
Retired dispatchers and failed presentation observers cannot undo an acknowledged transaction.
Construction is inert. One first-use load is shared by callers; a failed load remains retryable.
Only fixed vocabulary errors reach presentation; foreign IO errors do not expose paths or content.
Successful publication clears the previous error. Store disposal closes admission, drains accepted
operations, then disposes the archive; view dismissal is not transaction cancellation.

AppHost lazily selects `%LOCALAPPDATA%/Cotabby/vocabulary/vocabulary.sqlite`, preserving the current
Windows internal storage identity. The private Windows directory/file rules apply before any named
SQLite access. There is no migration from an assumed Mac file location and no cross-platform file
compatibility claim. Opening the app does not open the notebook or load a model.

The explanation service first asks this shared notebook for a matching input and response language.
Saving a capture may attach only a complete cached/saved explanation; it never requests generation.
A generated complete record is offered to the existing archive policy, which only updates a matching
saved target and preserves user-authored meaning. Explicit adoption is a separate store operation
that marks a chosen meaning as user-authored. A late result follows a moved usage by UUID and cannot
recreate a deleted usage. Shutdown drains the explanation service before closing the store, so its
already emitted records finish their database work.

The source verifier executes 16 observations from the exact original Store method bodies and
properties, backed by the complete actual Mac archive and memory SQLite. CLI/media/review/export
store methods are omitted from this explicit slice; no exercised method body or storage algorithm
is replaced. Three diagnostic logging lines are removed under count guards. Test-only hooks apply
acknowledged snapshots or gate two real saves to observe publication and pending lifetimes.
The C# consumer uses production Store/Archive/SQLite and native Mac row/codec graph inputs.

Eight additional portable tests cover single-flight load, retry/error hygiene, UI-context dispatch,
retired-dispatcher acknowledgement, store drain, enrichment failure, explanation dependency shutdown, and restoring a real persisted
record through the explanation service with zero engine creation. They do not run a model or a
Windows desktop. CI also retains the Windows archive/file and WPF card checks.

Unfinished: card Save/remove/show actions, notebook browsing/editing UI, review/template facade,
images/media, backup/restore/export/CLI, and full localization. Native UIA/IME/insertion/ghost/GPU
verification remains separate. This is an application-owned persistence integration, not complete
Mac product parity.
