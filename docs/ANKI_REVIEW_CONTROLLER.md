# Original review controller and navigation

Reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

This slice adapts both complete `Services/Review/AnkiReviewController.swift` and
`AnkiReviewController+Navigation.swift` to the App review controller and its Navigation partial.
It also adapts the transient catalog/remote-note values and `ReviewOperationScope`. These are
application/library boundaries: AppHost construction, settings/preparation controls, send sessions,
batch processing and media integration remain separate work. No method is called at app startup.

## Ownership and behavior

The eventual shared UI-context controller borrows the existing review preferences, notebook Store
and independently owned `IAnkiConnecting`. Windows will observe its notifications and snapshots;
they must not create their own HTTP clients. Construction performs no credential read, notebook
load or server request. The Store remains the notebook owner; this controller only reads current
entries and coordinates requests. Sessions will own durable review writes and acknowledgements.

An explicit operation scope freezes its connection/key, profile, model/mapping, optional speech
configuration ID and default choices before proceeding. The scope owns its mapping and is never
persisted or printed as a diagnostic value. Matching deliberately ignores later changes to default
deck/recognition/gap: the captured defaults stay frozen for the operation. Connection, model,
effective mapping and speech changes invalidate it. Verification checks those guards before and
after the awaited active-profile read. Preset model matching uses its effective preset mapping.

`AnkiConnection` now supplies source-compatible value equality for normalized local absolute URLs
and canonical key strings, while retaining a value-free ToString. URL spelling, explicit default
ports and trailing slashes remain distinct in the original URL equality probes; do not replace this
with .NET Uri's broader equality. The existing `CanonicalStringComparer` is public so Core values
and App-owned guards share one canonical Unicode comparison without altering retained strings.

Connection probes publish catalog/field results only when their captured connection still matches;
probes using the current setting also guard that model. An explicitly selected model intentionally
skips the active-model comparison. A repeated probe shares the in-flight guard. Field refresh retains the source's narrower
publication guard and error behavior. Field/model preparation preserves both built-in templates,
their exact payloads, explicit create permission and optional expected-profile check. Note reads
require one row, a positive integral matching note ID, a string model and string field values;
Boolean IDs cannot masquerade as numbers. Missing notes remain distinct from invalid responses.

The short Begin/Finish lease preserves sense versus independent-card identity, reentry rejection,
wrong-owner completion, cancellation, failure, missing-note and unresolved-edit state. Status and
synchronization project original review content/field/audio-text rules; they do not prove active
profile, provider, file existence or a remote acknowledgement. The original status precedence and
the separate synchronization predicate remain distinct.

Explicit navigation verifies profile and origin identity before browsing an associated note.
Deck navigation obtains the note's current card deck, checks profile again, requires the selected
deck in the catalog and an exact Boolean overview acknowledgement, then invokes a separate
existing-app activation callback. It never launches an Anki instance. Template upgrades accept
only unchanged original/current templates, append missing fields while preserving identities and
scheduling, check profile before field-add/template/style writes, conditionally replace only legacy
CSS and verify template readback. The original missing-model creation path does not pass an expected
profile into Fields; it therefore has no additional profile recheck before that nested createModel
request. A customized template is left untouched.

The optional speech boundary is currently an injected configuration-ID accessor: these methods
only read that property. A later speech owner will supply it. The native existing-app activation
adapter and generic app logging also belong to the later composition/UI slice.

## Evidence and limits

`anki-controller-v1.json` contains167 traces executed against both complete pinned controller
files. The verifier omits only the Logging import and three diagnostic statements, with exact
occurrence assertions. All exercised decision/mutation/request bodies remain original. It uses
the complete original settings type with isolated UserDefaults and the nil-service memory key
store, original card/layout/template/Anki policies, wire values and scope declaration. A read-only
`VocabularyStore.entries` adapter supplies immutable fixtures because these methods only read it;
this does not execute the Mac archive. A property-only speech stub supplies configuration identity.
The complete default client/session dependencies compile, but every controller receives the
scripted transport and activation callback; no real URLSession/client or Anki app is constructed.

Every trace compares controller state after each action, outcomes/errors, ordered requests and
parameters, captured endpoints/synthetic keys, activation and settings-reveal counts. Large
template/CSS strings compare actual UTF-8 count and SHA-256; smaller values compare exact text.
The source capture uses native JSONDecoder strings and each runner locks all167 case identities.
Coverage includes response shape/identity admission, ordered failure prefixes, stale settings and
profile changes, frozen defaults, preset/custom effective mapping, independent card/legacy states,
lease completion/cancellation, current Store lookup, navigation and protected template upgrades.

The App suite links actual production controller/settings/Store code. Its167 matching traces add
three deterministic suspended-operation cases for probe/menu reentry, model changes across await,
activation ordering and cancellation before dispatch. The loaded-entry cases use actual isolated
SQLite and Store publication. One Core fact checks detached read/scope collections, canonical
equality/hash and absence of secret text from ToString.

Local focused validation passes all170 App cases and all167 original traces. Full expected suites
are Core3720, App502, Windows Inference553 and WPF497; final-head Windows/Mac49-verifier CI remains
required. These are synthetic in-process effects, not native Anki interoperability, UI interaction,
production credential access, OS activation or durable send/recovery evidence. No model, socket,
installer or uploaded artifact participates.
