# Explicit screen-region reading

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`.
This module upgrades the explicit screenshot command beyond the historical verified-field-only
implementation in [reading sources](READING_SOURCES_3B.md). The subsequent
[pointer OCR module](READING_POINTER_OCR_3B.md) connects explicit OCR-first pointer acquisition;
selection visual enrichment remains incomplete. Native desktop acceptance remains outstanding.

## Source and responsibility mapping

| Committed Mac source | Windows implementation |
| --- | --- |
| `Lingrove/Services/LanguageLearning/LearningReadingSourceReader.swift`: `captureSource`, screenshot branch, origin validation, `recognize(region:)` | `App/Reading/NativeReadingScreenReader.cs`, `Win32/Reading/Win32ReadingWindowSource.cs` |
| `Lingrove/Services/Presentation/LearningRegionSelectionPanel.swift`: nonactivating gesture, clear selected area, cancellation before completion | `App/UI/Reading/ReadingRegionPicker.cs`, `App/Reading/ReadingApplication.cs` |
| `Lingrove/Services/Visual/WindowScreenshotService.swift`: `captureRegion`, one-display crop, request-owned pixels, exclusion of app windows | `Win32/Reading/WindowsReadingPixels.cs`, `ReadingWindowTopology.cs` |
| `Lingrove/Support/LanguageLearning/LearningReadingContextPolicy.swift` and reading text/target policies | Existing `Core/LanguageLearning/Reading/ReadingContextPolicy.cs`, `ReadingOcrTextPolicy.cs`, with explicit `Ocr` context method |
| `LingroveTests/Services/LanguageLearning/LearningReadingVisualReaderTests.swift`, `LingroveTests/Support/LanguageLearning/LearningReadingContextPolicyTests.swift`, `LearningReadingTextPolicyTests.swift` and coordinator tests | Existing source-derived reading suites plus new synthetic window-source/receipt/security/picker checks; these are C# observations, not executions of Swift or the Mac app |

The production composition borrows the existing foreground tracker, keyboard observer and UIA MTA.
It introduces one app-owned screenshot-source owner, not another hook, focus tracker, Store or
inference service. It retires and joins this owner before its dependencies at normal shutdown.

## User flow and admission

Existing settings, per-application grants, shortcut matching and task destination disclosure remain
the entry point. An explicitly enabled screen-region shortcut acquires the current external origin
process/window/epoch and window frame. It reads password metadata and focused identity without
asking for TextPattern, field text, a selection, caret or writable state. Positive nonpassword
metadata is required; missing evidence and uncertain editable composition fail closed. A provider in a
different process additionally requires a real native descendant HWND of the origin window.
An existing secure or actively composing focus snapshot rejects acquisition. The native metadata
reader independently requires a successful empty TextEdit active-composition result; without TextEdit,
text/value providers require positive read-only evidence. Only explicitly enumerated non-text
control types can proceed without either text pattern. It reads metadata/range attributes, not
the field value or range text. Unknown custom editing surfaces remain unavailable.

The nonactivating WPF picker covers the enumerated physical virtual-screen bounds. Dragging exposes
a clear selected area inside a dim surround. It closes before returning the crop; right-click or
Escape cancels without OCR or translation. Escape is suppressed only while an active region picker
consumes it. Keyboard changes, focus retirement, origin changes or a changed source frame revoke
the lease; releasing the initiating chord is allowed. The view itself never owns screenshot pixels.

An integral crop must be at least 4 by 4 pixels, inside one actual display and at most 16 million
pixels. The guard checks source/security evidence before and after capture and OCR, and again after
context tokenization. Local Windows OCR retains all recognized lines in the existing reading-order
policy. The existing context resolver receives the entire OCR target and Crop/Crop boundaries;
it never invents surrounding text or a confidence score. The resulting receipt has an opaque
origin-window ID and geometry, empty field ID and Unknown composition, not fabricated editable-field
proof. It grants no write authority. Existing reading routing, lookup/translation, explicit details
and canonical favorites consume this receipt through the same shared owners.

Pixels remain request-owned, are cleared on rejection, late completion and success, and are never
persisted or passed to a model. Only recognized, bounded text follows the user's selected task
profile and visible destination. Accepted native work and OCR are joined during cancellation and
shutdown; a canceled operation does not trigger a second capture or another acquisition route.

## Windows differences and limits

- Mac uses one nonactivating AppKit panel per screen; WPF uses one physically positioned virtual
  desktop surface. Physical crop validation remains per display. Segoe UI/Segoe UI Variable,
  system accent selection border and an 18% dim surround adapt the Mac hierarchy to Windows.
- Display rectangles come from synchronous `EnumDisplayMonitors`, rather than DPI-virtualized
  `GetSystemMetrics`. The callback borrows stack-owned state only for that call; no handle is owned
  by the portable receipt. Mixed-DPI physical interaction remains a final host acceptance item.
- Mac's screenshot service excludes this application's windows from its capture filter. The GDI
  adapter conservatively rejects any crop intersecting a visible app-owned top-level HWND, both
  before and after the copy. It does not capture through that window or claim DWM/GPU race immunity.
  `DwmFlush` is not treated as a session-wide exclusion guarantee.
- The existing Windows OCR envelope remains 16 million pixels, with the runtime's additional
  maximum dimension and installed language limits; Mac's capture service admits 20 million pixels.
  No language pack is installed automatically. Windows OCR exposes no per-line recognition
  confidence. This adapter emits no OCR source-language hint: the existing bounded reading-language
  resolver handles recognized text, retaining an unknown result when evidence is insufficient.
  Windows language evidence does not provide Apple's calibrated probabilities.
- Providers without positive password/identity metadata remain unavailable even when Mac can
  perform an explicit capture without Accessibility permission. Unknown composition is represented
  honestly for a visual receipt; uncertain editable composition, composing input or keyboard changes
  revoke acquisition. This representation does not waive native composition admission.
- Pointer OCR and spatial word alignment are connected by the subsequent
  [pointer module](READING_POINTER_OCR_3B.md), including its one-frame/two-crop policy. Selection
  visual context enrichment remains incomplete. The strict UIA pointer adapter remains a fallback.

## Evidence and remaining acceptance

The synthetic suites cover static controls without TextPattern, positive password refusal, stale
origin/frame/security, no pixels before a gesture, region budgets, late pixel clearing, initiating
chord release, registration retirement, cancellation and joined shutdown. Picker component checks
exercise a clear geometry hole and optionally render only the app-owned visual tree over synthetic
sample content at 96 DPI. They never read the desktop. Existing selection/pointer tests remain
regressions rather than evidence that the missing visual fallback is complete.

Release solution compilation passed with zero warnings/errors. The integrated local suites passed
Core 5,104, portable App 1,278 and Inference 751. Full mutex-serialized WPF passed 1,099/1,099
after the writing-retirement repair and OCR context integration. The subsequent independent IME
review found the uncertain-composition admission gap; after its repair, all four native metadata
checks passed, including an owned native RichEdit with a successful empty active composition and
synthetic Value-only providers requiring positive read-only evidence without reading Value.
Final-source full WPF and CI results remain a separate gate and are recorded when complete.

Full local validation initially found an independent writing replacement retirement race:
`CancelAsync` could return before propagation to a linked token, allowing a completed native call
to be acknowledged after shutdown. `WritingSentenceReplacement` now synchronously revokes its
permit with the published closed state, including retirement inside the caller's permit. Existing
accepted-operation drain checks and a reentrant-retirement regression cover this repair.

Physical Word, Chrome and Codex capture, OCR language availability/quality, focus retention, IME,
multi-monitor negative origins and mixed DPI, screen overlays, Escape/right-click behavior and
visual/accessibility acceptance are reserved for the maintainer's consolidated native acceptance.
No actual user editor, screen, clipboard, model, endpoint or audio device is exercised here.
