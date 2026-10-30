# Learning providers, local dictionaries and writing surfaces

This integrated candidate adapts committed Mac
`3b53402f2cbc196b0398d133177fd80006d165b2`. Historical fixture/verifier pins are unchanged.
It follows the maintainer's complete-flow delivery and final unified physical acceptance contract.

## Connected application flows

- Independent explanation Local/Endpoint settings, role-specific credential storage, explicit
  metadata discovery, per-destination budgets/sampling and request-owned providers are connected to
  the existing shared explanation controller. Writing, reading, expression and notebook surfaces
  disclose the active destination. Latest instruction admission is reserved before asynchronous
  preparation and cannot be stolen by an older saved lookup or cancelled caller.
- Translation and explanation retain independent manual/battery/AC selections. AppHost publishes
  real power evidence before construction/application, including with endpoint autocomplete.
  Settings and readiness display active choices; parameter edits retain the destination identity.
- One dictionary catalog/router serves writing, reading and the canonical notebook. Explicit MDX/MDD
  imports, source ordering/languages, local articles and links use that owner. Rich articles have
  restricted WebView2 requests and identity-safe private profile cleanup. Actual isolated SDK tests
  exercise resource loading, repeated retirement and recovery after a temporary child-file lock.
- Canonical image drops use an isolated native child receiver so foreign OLE data does not enter
  the WPF dispatcher. Existing bounded image preparation and acknowledged Store/media writes remain
  the only save path. Embedded ICC and bounded RLE bitmap support retain their separate evidence.
- Autocomplete exposes current streaming, visible-word control, local typing prediction and guarded
  continuation. Onboarding shows task readiness; normal Quit/restart drains its scans. Expression
  surfaces follow Windows themes and bound their scrolling to the anchor monitor's work area.

See [explanation transport](EXPLANATION_ENDPOINT_3B.md),
[provider settings](EXPLANATION_PROVIDER_SETTINGS_UI.md),
[learning power profiles](LEARNING_POWER_PROFILES_3B.md),
[dictionary providers](DICTIONARY_PROVIDERS_3B.md),
[private WebView profiles](WINDOWS_PRIVATE_WEBVIEW_PROFILE.md),
[canonical media](VOCABULARY_CANONICAL_MEDIA_3B.md),
[autocomplete](AUTOCOMPLETE_STREAMING_3B.md),
[onboarding](ONBOARDING_READINESS_3B.md) and
[expression themes](EXPRESSION_ASSIST_THEME.md) for exact source paths, Windows mappings and limits.

## Review and validation

Every implementation has independent source review; the primary reviewed provider settings and
rendering, while other reviewers checked transport/admission, native directory identity, OLE retirement,
power policies, application ownership and expression layout. Review repairs include late saved
lookup ordering, cancellation before reservation, native transient-lock classification, SDK failure
states, receiver publication/retirement and short-monitor candidate visibility.

Source head `99ce6de42647f978989760127ca7d2efdd22aca1` compiled in Release with zero warnings/errors.
Its full local suites ran sequentially: Core **5,046/5,046**, portable App **1,277/1,277**, Inference
**739/739**, and the mutex-serialized WPF suite **1,024/1,024**, with no skips. WPF took 4m02s.
The current developer SDK is 10.0.403 under the repository's 10.0.402 latest-patch policy.
All four final-head remote jobs remain the PR merge gate; local results are not a remote CI claim.

The first remote Windows run exposed a test-boundary race in the new word-completion fallback
checks: after acceptance, the valid next prediction could already have added another engine
request. The single-request assertion now checks the fallback before acceptance, while the final
field assertion still verifies that only the missing suffix was inserted. This matches the
committed Mac word-fallback and post-acceptance continuation responsibilities; it changes no
production behavior or historical fixture expectation. Core then passed 5,046/5,046 again and
the two focused cases passed five further runs. The updated PR head requires a fresh four-job CI run.

A subsequent Windows run exposed another timing assertion in the streaming coalescer test. A
typing-cadence wait can legitimately re-read focus more than twice on different scheduler clocks.
The test now holds the actual refresh with an explicit barrier, asserts exactly one in-flight
refresh during the thousand-delta burst, and then verifies the latest text after releasing both
refresh and UI. This changes no production logic. At the isolated PR base, Core passed 5,046/5,046
and all 17 streaming cases passed five consecutive runs; the final revised head still requires CI.

With Core green remotely, the Windows adapter suite exposed a pre-existing 400 ms spin-wait race
in queued UIA registration revocation. That test now uses a dedicated caller and an explicit
queue-admission barrier; the real 500 ms production timeout is unchanged. A null-by-default
internal test observation point orders this regression without querying a user field. The same
Inference filter used by CI passed 732/732, followed by five passes of that exact case. Windows CI
now runs all four test suites sequentially after a successful build even if an earlier suite fails,
retaining the overall failure, original commands/filters and all four required jobs. This collects
all diagnostics in one run rather than hiding later suites behind the first failure.

Tests use synthetic text, fake HTTP/keys/runtime, owned files/databases and isolated native components.
No user notebook/editor/clipboard, real provider query, model download or audio-device playback is used.
No installer, release, downloadable package or CI build artifact is produced.

## Explicit remaining work

The new writing-card automatic/collapsed/replacement workflow and the complete Home/Welcome learning
showcase remain separate active modules. A dispatched native edit is not an acknowledged replacement.
Multiple-source statistical language confidence has no selected Windows provider. Apple system and
online/website dictionaries, loose adjacent dictionary resources, some bitmap/drop variants and
damaged-existing-media repair retain their documented unsupported states. In particular, uncertain
WebView initialization/process exit retains its profile and one of two admission slots; a restart
may be required. A configured endpoint is not proof of connectivity or model quality.

Windows renders follow WPF/Segoe UI and shared palette conventions; pixel equality with Mac is not
required. Component images were reviewed, including dark and short expression views and narrow
provider settings. No corresponding Mac screenshot was available. Physical Word, Chrome and Codex,
IME, mixed-monitor DPI, accessibility, real dictionaries, Anki, voice/audio hardware and model quality
remain for the maintainer's final unified acceptance pass.
