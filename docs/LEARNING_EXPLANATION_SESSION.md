# Explanation session library

`Core/LanguageLearning/LearningExplanationSession.cs` and `LearningExplanationState.cs` adapt
`Cotabby/Services/LanguageLearning/LearningExplanationSession.swift` at pinned Mac revision
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. They preserve the original card/session responsibility,
while the [app-owned service/runtime](LEARNING_EXPLANATION_RUNTIME.md) owns model work. The
[writing sentence card](LEARNING_EXPLANATION_CARD.md) now borrows this session and connects its
explicit controls to that service; unit/component and notebook owners remain later work.

A card supplies one immutable explanation input. `SetTarget` validates it and can restore a
matching saved record synchronously or asynchronously; navigation never generates. Equal targets
keep existing state. Saved records must match target and response language. An offline session
without a service can still show an explicitly supplied valid saved record.

`ExplainAsync` is explicit, suppresses duplicate active requests, and bypasses a complete existing
record only for refresh. Refresh retains the previous complete answer while generating. Stop or
failure keeps that answer; a different target clears it. Preference changes clear record/error,
cancel work and restore saved content for the committed language. The admitted-record display hook
matches the Mac session's call, not proof that a native window painted pixels.

Cancellation advances a revision before asking work to stop. Generation and saved restoration
recheck revision/target; returning to equal text does not revive an older request. A Windows lock
and ordered immutable notifications replace MainActor state publication. Foreign callbacks run
outside the session lock where possible; sync saved-index reads recheck revision after reentrancy.
WPF owners must marshal notifications and reject queued snapshots after their captured
document/focus identity becomes stale. This library never reads UIA, edits text or saves a meaning.

Card teardown cancels/unsubscribes and returns immediately. An uncooperative generation or saved
lookup cannot hold a window open. Retired tasks release their own cancellation lifetime when they
finish and cannot republish; the separate app service/runtime drains native work at app shutdown.
The session does not dispose its borrowed service. This distinction preserves the Mac's lightweight
cancel-on-hide behavior without freeing resources another owner still uses.
Worker closures and static async methods retain only a weak reference to the session, matching
Mac's `[weak self]`; they reacquire it only for synchronous guarded publication. Disposal also
clears event subscribers so a suspended foreign callback cannot keep a closed view alive.

The Windows foreign-record boundary also validates saved input ranges and result content before
display, in addition to the source's identity/language checks. Canonical equality cannot authorize
an invalid UTF-16/grapheme range. Unexpected generation failures use a fixed content-free message;
saved lookup failures remain misses and do not start a model. Localization remains deferred.

`LearningExplanationSessionTests` adapts the four original session regressions in
`CotabbyTests/Services/LanguageLearning/LearningExplanationTests.swift`: navigation/late replies,
stop/preferences, refresh retention and response-language restoration. Additional fake-driven cases
cover duplicate requests, equal-target return, saved lookup teardown, reentrant clear, invalid
records, canonical range validation, async restoration and service borrowing. These are state and
lifetime tests, not an asynchronous Swift oracle or a model/desktop test. All six existing pinned
Swift verifiers retain their separately documented pure-policy/capture coverage.

The writing preview now sets a session target, revalidates its original document before request/
display, renders embedded results and clears on hide. Remaining integration includes unit/component
dictionary targets, settings sample test, notebook adoption, endpoint consent/transport, telemetry
and other capture origins.
