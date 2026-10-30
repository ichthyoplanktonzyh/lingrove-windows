# Literal Unicode expression triggers

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`. This upgrades the
ASCII-trigger restriction in the older [native receipt](EXPRESSION_EDITOR_RECEIPT.md) and
[application composition](EXPRESSION_ASSIST_APPLICATION.md), without changing their historical pins.

## Committed reference

- `Lingrove/Services/ExpressionAssist/ExpressionAssistEditor.swift`: capture the exact trigger
  immediately before a collapsed caret; retain the original selection across the panel round trip;
  resume with the original field/value/source and delegate to shared range replacement.
- `Lingrove/Services/LanguageLearning/LearningSelectionReplacement.swift` and
  `Lingrove/Support/LanguageLearning/LearningSelectionAcknowledgement.swift`: select the
  explicit range once, acknowledge with seven 45 ms observations, dispatch once and observe the
  full expected value with five 60 ms observations. Never retry or roll back uncertain content.
- `Lingrove/App/Coordinators/InlineFeatures/ExpressionAssistCoordinator.swift`: accepting a candidate
  replaces the capture; canceling a symbol invocation removes its marker, while shortcut cancellation
  preserves the selected source.
- `LingroveTests/App/Coordinators/InlineFeatures/ExpressionAssistCoordinatorTests.swift`,
  `LingroveTests/Support/ExpressionAssist/ExpressionAssistPolicyTests.swift` and the shared
  selection acknowledgement/replacement tests supply the cancel, fullwidth alias, stale/late reply
  and range-acknowledgement behavior. Windows tests are source-derived C# observations, not Swift
  execution or proof of physical editor behavior.

## Windows composition and transaction

The existing trigger allowlist, disabled-by-default settings, fullwidth `？？` alias for default `??`,
panel, candidate actions, keyboard behavior, state presentation and task destination remain the same.
There is no new window, provider, runtime, global hook, Store or clipboard owner. The existing
Windows/WPF visual adaptation and its rendered evidence remain applicable.

`ExpressionReplacementPlan` computes the exact expected value for Unicode triggers and explicitly
requests native range selection. It never represents UTF-16 length as a Backspace count. The native
capture entry accepts triggers already admitted by the same punctuation/symbol policy.

The existing session still proves two stable original-host observations and takes the shared edit
gate. Any clipboard lease is prepared before selection and retained until the transaction completes.
The retained panel receipt's composition version and the ordinary focus owner's composition version
are independent; each is checked against its own owner.

`UiaExpressionRangeCommit` runs on the same UIA MTA. It rechecks the complete original value,
selection, field, focus, composition, scope and retained version, then atomically consumes the retained
generation. Its old subscriptions are released before selection so a self-generated selection/text
event cannot be mistaken for a new user action. This is a one-way transfer, not a notification-ignore
window: a competing prior notification wins or retires the scope, and the subsequent transaction
independently checks fresh field/value/selection and live composition/foreground admission.

The commit borrows the same exact-prefix range finder and acknowledgement loop as writing range
replacement. Whole-prefix start anchors distinguish repeated Unicode text without converting UTF-16
offsets into UIA Character counts. It selects once, positively observes the selected range and invokes
the existing guarded KeySender at most once. On cancel, one Backspace deletes that positively
acknowledged nonempty selection; the marker's code-unit length is never used as a key count.
Mac observes both AX selection and its focus snapshot. The Windows MTA instead observes the actual
UIA selection and separately rechecks focused RuntimeId; the cached focus caret is not range
authority after the programmatic Select. Provider-specific asynchronous publication remains native
acceptance, not evidence of pixel or platform equivalence.

After dispatch the same native task observes the expected complete value, retaining live identity,
composition and scope checks. A second session permit check protects publication of Verified after
an asynchronous return. Refusal never falls back to caret deletion. Partial dispatch, changed
selection, cancellation after a possible effect and clipboard cleanup failure remain Unverified.
The accepted task is joined through session and native-owner shutdown. Its queue and effect admission
budgets retain the existing 500 ms and two-second Windows limits; expiry cannot abandon an already
accepted native action or release its gate/clipboard early.

## Validation and limits

Seven Core planning checks cover ASCII behavior, literal fullwidth/emoji/symbol replacement and
cancel, grapheme/scalar admission, exact expected values and bounded outputs. Forty-eight focused
WPF checks cover the real session and existing views/subscriptions, including Unicode insertion,
cancel, unavailable targets without focus reads, range refusal without fallback, paste ownership and
retirement draining a noncooperative accepted commit.

Five native cases use a hidden owned RichEdit, the real UIA range selector, original subscriptions
and expected-value acknowledgement. A callback synchronously edits only that owned control; repeated
trigger occurrences, emoji, empty replacement, partial dispatch and a retired receipt are covered.
They do not activate an editor, invoke SendInput, read the clipboard or use a model/network.
The dispatcher remains free while the native owner releases providers and joins callbacks.

A fresh Luna/max independent review found no confirmed P1/P2 and independently passed the 43
fake/hidden-owned expression/native checks and seven Core planning checks without build/restore.
The primary also passed all 48 focused checks including the five existing component view tests.
Final source additionally preserves secure/credential admission in the range dispatch permit,
with two controlled changes immediately before dispatch. A second fresh Luna/max final review
found no confirmed P1/P2 and independently passed 45 fake/hidden-owned checks plus seven Core cases.
Integrated Release compilation passed with zero warnings/errors; complete sequential Core 5,108,
portable App 1,278, Inference 751 and final-source WPF 1,117 passed without skips. Remote final-head
CI remains the merge gate.

Actual Word, Chrome and Codex provider behavior, focus return, IME, native keyboard/clipboard delivery,
multi-monitor/DPI and accessibility remain the maintainer's final unified acceptance. Providers without
positive composition, writable exact ranges or complete bounded values remain unavailable; UTF-16
literal mismatch, embedded objects and truncated fields still fail closed. This module does not
upgrade unrelated historical fixture rules or claim complete native parity.
