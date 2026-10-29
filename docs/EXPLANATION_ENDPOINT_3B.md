# Independent explanation endpoint

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Models/LanguageLearning/LearningModelSettings.swift`,
`Services/LanguageLearning/LearningModelController.swift`, `LearningExplanationService.swift`,
`LearningEndpointSession.swift`, and
`Services/Runtime/OpenAICompatible/OpenAICompatibleAPIClient.swift`.
The existing `ModelDestinationIdentity` retains its historical verifier pin; its destination
identity body was also inspected at 3b before reuse. Historical fixtures are unchanged.

This checkpoint provides scoped components. AppHost owns one inert explanation HTTP/credential
feature, but does not yet inject its generation factories into the live controller. Provider
settings, every request's visible destination and final production activation are pending.
It must not be described as a working endpoint option until that composition is complete.

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
or notebook is used. Production activation and native host acceptance remain separate work.
