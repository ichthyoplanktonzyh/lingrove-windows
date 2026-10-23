# Anki review settings and application ownership

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/UI/Settings/Panes/LearningReviewSettingsSection.swift` and the review composition in
`Cotabby/App/Core/CotabbyAppEnvironment.swift`. This slice adapts the existing grouped form.

## Ownership and flow

`AppHost.Review` owns one `AnkiReviewFeature`, shared settings/controller and an independent
`AnkiConnectClient`/`DpapiAnkiCredentialStore`. Construction performs no credential read, notebook
load, HTTP request or model load. Review preference writes save through the existing settings
owner without reapplying inference settings. The current settings getter follows general-reset
instance replacement. The controller's Configure route selects the existing Learning pane and
scrolls to its review section.

`AnkiReviewSettingsSection` is owned by the settings window, borrows this feature and observes the
same notebook as writing cards and the notebook window. Eager construction of a hidden pane has no
review effects. Loaded **and visible** appearance starts the existing lazy Store load, then reads
the optional key. The masked input saves on submit, focus loss and disappearance. As in Swift,
canonically equivalent unchanged text causes no credential write or connection invalidation;
empty text deletes while whitespace remains a key. Save errors block Test. A successful later
read does not clear a previous key error; a successful changed-key save does. These less obvious
source behaviors are intentional, not improved/reinterpreted recovery rules.

Hiding clears the masked control under an event guard, preserving its interaction draft just as
SwiftUI retains `@State`. If saving fails and the next read also fails, the unsaved key remains
available for retry. Closing saves once, detaches observers and releases the view-owned draft.
No secret is written to preferences or logs. Managed strings cannot promise immediate memory
zeroing; the DPAPI boundary separately clears its plaintext byte buffers.

Test explicitly saves first, then probes the fixed `Lingrove Flexible v1` note type. It does not
change the retained legacy model preference. Progress/busy state, connection/key error priority,
profile, manual deck input, canonical unique/sorted deck picker (including the selected deck),
fixed note type and acknowledged template deletion follow the source. Template deletion ignores
a failure and retains the last acknowledged Store rows. No automatic connection probe occurs.

`AnkiReviewFeature.RunAsync` reserves admitted work before invoking a UI-context callback. Normal
Quit closes settings first, then awaits AppHost cleanup **before** asking WPF to shut down its
dispatcher. The review owner closes admission, cancels and drains its admitted operations and
disposes its HTTP owner before the shared notebook closes. Action failures return to the action
caller without poisoning the drain reservation. A throwing cancellation callback still permits
drain/disposal. WPF `OnExit` alone cannot await cleanup: forced process/OS termination remains a
native lifetime gate, with only best-effort fallback there.

## Evidence and limits

- `verify-anki-settings-interaction-macos-parity.py` compiles the **literal original** private
  `saveKey` method and appearance do/catch in a method wrapper, with recorded synthetic key and
  invalidation effects. Sixteen traces compare exact key/saved-key/error/effect state, including
  NFC/NFD, whitespace, NUL/BOM, failed writes, sticky errors and failed reappearance. It also asserts
  the source's focus/submit/disappear/address/Test wiring. This is not SwiftUI execution, nor a
  replacement implementation of the original settings/controller. Those complete boundaries have
  their own [settings](ANKI_REVIEW_SETTINGS.md) and [controller](ANKI_REVIEW_CONTROLLER.md) oracles.
- Portable App tests compile actual feature/interaction/settings/controller/Store. Sixteen fixture
  traces and seven lifecycle/interaction cases cover inert construction, synchronous failure,
  reservation/reentrant close, cancel/drain/admission, cancellation callback failure, exact saves,
  fixed-model Test ordering and error recovery.
- Six Windows WPF cases exercise the compiled section/theme on its STA dispatcher with real Store
  and isolated SQLite, synthetic Anki/key effects and routed controls. They cover hidden/visible
  lifetime, submit/focus/hide guards, error/retry, fixed model/deck selection including canonical
  spelling, busy cancellation, and acknowledged/rejected template deletion. macOS can compile this
  assembly but cannot execute it. Final-head Windows CI is required.

Actual DPAPI has its separate [native tests](ANKI_CREDENTIAL_STORAGE.md). This component evidence
does not establish physical focus/tab/IME behavior, real Anki integration, system shutdown,
accessibility/pixel parity, cross-process restart, or a usable Windows desktop acceptance pass.
This slice does not expose review navigation: the controller's existing-app activation callback
still needs a Windows adapter before a tray/notebook navigation entry point is connected. Review
card preparation/editing, send/session/conflict handling, media upload and speech remain later
slices. No endpoint is contacted by tests, and no installer, model weight or release artifact is
produced.
