# Current writing translation flow

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Lingrove/App/Coordinators/InlineFeatures/LearningSentencePreviewCoordinator.swift`,
`Services/Presentation/LearningSentencePanel.swift`, `UI/InlineFeatures/LearningSentenceCard.swift`,
`Support/LanguageLearning/LearningCommittedTextChangePolicy.swift`,
`LearningAutomaticLanguagePolicy.swift`, and their coordinator/policy/card tests.
The older [manual read-only slice](LEARNING_PREVIEW.md) retains its historical fixture references.
This flow merged in #143 after all four final-head CI jobs passed at
`d5f0e52a54aefad88a16965424e6de68d4e6eef4`; physical editor acceptance remains separate.

## Current application wiring

The existing App-owned controller observes committed complete-field mutations and waits 600 ms
before automatic capture. Translation still defaults off; the separate automatic-preview choice
defaults on. Navigation, uncertain composition, field switches, secure/truncated snapshots and
competing inline features do not authorize passive requests. Windows currently limits passive
capture to multiline fields because it cannot yet distinguish every search field reliably.
The previous clear same-field snapshot survives IME preedit: committed changed text can start a
preview, while cancelled preedit returning to that snapshot does not. Windows lacks the complete
marked-range evidence needed for every attribute-only commit supported by Mac.

The manual shortcut toggles expansion on a current document or requests a fresh expanded capture.
Compact and expanded cards retain distinct content hierarchy. The compact header retains learning
actions, as in the committed Mac view; explicit explanation first expands the validated document
and destination disclosure. Escape dismisses a detail before its parent. An explicitly selected
task endpoint may receive automatic requests before the first safe partial opens a card, matching
the source presentation policy. Its configured destination and transmitted scope are disclosed in
settings; Local remains the default. Input is bounded and revalidated at the existing transport gate.

AppHost constructs one `WritingSentenceReplacement`, borrowing the existing conditional inserter,
focus tracker and insertion gate. An explicit replacement withdraws the ghost suggestion and blocks
other inline captures while pending. The guarded path rechecks the direct nonblocking request
permit after queueing, around clipboard preparation and at final native dispatch. Existing paste
format preservation, concurrent user-copy handling and default insertion callers remain intact.
The later [range transaction](WRITING_RANGE_REPLACEMENT_3B.md) now admits exact selected text and
Unicode sentence ranges around a middle caret when the native provider exposes sufficient evidence.
The original caret-ending ASCII policy remains for historical tests and other insertion callers.
No whole rich field is rewritten or retried as a workaround.

Dispatch is not success. The owner checks the same field/epoch/composition receipt and exact expected
whole value up to five times at 60 ms intervals. A verified result closes the card. An uncertain
attempt permanently blocks another write for that document. A separately validated same-field
recovery snapshot can permit explicit Copy of the already displayed source/result; it cannot
authorize another model request, favorite or edit. Further input/focus/settings changes retire that
receipt. Normal ready Copy no longer incorrectly depends on the already retired generation token.
Controller shutdown revokes pending work before joining the borrowed replacement owner.

## Validation and remaining work

The primary continued the preserved interrupted implementation alone and checked it against the
committed sources; no new independent-agent review is claimed. Release compilation passed with
zero warnings/errors. Targeted Core policies/conditional edits passed 14 checks before the additional
IME baseline regression (2/2); native ICU script cases passed 5/5. WPF checks include the actual
controller/card with fake transport, focus, inserter and clipboard: completed-generation Copy,
verified replacement, uncertain-write/no-retry recovery Copy, typing before held dispatch and
revocation of a queued Copy all passed. The transaction/embedded/inline group passed 12/12; the
automatic IME/transaction/embedded group then passed 11/11. These use synthetic text and no actual
editor, clipboard, model, endpoint or audio device.

Draft reference is now connected as a separate explicit choice with the original `learningReferenceMode`
raw strings and safe current-sentence default. Capture retains the nearest 65,536 graphemes per side;
the 3b planner quotes reference as data, binary-searches only reference length and retains the whole
target. Native planning carries the winning token payload with its prompt, including when the last
probe is oversized. Current-sentence mode sends no adjacent draft. Both the settings destination and
card disclose the effective reference choice. Older empty-reference fixtures retain their behavior.

Previous-sentence presentation now retains bounded, window-local source/range/translation evidence.
The old source must still occupy its original range before the current target and the gap must contain
only punctuation/whitespace. Editing an earlier sentence cannot relabel a cached current result as its
previous translation. Settings, field changes and explicit dismissal retire the appropriate history.
Core reference/budget/prompt checks passed 42/42, local native-plan fake checks 16/16, the explicit
reference settings check 1/1, history policy 2/2 and the real controller/card transaction group 8/8.

The integrated source head `4d407856e22e4205eb6c59bf47a1158f3e111408` then compiled with zero
warnings/errors and passed all local suites sequentially: Core 5,062, App 1,278, Inference 745 and
mutex-serialized WPF 1,042, without skips. WPF took 3m38s. Expanded/current-sentence, expanded/draft
and compact/previous-sentence images use the actual controller/window and synthetic HTTP/focus;
the primary inspected them for destination disclosure, hierarchy and legibility. The compact card
keeps its header actions and removes the redundant lower Close action. No corresponding Mac image
was available; these renders do not establish physical desktop or DPI acceptance.

Selected/middle/Unicode source replacement now follows the separately documented range transaction.
Multiple-source language confidence remains unavailable. This does not establish real
Word/Chrome/Codex behavior, rich-editor acknowledgement,
IME compatibility, mixed DPI or accessibility; those remain the final unified native acceptance.
