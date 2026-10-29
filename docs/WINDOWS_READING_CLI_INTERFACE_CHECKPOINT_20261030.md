# Reading, automation and interface composition

This round uses Mac `3b53402f2cbc196b0398d133177fd80006d165b2`. The source paths,
platform differences and component evidence are recorded in
`READING_SOURCES_3B.md`, `VOCABULARY_CANONICAL_CLI.md` and
`INTERFACE_LANGUAGE_PERFORMANCE_3B.md`.
Historical fixtures retain their own pins. No old notebook files are admitted or converted.

AppHost now attaches selection/pointer affordances, explicit field-bounded screenshot OCR,
contextual details records and content-free reading observations to its existing reader,
explanation service, canonical Store and shared speech controller. It owns one observation-only
mouse monitor, retires the reading application first and then joins/disposes native owners.
The reviewed source module does not establish arbitrary-screen capture or secure-field access.
Actual OCR, DWM/GPU/windowless occlusion, IME and host-specific interaction remain native checks.

The local CLI mailbox borrows that exact canonical Store. Its tracked worker identifies this
executable and starts one private, event-driven server; construction does not load a notebook.
Shutdown joins setup, closes mailbox admission and drains its claimed request before closing
notebook producers and the Store. Strict `-lingrove-cli` background startup suppresses the
welcome window for that launch without editing first-use preferences. Explicit CLI installation
is available in General; actual installed-command/background app startup remains unverified.

App startup loads one settings snapshot, freezes interface language before any window is built,
and injects the same Store/snapshot into AppHost. Automatic first-use opening requests the
versioned resume state; manual opening starts normally. General reset preserves this app state.
Restart drains accepted work, releases the UI-owned single-instance mutex and starts the same
executable without the CLI flag. A failed drain or launch produces an explicit message.

One UI-owned request history is injected into speech and the settings pane. Completion,
endpoint completion, writing translation, explanation, expression assistance, reading translation
and OCR feed its bounded collector. Central settings application synchronously replaces the
collector's opt-in gate, including General reset; previously accepted observations may finish.
After task owners and the completion coordinator drain, shutdown detaches observers and flushes
the collector before disposing services. Normal observations contain task/model IDs, outcomes
and times, not typed text, prompts, pixels or credentials. CPU/working-set sampling remains
visible-only; child-process and GPU totals are unavailable.

Integration validation before independent composition review: Release solution build; serialized
WPF reading/interface/history 99/99 and portable onboarding 2/2. A duplicate identical native
retirement test from the backport and full source merge was removed; its assertion remains once.
The Inference test project now includes the onboarding partial required by the reset projection.
Final-head complete test results and CI are recorded in the PR. These are synthetic/fake tests,
not physical desktop acceptance. No real user notebook, editor, screen, clipboard, endpoint,
model or Anki instance was used. Final native acceptance remains with the maintainer.
