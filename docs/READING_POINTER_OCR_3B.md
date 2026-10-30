# Explicit pointer reading through visual OCR

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`.
This connects the explicit pointer command to OCR first and the existing strict UIA route as
fallback. The subsequent [selection module](READING_SELECTION_OCR_3B.md) connects optional
visual-context enrichment over an exact native selection.

## Source and ownership mapping

| Committed Mac source/test | Windows mapping |
| --- | --- |
| `Services/LanguageLearning/LearningReadingSourceReader.swift`: `readPointer`, `capturePointer`, origin/security validation | `App/Reading/NativeReadingVisualPointerReader.cs`, `ReadingSourceReader`, `Win32/Reading/Win32ReadingWindowSource.cs`, `Focus/UiaReadingScreenSecurity.cs` |
| `Services/Visual/WindowScreenshotService.swift`: pointed-window identity/frame and current-region validation | `Win32/Reading/ReadingWindowTopology.cs`, native window-source lease |
| `Services/LanguageLearning/LearningReadingVisualReader.swift`: pointer candidate, preferred result, one frame/two crops | `App/Reading/ReadingVisualReader.cs` |
| `Support/LanguageLearning/LearningReadingVisualPolicy.swift` | `Core/LanguageLearning/Reading/ReadingVisualPolicy.cs` |
| `Support/LanguageLearning/LearningReadingOCRColumns.swift`, `LearningReadingOCRLayout.swift` | `Core/LanguageLearning/Reading/ReadingVisualLayout.cs` |
| `Support/LanguageLearning/LearningReadingOCRPolicy.swift`, existing target/context policies | `Core/LanguageLearning/Reading/ReadingVisualContext.cs`, existing `ReadingContextPolicy.cs` |
| `LingroveTests/Support/LanguageLearning/LearningReadingVisualPolicyTests.swift`, `LearningReadingOCRPolicyTests.swift`, `LearningReadingOCRColumnsTests.swift` | `Core.Tests/LanguageLearning/ReadingVisualPolicyTests.cs` source-derived cases |
| `LingroveTests/Services/LanguageLearning/LearningReadingVisualReaderTests.swift` pointer cases | `App.Wpf.Tests/ReadingVisualPointerTests.cs`, `ReadingWindowSourceTests.cs` synthetic acquisition/lifetime cases |

Mac production paths above are relative to `Lingrove/`. C# source-derived cases do not execute
Swift or the Mac application and do not replace the separately pinned historical fixture suites.

AppHost composes the existing native selection adapter and this visual adapter through one
`ReadingSourceReader`. The existing window-source owner is shared with explicit region reading;
the origin tracker, keyboard observer, UIA MTA, task destinations and Store are unchanged.
Passive selection capture still takes only the fast UIA path. Pixels are read only after an
explicit enabled pointer command, with the existing per-application permissions and shortcuts.

## Observable behavior and safety

The native adapter freezes the original foreground process/window/focus epoch and independently
retains the actual pointed native top-level window, physical pointer location, display/frame and
scale. A different pointed window in the same application has its own capture bounds; a same-size
replacement cannot reuse its receipt. It requires positive focused and pointed-element password/composition
metadata, exact pointed runtime identity, a real native ancestor belonging to that pointed window,
and the existing sensitive-field ancestor checks. UIA handles stay in Win32 and never enter Core values.
The pointer may address read-only/static content independently of the focused editable field.
Uncertain metadata remains unavailable. Retiring during acquisition is cancellation, not a missing
evidence reply that can start a different capture. Geometry changes revoke the request.

One original-resolution frame is captured inside the pointed window and display. Its encounter
time freezes when the pixels arrive, independently of later OCR duration. At most
two OCR crops borrow that frame: initially 720 by 240 logical units, then expansion only along
the clipped axes, retaining independently proven sentence boundaries. An empty first crop may
expand to the frozen frame. There is one twelve-second deadline; native/recognition failures may
use UIA while the same origin remains current. User cancellation, later input, security retirement
and changed origin never trigger fallback. An accepted first fragment survives a failed expansion;
two candidates never stitch text or switch to another repeated word.

Spatial hits validate exact line/UTF-16 ranges, letter content, small glyph-relative tolerance and
neighbour ambiguity. Equal envelopes may resolve only the original hyphen/apostrophe compound.
Windows word order maps exact sequential line occurrences, rejecting unobserved letter/number
gaps; offsets never come from a page-wide search. Row/column reconstruction retains original
ranges, repeated-gutter evidence, uniform RTL ordering, CJK/soft-wrap separators, intervening
headings, bounded adjacent rows and the existing surrounding-text budget.

Portable receipts identify a visual pointer occurrence through an opaque origin-window receipt
and anchor inside the frozen capture frame. They carry no field/caret/selection-range authority,
and retain Unknown composition with sequence zero. They authorize only the existing read-only
lookup/translation/favorite flow. The existing task disclosure appears before any recognized text
can reach the selected endpoint. Raw images never reach a model, logs, disk or notebook.

Every crop and source array is cleared after completion/rejection. Accepted native work and OCR
are joined on cancellation; late callbacks cannot publish after retirement. The existing window
owner participates in AppHost shutdown. Performance observations are content-free and opt-in.

## Explicit Windows differences and acceptance

- Screen coordinates use top-left physical pixels. The Mac bottom-left ordering, top/bottom
  expansion and crop-edge rules are reflected at this adapter boundary.
- Windows OCR has no confidence values; missing optional confidence remains absent. It is never
  replaced with a fabricated probability. Script properties come from the existing Windows ICU
  boundary; unknown script evidence fails the visual route rather than guessing row direction.
- The frame also obeys the real Windows OCR maximum dimension before pixels are captured.
  Very high DPI can therefore reduce the available context frame. Original glyph pixels are
  preserved; there is no downsampling to evade that ceiling or the strict 16-million-pixel cap.
- Scale comes from the pointed HWND's [GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow), whose result depends on that window's
  awareness; a window spanning displays can differ from Mac's pointed-screen backing scale.
  Changed geometry/scale cancels. Physical mixed-DPI/negative-origin acceptance remains open.
- GDI refuses crops intersecting visible app-owned windows; it does not claim Mac's compositor
  capture-filter behavior or immunity to GPU/overlay races. Positive UIA security is required even
  when Mac can proceed without Accessibility permission. Missing Windows OCR/language support
  retains the existing strict UIA fallback; no language pack is downloaded automatically.
  Windows resolves the pointed top-level identity with `WindowFromPoint` and `GetAncestor(GA_ROOT)`;
  disabled/hidden windows are not returned by that API. Inconsistent UIA/native ancestry is refused.
- Selection enrichment is connected by the subsequent [selection module](READING_SELECTION_OCR_3B.md).
  It retains its original native selection, bounds/gesture and independent alignment rules;
  it does not substitute a pointer hit for that selection. Provider coverage remains explicitly scoped.

Synthetic automated checks cover pure geometry/UTF-16/column/source rules, one capture/two crops,
same-occurrence preference, original pixels, cancellation, acquisition-time retirement, frozen
point security/scale, fallback admission and late-result clearing. They do not capture the desktop.
Production `ElementFromPoint` hit testing plus sensitive ancestry, actual Windows OCR quality,
Word/Chrome/Codex, IME, overlays, language availability, hardware/DPI and visible accessibility
remain the maintainer's consolidated native acceptance. Final local/CI results are recorded in
the implementation PR; component compilation alone does not establish native parity.

Local evidence: Release solution compilation passed with zero warnings/errors; Core passed
5,130/5,130 and portable App 1,278/1,278. A fresh independent Luna run passed the complete WPF
suite, 1,139/1,139, before the final capture-time and pointed-window identity refinements.
Those refinements rebuilt the affected App/Win32 components without warnings/errors and passed
31 focused reading/window/security tests. The latter include a same-size pointed-HWND replacement
and an encounter time unaffected by later OCR. Final-source full CI is a separate merge gate.
