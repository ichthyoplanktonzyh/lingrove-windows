# Read-only UIA selection providers

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`.
This extends the existing native reading owner to positively read-only TextPattern providers and
focused/pointed ancestors. It grants no insertion, selection mutation, clipboard or model authority.

## Source and ownership

`Lingrove/Services/LanguageLearning/LearningReadingSelectionReader.swift` is the behavior reference:
capture freezes focused/point roots, considers at most seven ancestors under one 250 ms search
budget, retains the selected occurrence separately from the original focus, and rechecks native
range/text/window/focus/real geometry before effects. It does not walk a page or copy selected text.
The existing [selection-context module](READING_CONTEXT_3B.md), [pointer module](READING_POINTER_OCR_3B.md)
and [selection OCR module](READING_SELECTION_OCR_3B.md) remain the consumers.

Windows maps that acquisition to `Win32/Focus/UiaReadingCandidate.cs` and
`UiaReadingSelectionEvidence.cs` on the existing retained UIA MTA. The original focus reference,
provider PID, target element, exact range, text, native geometry and observer lifetime remain
inside the same reading lease. `Win32ReadingSelectionReader` keeps the existing outer process,
foreground/focus epoch, window-frame and keyboard permits. No second listener, focus service,
inference runtime, Store or data format is introduced.

## Admission and flow

- A provider advertising TextEdit still requires the real pattern and a successful empty active
  composition. Pattern errors or active/uncertain composition never become a read-only fallback.
- With TextEdit explicitly unavailable, the focused/root metadata and TextPattern document must
  positively certify read-only text. The cloned and newly returned selected range must also report
  Boolean read-only true **before** selected-range `GetText`. False, mixed, unsupported and unknown
  attributes are rejected. Available Value metadata must be consistent; Value itself is never read.
- Focus and point roots resolve independently before searching. A focused inline fragment can
  lead to its selected parent document. A missing/non-owned focus may use the separately bound
  point root only for a positively read-only target, preserving the original focus absence/result.
  Sensitive or uncertain editable focus does not fall through to another root. Both searches share
  the original 250 ms deadline and each stops after seven parents; no production subtree walk occurs.
- Native HWND ancestry binds every candidate to the exact original source window, including UIA
  providers hosted in another process. The provider's actual PID drives sensitive-path checks;
  the portable receipt still identifies the original application/process and immutable occurrence.
- A chosen candidate obtains text/selection observers and, for TextEdit, composition observers.
  Observer registration is followed by revalidation rather than recapturing a later field.
  Original focused-element identity (or its frozen absence/error), native ownership, positive
  composition/read-only state, selected endpoints, exact UTF-16 text and selected-range rectangles
  are checked again. A same-spelled different range, geometry movement, mode flip, changed focus,
  changed host, positive secure/composing snapshot or later input retires publication.

Source bytes remain bounded by the existing UTF-8/UTF-16 reading rules. BSTR, VARIANT, temporary
range, walker and element references are released on every path; unchosen candidates release their
references before another ancestor is considered. The retained original focused element has its
own reference even when it equals the target. Observers are removed before callbacks/native values
are released; accepted work drains through the original owner on retirement/shutdown.

The same read-only receipt enters the existing contextual lookup/translation/card/favorite flow.
Optional visual enrichment retains its own stronger screenshot/window guard; missing focus may
still make that optional pixel route unavailable even while the native selection/context is usable.
No missing Windows OCR/security capability fabricates geometry or drops native source authority.
Task settings/destination disclosure, unknown-language behavior, UI and canonical persistence stay
with their current owners. There is no cloud sync or older-data conversion.

## Executed evidence and native limits

`UiaReadOnlySelectionTests` uses an actual UIA client against an owned hidden WPF provider exposing
TextPattern without TextEdit or Value. It checks self/inline-parent capture, exact repeated-range
identity, read-only false/unknown document and range metadata with zero selected-text reads,
mode/geometry/permit changes, missing focus plus independent point root, later focus appearance,
sensitive/uncertain focus refusal, and zero selection/scroll/write effects. Its owned-only test entry
verifies the HWND process and bounds descendant discovery to 64 nodes; production does not use that
subtree helper. Existing owned RichEdit tests retain real TextEdit composition and range regressions.
These checks exercise native UIA marshalling, not another application's provider implementation.

The selected-range metadata-before-text ordering was independently identified during review and
repaired before the final validation. A further source comparison upgraded the initially coupled
focus/point roots and pinned a missing focus instead of treating it as positive editable evidence.
The focused native suites passed 45/45. The final solution build completed with zero warnings/errors;
an independent final-source full WPF run passed 1,176/1,176 (zero failed/skipped, 3m53s) through
`scripts/test-wpf-dev.ps1 -Configuration Release -NoBuild -NoRestore`. Final commit CI is recorded
in the PR and remains separate from physical host acceptance.

Physical Word/Chrome/Codex, provider hierarchy/latency, native cross-process hosting, IME, DPI,
scroll/reflow and actual screenshots/OCR remain consolidated native acceptance. A provider still
needs a reliable TextPattern range, positive safe metadata and real window ancestry; unsupported
or uncertain custom editors remain unavailable. No user editor, text, screen, clipboard, model,
endpoint or audio device is exercised by these owned/synthetic checks.
