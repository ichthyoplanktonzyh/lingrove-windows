# Original review field and conflict rules

Reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

This slice adapts four complete method bodies and the preset constants from
`Cotabby/Support/Review/AnkiReviewPolicy.swift`: `fields`, `conflicts`, `removesExistingCard` and
`audioFields`. Windows uses `Core/Review/AnkiReviewPolicy.Fields.cs`; the existing merge method in
`AnkiReviewPolicy.cs` keeps its implementation. `AnkiFieldConflict.cs` adapts the transient value
from `Models/Review/ReviewModels.swift`. This is a pure library, with no review settings/window,
controller transaction, server read/write, speech generation or media upload. Audio freshness
(`currentAudio`) remains separate work; these methods alone do not establish synchronized status.

## Ownership and behavior

A preparation session supplies detached `ReviewCardContent`, a stable origin identity, explicit
logical-to-model field mapping, remote-edit overrides and optional audio markup. The policy returns
an owned read-only field dictionary. It accesses no Store or connection and cannot acknowledge an
Anki receipt. Logical field identities remain independent of the user's chosen Anki names.

The original preset name and all twelve mapping values remain unchanged. Identity/expression/meaning
need nonempty selected mappings; all selected names must be canonically unique, free of Swift
newline characters and at most200 UTF-8 bytes. A whitespace-only name or embedded NUL is retained
because this source rule permits it; server compatibility/admission is a later boundary. Empty
optional mappings exclude those fields entirely.

Preparation keeps the source order: create legacy values, merge supplied audio values, replace
expression/meaning with flexible front/back HTML only when both sides are present, then apply
selected-field overrides. The selected identity cannot be overridden, including with an equivalent
Unicode spelling; unmapped overrides never add model fields. Flexible HTML uses the existing typed
part renderer; a text slot cannot become audio by string replacement. The final fields together
must fit1,000,000 UTF-8 bytes. This is the original field budget, distinct from the transport's
JSON-escaped18 MiB body limit. Overrides remain prepared HTML as in source, not a new sanitizer.

Conflict comparison visits only local mapped keys, sorted with existing Foundation canonical scalar
ordering. It surfaces a remote-only edit as well as a divergent edit; a remote value equal to the
base or local value produces no conflict. Missing base/remote fields compare as empty strings,
which preserves older receipts when a model gains an empty field. `AnkiFieldConflict` owns the
actual retained field/local/remote strings and uses canonical equality/hash; it is sheet state,
not durable learning content. Inputs are copied before producing immutable result collections.

Existing-card removal requires an associated note ID and an existing nonempty recognition/gap
field whose plan flag is being disabled. The original missing-mapping empty-key fallback remains
observable. Audio-field projection includes only selected audio kinds with a receipt and keeps
original `[sound:filename]` markup. It performs no filename/path/file validation or I/O; archive,
audio export and send scope checks remain independent responsibilities.

## Evidence

`anki-fields-v1.json` has76 source-policy-extension observations. The verifier executes the exact
four original methods and constants in an extension around the original merge declaration. Complete
original card/layout/template/wire sources supply dependencies. It does not claim to run the entire
AnkiReviewPolicy enum, controller, speech policy or HTTP client. Inputs and outputs use native Swift
JSON values rather than an NSString bridge, preserving decomposed/BOM text.

Cases cover required/duplicate/canonical mappings, all newline categories, ASCII/non-ASCII name
limits, identity and unmapped overrides, source audio/flexible precedence, absent versus empty
sides, exact aggregate byte admission, three-way comparison and scalar ordering, removal flags and
audio selection/receipt absence. Large generated values are compared by actual UTF-8 count and
SHA-256 so fixtures do not retain megabytes of repeated text. Smaller values compare exact strings;
result arrays and object keys must match completely. Two extra .NET cases cover result ownership,
canonical equality/hash and rejection of undefined enums/null values/malformed mapped UTF-16.
Both runners lock the reviewed case-ID matrix as well as the count, preventing same-count case
substitutions from silently reducing that coverage.

Local validation: all76 source observations and Core3595 tests pass without failures/skips. Portable
App269 also passes without failures/skips, and full Release cross-build has zero warnings/errors.
Final-head Windows execution and all45 Mac source verifiers remain required before merge. The new
CI step uses no network, model, Anki collection, installer or uploaded build artifact.
