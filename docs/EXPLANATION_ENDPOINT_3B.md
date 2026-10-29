# Independent explanation endpoint

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Models/LanguageLearning/LearningModelSettings.swift`,
`Services/LanguageLearning/LearningModelController.swift`, `LearningExplanationService.swift`,
`LearningEndpointSession.swift`, and
`Services/Runtime/OpenAICompatible/OpenAICompatibleAPIClient.swift`.
The existing `ModelDestinationIdentity` retains its historical verifier pin; its destination
identity body was also inspected at 3b before reuse. Historical fixtures are unchanged.

AppHost owns one explanation HTTP/credential feature and injects its request-owned generation
factories into the shared explanation controller. The independent provider settings and both
learning roles' power controls are connected. Writing sentence/dictionary cards, expression
assistance and canonical notebook rows now disclose the service's immutable destination before
generation; reading freezes the explanation destination and credential revision with its request.
Construction remains inert; only explicit task requests or metadata actions use the selected endpoint.

The endpoint profile is independent of translation/completion/speech and defaults to Local.
It keeps per-destination context/output/sampling maps, exact model destination keys, and a
runtime-only credential revision. Dormant selections do not retire the active backend.
Its credential file is separately named under the existing Cotabby local data root. Construction,
profile projection and local selection read no secret; explicit Save/Clear and admitted endpoint
operations may read this role's key. Profile values never contain the key.

Request-owned explanation and instruction facades reuse the existing instruction-task envelope,
prompts and complete-result parsers. Their role-specific raw caps are 24,000 bytes for explanation
and lookup, and 12,000 for expression assistance. The shared wire implementation retains the
old 12,000 translation cap. A non-`stop` finish never becomes a complete answer. Context/output
reservations, literal input, UTF-8 validity, redirects, cookies, deadlines and response sizes are
bounded before acceptance. Existing endpoint factories now disable redirects and cookies; model
catalogs are bounded to 1 MiB, 512 identifiers and 1,024 UTF-16 units per identifier, rejecting
oversize catalogs rather than truncating them. Metadata and generation use separate stateless
facades over this role's own HTTP pool.

One instruction semaphore now orders explanation, expression assistance and reading lookup
through the original latest-request lease: cancellation retires immediately; newer generation
waits until the old facade has drained. The earlier Windows fake test allowed overlap between
its fake explanation and native facade. It is upgraded to the inspected 3b `generate` contract
that awaits the previous task, while preserving held cancellation and shutdown assertions.
This changes no old JSON expectation or reference revision.

Reading endpoint instructions require a live captured permit. Expression requests now carry
an ignored/nonpersistent permit composed from the query revision/token and native receipt.
The native check is nonblocking: panel phase, keyboard ownership, host/panel PID, retained
token/version and direct retirement state. No dispatcher/UIA wait occurs at the actual HTTP
send or native generation boundary. Its noncooperative fake tests cover revocation before
notification, foreground/PID changes and return-to-editor retirement.

Settings operations are tracked through their UI continuation and metadata request. They close
admission and drain before retiring the role HTTP pool. Generation remains owned/drained by
the existing explanation controller and the single canonical Store flow.

Component validation: App explanation owner/settings 45/45; Inference endpoint/translation/API
84/84; serialized WPF expression/native permit/HTTP feature lifetime 48/48. Tests use synthetic
text, fake HTTP/keys/runtime and owned receipts. No real endpoint, model, user editor, clipboard
or notebook is used. Live endpoint quality and native host acceptance remain separate work.

The presentation adapter validates destination URLs before displaying them, and never renders
invalid URL user-info, query or fragment contents. Dormant endpoint fields are absent from local
labels. Long destination labels live in bounded scrolling regions alongside existing controls;
Segoe UI and the existing Windows theme remain the rendering convention. The shared service
exposes only a credential-free immutable profile; views construct no runtime or transport.
Explicit credential Save/Clear tests cover normalization, unchanged values, validation before key
access, fixed failures and retirement. Normal Quit/restart now drains onboarding scans before
AppHost. Integration compiled with zero warnings/errors. A WPF batch passed 54 checks and exposed
an incorrect terminal-punctuation fixture in the new reading test; the corrected standalone-word
receipt then passed 1/1, proving that a nonserialized explanation key revision retires the lookup.
These synthetic component checks do not establish native editor or visual acceptance.

The final shared instruction owner also reserves explanation order synchronously before any
asynchronous saved lookup or worker scheduling. Returning from a held old lookup validates that
same reservation and cannot cancel a newer expression or reading request. Caller cancellation is
checked before reservation and again inside the owner's admission lock. Saved/cache hits release
the reservation without constructing a provider. Independent Core 33/33 and App 35/35 checks cover
held lookups, reverse worker scheduling, already-cancelled callers and cancellation while waiting
for the lock. Historical fixture pins and expected values remain unchanged.
