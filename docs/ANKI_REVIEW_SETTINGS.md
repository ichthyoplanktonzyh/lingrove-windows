# Original review preferences and credential boundary

Reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

This slice adapts the complete `Models/Review/AnkiReviewSettings.swift` settings behavior to
`App/Review/AnkiReviewSettings.cs`. It adds the six original preference keys to the existing
`AppSettings` owner and defines a separate `Core/Review/IAnkiCredentialStore` contract. It does
not yet construct the service in AppHost, implement native credential storage, expose settings
controls, or probe/send to Anki. Those require the later application/controller slice.

## Ownership and data flow

The future shared app-owned settings instance reads through a `Func<AppSettings>` because general
reset replaces that object. Its setters edit the current owner, request one persistence operation
and notify presentation. They do not apply autocomplete settings or wake a model. General reset
retains all six raw review preferences, just as the original explicit reset-key list excludes them.
An unavailable preference destination leaves the in-memory edit intact, matching best-effort
UserDefaults writes. Credential operations have their own explicit failure boundary.

`AppSettings.Review.cs` retains raw JSON values so one damaged preference cannot discard unrelated
settings or silently rewrite stored data. Defaults and Foundation getter coercions live in
`FoundationPreferenceValue`; this stateless helper owns no disk or UI state. Numeric raw values
represent finite Double preferences at this adapter boundary. String projection preserves the
observed NSNumber G16 formatting, including negative zero. The optional Bool cast used by default
recognition differs from the coercing Bool getter used by default gap. In particular, recognition
accepts Boolean/numeric zero or one and otherwise defaults true; gap accepts nonzero numbers and
the exact case-insensitive strings `true`, `yes`, `1`, without trimming.

Defaults remain local HTTP port8765, deck `Default`, model `Lingrove v1`, the original twelve-field
mapping, recognition enabled and gap disabled. A saved empty deck still counts as a saved deck.
The preset model always uses its preset effective mapping; a custom model uses the saved mapping.
`ReviewMappingCodec` reuses the established Codable enum-dictionary boundary: persisted Data is an
alternating key/value JSON array, represented as base64 in Windows preferences. A malformed mapping
falls back to the preset without repairing the original bytes. A setter writes the array format.

Constructing or reading preferences never reads a secret. An explicit connection request first
validates and normalizes the local HTTP endpoint, then reads the independent credential store.
Only an empty key deletes; whitespace is an exact key. The existing inference credential contract
trims keys and therefore cannot stand in for this boundary. Keys never belong in settings JSON,
notebook data or logs. `Connection()` creates an immutable snapshot and performs no HTTP.

## Evidence and limits

`anki-settings-v1.json` contains60 action traces from the complete unchanged original settings and
credential-store declarations. The verifier creates and removes an isolated UserDefaults suite for
each trace and selects the original `service: nil` in-memory credential path. It cannot access the
user's Keychain. Native Swift JSON inputs preserve text scalars. Both runners lock case identities
and count. The mapping check inspects the actual encoded bytes as well as the decoded fields.

Cases cover defaults, edit/restart, preset/custom mapping, exact keys and deletion, invalid endpoint
ordering, corrupted preference types, duplicate/unknown/odd/object mapping encodings, numeric
formatting and the two distinct Bool getters. Three additional App tests cover no startup key
read, endpoint admission before secret failure, absence of keys from settings JSON, raw preference
preservation through general reset, continued ownership after replacement, and best-effort writes.

Local validation passes all60 original traces, full Core3719 and App332 tests without failures or
skips. Full Release cross-build and final-head Windows/Mac48-verifier CI are required before merge.
The tests establish settings semantics; they do not establish disk durability, DPAPI, production
AppHost wiring, UI interaction or an Anki server transaction. No socket, model, package or uploaded
artifact participates.
