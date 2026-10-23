# Original graph canvas component

Reference: stable Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/UI/Vocabulary/VocabularyGraphCanvasView.swift`. This slice adapts the existing AppKit
surface as a compiled WPF component. Notebook mode/chrome, settings controls and AppHost preference
injection are still the next slice; this component is not yet reachable from the notebook UI.

| Original responsibility | Windows file under `App/UI/Vocabulary/Graph/` |
| --- | --- |
| Window lifetime, animation, epoch/frame/pin handoff | `VocabularyGraphCanvas.cs` |
| Native input conversion and pointer/keyboard/camera transitions | `VocabularyGraphCanvas.Input.cs` |
| Dot/edge/label drawing and culling | `VocabularyGraphCanvas.Rendering.cs` |
| Lightweight accessibility nodes and lazy frames | `VocabularyGraphCanvas.Automation.cs` |

## Ownership and state flow

A presentation owns the already ported original layout worker. The canvas borrows it, receives
immutable filtered graph/settings/selection and reports user selection through a callback. The
worker's mutable particles remain off the WPF dispatcher; only detached frames become a canvas
position dictionary. The canvas owns no Store, runtime, image, clipboard or endpoint.

One tracked animation task configures the worker then requests frames while running or dragging,
with the original 16ms cadence. Each UI delivery checks its epoch. Pending pointer/final-release
positions override older frames until the matching release serial is acknowledged. The final
input check and publication of idle happen in the same UI callback: a new drag cannot be lost
between cooling and optional Background cleanup. A stale callback cannot acknowledge a newer
release. CTS cancellation tolerates a worker that already disposed its token source.

Hide, unload, minimize and retirement cancel the loop, advance the epoch, and suspend the worker
with any final drag coordinate. Suspend releases particles while preserving the original bounded
coordinate cache. Retirement joins tracked work without requiring optional dispatcher cleanup to
run after shutdown. Ordinary reload/hide retains the presentation/camera; no extra worker is built.

Click/drag threshold (>3 DIPs), nearest hit (<22), grab offset, empty-area pan, headword toggle,
exact sense activation, Tab/Shift+Tab traversal, Enter/Space, Escape, +/- and arrow camera commands
retain the source transitions. Explicit search/selection navigation disables settling fit so the
next frame cannot undo it. Camera operations do not heat or modify particles. Routed WPF events
convert input into these same transitions; controlled transition tests are not OS input injection.

## Rendering and platform adaptations

Drawing keeps the original fixed ownership edges, highlight/dimming, radius clamps, label gates,
culling and 152x36 label box. It uses existing theme brushes, Windows system fonts and WPF DIPs;
there are no per-particle visual controls. Glyph/pixel identity with AppKit is not claimed.

AppKit's natural paragraph direction is adapted using Windows ICU's Unicode first-strong direction,
cached per projected node, rather than forcing Arabic labels into an LTR paragraph. The narrow
`Win32/Text/WindowsTextBaseDirection.cs` boundary passes an explicit UTF-16 length to the System32
ICU function and retains no native handle/pointer. Neutral or unavailable results inherit the UI
direction; missing ICU and multi-paragraph/cross-platform glyph layout remain explicit provider/
visual verification limits. See the primary [ICU base-direction contract](https://unicode-org.github.io/icu-docs/apidoc/released/icu4c/ubidi_8h.html).

WPF wheel detents map to pointer-anchored zoom (one detent = three source scroll units); Shift-wheel
pans horizontally. WPF does not expose AppKit's precise-scroll flag through this event. Native
precision-touchpad pan/magnify and covered-window occlusion remain gaps; visibility/minimization
checks do not establish compositor occlusion detection. No synthetic substitute is claimed.

Virtual button peers keep stable headword/sense identity, source labels and lazy DPI-aware screen
bounds. Explicit canvas references are weak; framework peer-connection/cache lifetimes are not
claimed to be a GC guarantee. Retired/removed nodes reject invocation. Normal UIA property/focus
requests are marshalled by WPF's [ElementProxy](https://source.dot.net/PresentationCore/MS/Internal/Automation/ElementProxy.cs.html)
through [ElementUtil.Invoke](https://source.dot.net/PresentationCore/MS/Internal/Automation/ElementUtil.cs.html)
to the peer's dispatcher. Direct peer APIs remain dispatcher-only; a direct pattern Invoke also
marshals and rechecks mutable state on that dispatcher. External UIA/Narrator behavior still needs
an actual client/Windows host test.

## Evidence and limits

Thirteen compiled WPF cases use the actual original Grape worker and a real owned test window.
They cover cooling/idle, rendering to a memory bitmap, click/sense callbacks, grab-offset drag and
immediate hide, pin precedence over in-flight frames, background pan, camera/keyboard navigation,
search centering, hide/cache/reopen/empty state, minimize/restore, virtual peer identity/bounds/
invocation, retirement, the actual routed KeyDown path, and native Unicode direction samples.
The suite controls points/keys; it does not send global input. The bitmap is not saved or uploaded.
Waits/cleanup are bounded at ten seconds on the existing 30-second STA test fixture.

Independent source review caught and corrected the cooled-loop handoff and fixed-LTR rendering
issues. The Mac canvas itself is not executed by a Swift observation driver here; its rules were
read and ported, while the camera, projection, physics, worker and presentation retain their separate
actual-source corpora. A successful cross-build only compiles WPF; all 476 WPF tests must execute
on Windows CI before this round can merge. Native pointer/keyboard dispatch, Tab focus transfer,
UIA client threading, DPI/compositor behavior and visual comparison remain physical-host gates.

The macOS job budget increases from 35 to 50 minutes after exact-head run40002274082 hit the
hosted-runner limit before all source verifiers finished. Its 41-verifier sibling run40003462033
completed in 22m24s. This changes the job capacity only; no source cases, protocol deadlines,
assertions, native tests or privacy guards are removed or relaxed. The timed-out head is being
rechecked separately and is not counted as a pass.
