# Writing sentence and selection replacement

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`. Read with `git show`:

- `Lingrove/Services/LanguageLearning/LearningSelectionReplacement.swift`
- `Lingrove/Support/LanguageLearning/LearningSelectionAcknowledgement.swift`
- `Lingrove/Support/LanguageLearning/LearningReplacementVerificationPolicy.swift`
- `LingroveTests/Services/LanguageLearning/LearningSelectionCaptureTests.swift`
- `LingroveTests/Support/LanguageLearning/LearningSelectionAcknowledgementTests.swift`
- Existing card/coordinator/IA references in [the writing flow](WRITING_TRANSLATION_3B.md).

This completes a single Windows writing-range transaction from explicit capture through the real
card/shortcut, native selection, single dispatch, acknowledgement and recovery. It replaces the
production caret-ending ASCII restriction; ordinary autocomplete/macro/backspace policies retain
their own admission rules. This is not a claim of physical Word/Chrome/Codex acceptance.

## Ownership and flow

The shared focus snapshot now retains selected text, its exact UTF-16 length and a positive
read-only attribute result. Selected text is no longer omitted from the captured document. A
selection with missing/inconsistent evidence remains unavailable. Explicit translation uses the
exact selection; a collapsed caret uses the existing sentence policy, including a caret inside
a Unicode sentence. Text-element boundaries, complete source, field/focus/composition epochs,
and original selection remain part of freshness. The inference payload stays bounded and obeys
the existing explicit per-task destination and draft-reference choice.

`WritingRangeReplacementPolicy` and `WritingRangeSelectionAcknowledgement` own portable rules.
`GuardedRangeTextInserter` borrows the existing `InsertionTransactionGate` and clipboard backend.
It prepares any paste before changing selection, revalidates after preparation, and holds both
gate and clipboard lease until the accepted native action finishes. A concurrent copy, cancelled
request or stale queued capture cannot select or dispatch. Paste consumption and conditional
restoration still use the existing full-format lease. Every input batch contains zero Backspace
events; range length is never interpreted as a number of keystrokes.

`Win32RangeTextInserter` borrows the existing `UiaFocusTracker`/UIA MTA. Its native partial checks
foreground process, runtime ID, focus/composition epoch, full value, original selection and writable
range. For caret scopes it finds complete document prefixes and verifies their document-start
anchors before combining native endpoints. It never chooses the first occurrence of the source
spelling or treats UIA Character units as UTF-16. Already selected source uses its actual native
selection without another selection setter or a FindText requirement.

Selection is set once. The original selection or unavailable receipt can remain pending; unrelated
selection, changed value or revoked safety rejects. Selection ACK uses the source's seven attempts
and 45-ms interval. A final native receipt travels into the input dispatch predicate, and foreground
is checked again after that receipt. One Unicode/paste batch follows; no whole-field setter,
automatic retry or rollback is used.

`WritingSentenceReplacement` runs guarded work off the WPF thread, joins shutdown, and treats dispatch
as unacknowledged until the same complete field equals the exact expected value (five checks at
60 ms). The real card keeps its existing Replace/loading state and shared Segoe UI/theme styling.
An unavailable native range now shows explicit no-write/Copy guidance and disables Replace for
that preview. An uncertain edit retains the existing same-field Copy-only recovery and prevents
another write. New capture resets the appropriate presentation state.

## Windows differences and limits

Mac reconciles AX and its separate FocusTracker selection publications. Windows selects and reads
the actual UIA range on one existing MTA, checks the live global focus epoch and native focused
element at each boundary, and publishes the ordinary shared snapshot during final value ACK. It
does not create a second listener or call the queue-backed focus reader recursively from that MTA.
Windows remains stricter about complete values and positive clear composition evidence. Mac's
retained native-field ACK can survive temporary focus loss; Windows still requires the same live
field/focus/composition receipt.

The native reader retains its existing 65,536-unit full-value admission and refusal of embedded
child objects. Bounded focus windows still carry truncation and cannot authorize replacement.
Providers with unsupported writable attributes, prefix search, selection or composition evidence
remain explicitly unavailable. Queue admission expires after 500 ms; the joined native operation
has a two-second effect deadline and existing per-call UIA timeouts. Expiry revokes later effects,
but never abandons an accepted action while releasing its clipboard/gate. Those are Windows adapter
bounds, not new model budgets.

## Validation

The primary implemented and reviewed this block without delegation. Tests cover exact Unicode and
repeated-source anchors, selected/caret scopes, malformed scalar/grapheme boundaries, freshness,
native read-only/source/selection refusal, final receipt drift, shared gate ownership, concurrent
copy, partial dispatch, cleanup failure, no legacy fallback, acknowledgement and joined retirement.
Native component tests use hidden owned WPF controls with a callback that edits only those controls;
they do not activate another application or call SendInput. Card tests use fake transport/focus and
clipboard, not a model, endpoint or user data. Initial focused groups passed 15 Core and 25 WPF
checks; integrated final-source results are recorded below when complete.

Physical mouse/keyboard, real rich editors and paragraph boundaries, IME, mixed DPI, accessibility,
and actual clipboard/input acknowledgement remain the maintainer's final unified acceptance.

Integrated Release solution compilation passed with zero warnings/errors. The complete suites ran
sequentially with system speech/clipboard tests disabled: Core 5,096; App 1,278; Inference 751;
WPF 1,074. After the final already-selected fast path, foreground recheck and unavailable-state
follow-up, rebuilding through `./scripts/test-wpf-dev.ps1 -NoRestore` passed the full WPF suite
1,075/1,075 in 3m47s, without skips. The primary inspected 96-DPI rendered selected-source and
unavailable-state cards for Unicode/emoji rendering, legibility, action hierarchy and wrapped
recovery guidance. These are actual component renders with synthetic data, not desktop capture;
there is no corresponding Mac screenshot comparison. Final-head CI remains a separate merge gate.
