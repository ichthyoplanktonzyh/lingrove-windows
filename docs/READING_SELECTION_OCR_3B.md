# Selected-text visual context enrichment

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`.
This completes the explicit selection-enrichment path over the existing native selection adapter.
It does not establish universal Windows provider support or native desktop acceptance.

## Committed source and Windows mapping

| Mac source (relative to `Lingrove/`) | Windows mapping |
| --- | --- |
| `Models/LanguageLearning/LearningReadingSelection.swift` exact selection, real bounds, ID-bound gesture | `Core/LanguageLearning/Reading/ReadingSelection.cs`, `Win32/Focus/UiaReadingSelectionBounds.cs`, existing native lease |
| `Support/LanguageLearning/LearningReadingGesturePolicy.swift` | `Core/LanguageLearning/Reading/ReadingGesturePolicy.cs` |
| `Support/LanguageLearning/LearningReadingSelectionOCRPolicy.swift` | `Core/LanguageLearning/Reading/ReadingSelectionOcrPolicy.cs` |
| `Support/LanguageLearning/LearningReadingContextPolicy.swift` original window and trimmed target distinction | `Core/LanguageLearning/Reading/ReadingContextPolicy.cs`, `ReadingContext.Window` |
| `App/Coordinators/InlineFeatures/LearningReadingCoordinator.swift` completed drag/double-click binding | `App/Reading/ReadingAffordanceCoordinator.cs` |
| `Services/LanguageLearning/LearningReadingSourceReader.swift` `enrich`, optional failure, adopted-window lifetime | `App/Reading/ReadingSelectionVisualEnricher.cs`, `ReadingSourceReader`, existing window-source owner |
| `Services/LanguageLearning/LearningReadingVisualReader.swift` `resolveSelection`, independent alignment/two-pass preference | `App/Reading/ReadingVisualReader.Selection.cs` |

The source-derived cases adapt `LingroveTests/Support/LanguageLearning/LearningReadingGesturePolicyTests.swift`,
`LearningReadingSelectionOCRPolicyTests.swift` and the selection cases in
`LingroveTests/Services/LanguageLearning/LearningReadingVisualReaderTests.swift`. Core cases use
synthetic text/ranges; WPF cases use owned arrays, native-owner fakes and the production coordinators.
They are C# observations, not execution of Swift or the Mac app.

## User flow and retained authority

Initial/passive capture still reads only the native selected occurrence and metadata. The same
UIA MTA now obtains optional real selected-range rectangles, validating/releasing its bounded
SAFEARRAY and rechecking the retained occurrence. Mouse-position/card anchors never become range
geometry. A completed external drag or exact double click binds its endpoints/time to that
selection UUID after capture; a triple click does not manufacture a double-click receipt.
The fixed Mac freshness, gesture distance, endpoint and search-window rules remain explicit.

Only explicit activation calls `EnrichAsync`. Native bounded context remains first; complete native
context skips pixels altogether. If context is incomplete, valid range geometry or a recent bound
gesture may locate one frame through the existing window-source owner. Capture requires the same
foreground epoch/window frame plus the independently frozen pointed-window/display/security proof.
Acquisition never replaces the selected text, UUID, selection anchor, composition evidence, source
application, encounter time or native read authority. It never reacquires a later selection.

The visual reader reuses one original-resolution frame for at most two crops and a shared twelve
second deadline. Each crop independently aligns the original native selected text. Exact repeated
occurrences require matching spatial evidence; ambiguity in an expanded crop discards the first
candidate, and a shifted target cannot replace it. Canonical normalization/soft-wrap equivalence
maps to whole original graphemes and exact source ranges. Highlight-induced OCR misspelling may be
aligned only with a precise word rectangle and exact adjacent native text; it is not spelling repair.
Coarse text-run boxes need unique occurrence and neighbouring native evidence; a page frame is insufficient.

The bounded original acquisition window retains every selected UTF-16 unit, including selected
trailing whitespace/newlines. Its visible sentence target is separately trimmed by the original
context rules. `ReadingContext.Window` is memory-only and `JsonIgnore`: prompts keep using the
bounded visible context, not an extra raw acquisition payload. Exact native context wins ties or
conflicting neighbours. Only compatible, genuinely wider or complete context is adopted.

An adopted result retains both the original native lease and its visual guard. Either source can
revoke publication; event subscription during adoption cannot strand a newly retired guard.
Retirement publishes the closed state before asynchronous cancellation. Disposal joins native work,
uncooperative late OCR, cancellation and adopted guards, including reentrant invalidation callbacks.
The activation key's legitimate release remains distinct from later input. Every source/crop array
is cleared after its accepted operation finishes. Optional OCR failure leaves the exact native
selection/context usable; stale origin, active retirement or user cancellation cannot publish.

The existing card, translation/lookup destinations, source/target language handling, explanation,
speech and canonical favorite Store consume the richer context without another service owner.
No new model, hook, focus tracker, archive, endpoint, preference or persistent image is introduced.

Mac's coordinator shows a small progress affordance while preparing selection context. Windows
borrows the existing nonactivating icon, with a theme-colored indeterminate ring and localized
accessible preparation name. The pixel-ready callback is awaited on the UI dispatcher: the GDI
frame finishes before showing this app-owned window, and no further screenshot is taken. The
surface is kept away from the frozen point; if monitor-edge placement would cover that point it
stays hidden. Session completion/cancellation hides its own preparation token, so an old task
cannot hide newer progress. The disabled affordance offers no action and takes no keyboard focus. Own-component
96-DPI renders were inspected; mixed-DPI placement and assistive-technology acceptance stay native gates.

## Windows differences and explicit limits

The [pointer module](READING_POINTER_OCR_3B.md) records top-left physical coordinates, frozen scale,
Windows OCR size/language/optional-confidence limitations, native ancestor checks and GDI overlay
differences. Selection uses those same boundaries. A missing stable origin-window frame disables
optional visual enrichment; a moved frame retires a native receipt. Missing/late/invalid range
rectangles remain absent rather than being replaced with a whole field or presentation position.

The subsequent [read-only provider adapter](READING_READONLY_PROVIDERS_3B.md) adds positive
TextPattern-only read-only documents and bounded ancestor roots. Editable targets still require
real TextEdit composition evidence. This does not prove all Word/Chrome/Codex surfaces work;
unavailable ranges or uncertain editable composition remain refused.
Native UIA geometry, WinOCR/highlight quality, IME, language packs, scroll/window changes, overlays,
mixed DPI/negative origins and visible accessibility remain consolidated host acceptance.

The original Mac bound of 64 spatial seeds/canonical occurrences is retained. Exhausted/ambiguous
alignment leaves native context intact; the adapter does not silently loosen this source rule.
No user editor, screen, clipboard, model, endpoint or audio device is used by the automated checks.
No installer, release or automatic model/language-pack download is produced.

Final local/independent test results and exact-head remote CI are recorded in the implementation
PR. Compilation and synthetic component checks alone do not establish native desktop parity.

Local Release solution compilation passed with zero warnings/errors, Core 5,142/5,142, portable
App 1,278/1,278 and Inference 751/751. Initial full independent WPF found a legitimate activation
metadata gap: the primary key was armed in the down state before transfer to the up state, while
the new guard could read only the latter. The getter now preserves both states; the original
regression expectation was retained. Later focused checks cover external cancellation and
CancelAsync with deliberately delayed linked-token propagation, post-capture progress, dispatcher
handoff and cleanup, owner-token replacement and the corrected native key/window-frame behavior.
After those repairs and preparation-state integration, a fresh independent Luna run passed the
complete final-source WPF suite, 1,162/1,162, and found no static blocker. Own-component ring
rendering was inspected at 96 DPI. Remote CI remains the separate exact-head merge gate.
